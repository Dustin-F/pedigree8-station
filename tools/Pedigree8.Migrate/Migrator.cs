using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.Sqlite;
using Minio;
using Minio.DataModel.Args;
using Npgsql;
using Pedigree8.Core;
using Pedigree8.Core.Data;

namespace Pedigree8.Migrate;

public sealed record MigrateOptions(
    SourceSettings Source, string? OwnerEmail, string? OrgId, string OutputDir, string TimeZone,
    bool LocalDates, bool AllowOverwriteStationData);

public sealed class Migrator
{
    private static readonly HashSet<string> MetadataColumns = new(StringComparer.Ordinal)
    {
        "organization_id", "updated_at", "recorded_by_user_id", "created_by_user_id", "set_by_user_id", "cleared_by_user_id",
        "promoted_by_user_id", "assigned_by_user_id", "generated_by_user_id", "verified_by_user_id",
    };

    private static readonly HashSet<string> UniqueCodeTables = new(StringComparer.Ordinal)
    {
        "spiders", "enclosures", "lines", "pairings", "broods", "events",
    };

    private readonly MigrateOptions _o;
    private readonly MigrationReport _report = new();

    public Migrator(MigrateOptions options)
    {
        _o = options;
    }

    public MigrationReport Run()
    {
        _report.StartedAt = DbTime.Now();
        _report.TimeZone = _o.TimeZone;
        _report.DateRule = _o.LocalDates
            ? "Calendar dates re-derived from the recorded time in the local time zone."
            : "Calendar dates kept exactly as stored in Pedigree8 (date-only values derived from the time in the local time zone).";

        var buildDir = _o.OutputDir.TrimEnd('\\', '/') + ".building";
        if (Directory.Exists(buildDir))
        {
            Directory.Delete(buildDir, recursive: true);
        }

        Directory.CreateDirectory(buildDir);
        var stationPath = Path.Combine(buildDir, "station.db");

        try
        {
            CheckExistingOutputIsSafeToReplace();

            using var pg = new NpgsqlConnection(_o.Source.PostgresConnectionString);
            pg.Open();
            using var snapshot = pg.BeginTransaction(System.Data.IsolationLevel.RepeatableRead);
            pg.Execute("SET TRANSACTION READ ONLY", transaction: snapshot);

            var org = ResolveOrganization(pg);
            var p = new { org = Guid.Parse(org.Id), tz = _o.TimeZone };
            _report.Organization = org;

            var station = new StationDb(stationPath);
            station.Initialize();
            using (var sqlite = station.Open())
            {
                using var tx = sqlite.BeginTransaction();
                sqlite.Execute("PRAGMA defer_foreign_keys = ON;", transaction: tx);

                foreach (var mapping in Mappings.All(_o.LocalDates))
                {
                    CopyMapping(pg, sqlite, tx, mapping, p, buildDir);
                }

                PostProcess(sqlite, tx);
                ArchiveLegacyRows(pg, sqlite, tx, p);
                WriteSettings(sqlite, tx, org);

                var brokenLinks = sqlite.Query<(string Table, long RowId, string Parent)>(
                    "SELECT \"table\", rowid, parent FROM pragma_foreign_key_check", transaction: tx).ToList();
                if (brokenLinks.Count > 0)
                {
                    foreach (var (table, rowId, parent) in brokenLinks.Take(50))
                    {
                        var id = sqlite.ExecuteScalar<string>($"SELECT id FROM {table} WHERE rowid = @rowId", new { rowId }, tx);
                        _report.Fatal.Add($"Broken link: {table} {id} points to a missing {parent} row.");
                    }

                    throw new InvalidOperationException($"{brokenLinks.Count} records point to missing records; nothing was saved.");
                }

                tx.Commit();
            }

            Validate(pg, station, p);
            BuildDateReview(pg, p);

            _report.Succeeded = _report.Fatal.Count == 0;
            _report.FinishedAt = DbTime.Now();
            RecordRun(station);
            ReportWriter.Write(_report, buildDir);
            SqliteConnection.ClearAllPools();

            if (_report.Succeeded)
            {
                Promote(buildDir);
            }

            return _report;
        }
        catch (Exception ex)
        {
            _report.Fatal.Add($"{ex.GetType().Name}: {ex.Message}");
            _report.Succeeded = false;
            _report.FinishedAt = DbTime.Now();
            SqliteConnection.ClearAllPools();
            ReportWriter.Write(_report, buildDir);
            return _report;
        }
    }

    private void CheckExistingOutputIsSafeToReplace()
    {
        var existing = Path.Combine(_o.OutputDir, "station.db");
        if (!File.Exists(existing) || _o.AllowOverwriteStationData)
        {
            return;
        }

        using var c = new SqliteConnection($"Data Source={existing};Mode=ReadOnly;Pooling=False");
        c.Open();
        var stationRows = c.ExecuteScalar<long>("SELECT count(*) FROM events WHERE source = 'station'");
        if (stationRows > 0)
        {
            throw new InvalidOperationException(
                $"{existing} already contains {stationRows} records entered on the station itself. " +
                "Refusing to replace it. Use --allow-overwrite-station-data only if you are sure.");
        }
    }

    private OrganizationInfo ResolveOrganization(NpgsqlConnection pg)
    {
        const string select = @"SELECT o.id::text AS Id, o.name AS Name, o.slug AS Slug, o.timezone AS TimeZone,
                                       u.email AS OwnerEmail, m.role::text AS Role
                                FROM organizations o
                                JOIN memberships m ON m.organization_id = o.id AND m.archived_at IS NULL
                                JOIN users u ON u.id = m.user_id";
        List<OrganizationInfo> matches;
        if (_o.OrgId is not null)
        {
            matches = pg.Query<OrganizationInfo>($"{select} WHERE o.id = @id ORDER BY m.created_at", new { id = Guid.Parse(_o.OrgId) })
                .GroupBy(x => x.Id).Select(g => g.First()).ToList();
        }
        else
        {
            matches = pg.Query<OrganizationInfo>($@"{select}
                WHERE lower(u.email) = lower(@email)
                  AND coalesce(o.slug, '') NOT LIKE 'pedigree8-demo-lab%' AND o.name NOT LIKE 'DEMO LAB%'
                ORDER BY m.created_at", new { email = _o.OwnerEmail }).ToList();
        }

        if (matches.Count != 1)
        {
            var found = matches.Count == 0 ? "none" : string.Join(", ", matches.Select(m => $"{m.Name} ({m.Id})"));
            throw new InvalidOperationException($"Expected exactly one lab to migrate, found {found}. Use --org-id to choose.");
        }

        return matches[0];
    }

    private void CopyMapping(NpgsqlConnection pg, SqliteConnection sqlite, SqliteTransaction tx, Mapping mapping, object p, string buildDir)
    {
        var rows = pg.Query(mapping.Select, p).Cast<IDictionary<string, object?>>().ToList();
        var stat = _report.Table(mapping.SourceTable);
        stat.Target = mapping.TargetTable;
        stat.Description = mapping.Description;
        stat.FoldedTables = mapping.FoldedTables;
        stat.ConsumedColumns = Regex.Matches(mapping.Select, @"\bx\.([a-z_]+)").Select(m => m.Groups[1].Value).ToHashSet();

        if (rows.Count == 0)
        {
            return;
        }

        var isMedia = mapping.TargetTable == "media";
        var hasCode = UniqueCodeTables.Contains(mapping.TargetTable);
        var takenCodes = hasCode
            ? sqlite.Query<string>($"SELECT code FROM {mapping.TargetTable} WHERE code IS NOT NULL", transaction: tx).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>();

        foreach (var row in rows)
        {
            if (hasCode && row["code"] is string code && !takenCodes.Add(code))
            {
                if (mapping.TargetTable == "events")
                {
                    row["code"] = null;
                    _report.Warnings.Add($"{mapping.SourceTable} {row["id"]}: duplicate event code {code} cleared (kept in archive).");
                }
                else
                {
                    var renamed = $"{code}-{row["id"]!.ToString()![..4]}";
                    row["code"] = renamed;
                    takenCodes.Add(renamed);
                    _report.Warnings.Add($"{mapping.SourceTable} {row["id"]}: code {code} already used, stored as {renamed}.");
                }
            }

            if (isMedia && !DownloadMedia(row, buildDir))
            {
                stat.Skipped++;
                continue;
            }

            var columns = row.Keys.ToList();
            var sql = $"INSERT INTO {mapping.TargetTable} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", columns.Select(c => "@" + c))})";
            try
            {
                var parameters = new DynamicParameters();
                foreach (var column in columns)
                {
                    parameters.Add(column, row[column]);
                }

                sqlite.Execute(sql, parameters, tx);
                stat.Migrated++;
            }
            catch (SqliteException ex)
            {
                stat.Skipped++;
                _report.Errors.Add($"{mapping.SourceTable} {row["id"]}: {ex.Message}");
            }
        }
    }

    private bool DownloadMedia(IDictionary<string, object?> row, string buildDir)
    {
        var id = (string)row["id"]!;
        var storageKey = (string)row["storage_key"]!;
        row.Remove("storage_key");
        var fileName = string.Concat(((string?)row["file_name"] ?? "file").Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var relative = $"media/{id}/{fileName}";
        var fullPath = Path.Combine(buildDir, "media", id, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        try
        {
            var endpoint = _o.Source.MinioEndpoint;
            var client = new MinioClient()
                .WithEndpoint(endpoint.Host, endpoint.Port)
                .WithCredentials(_o.Source.MinioAccessKey, _o.Source.MinioSecretKey)
                .WithSSL(endpoint.Scheme == "https")
                .Build();
            using (var file = File.Create(fullPath))
            {
                client.GetObjectAsync(new GetObjectArgs()
                        .WithBucket(_o.Source.MinioBucket)
                        .WithObject(storageKey)
                        .WithCallbackStream(s => s.CopyTo(file)))
                    .GetAwaiter().GetResult();
            }

            var bytes = File.ReadAllBytes(fullPath);
            row["relative_path"] = relative;
            row["byte_size"] = (long)bytes.Length;
            row["sha256"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            _report.Media.Add(new MediaResult(id, relative, bytes.Length, (string)row["sha256"]!, "copied"));
            return true;
        }
        catch (Exception ex)
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }

            _report.Media.Add(new MediaResult(id, relative, 0, "", "MISSING: " + ex.Message));
            _report.Errors.Add($"File {storageKey} could not be downloaded: {ex.Message}");
            return false;
        }
    }

    private static void PostProcess(SqliteConnection sqlite, SqliteTransaction tx)
    {
        sqlite.Execute(@"
            UPDATE spiders SET mother_id = (SELECT b.mother_id FROM broods b WHERE b.id = spiders.brood_id)
             WHERE mother_id IS NULL AND brood_id IS NOT NULL;
            UPDATE spiders SET father_id = (SELECT b.father_id FROM broods b WHERE b.id = spiders.brood_id)
             WHERE father_id IS NULL AND brood_id IS NOT NULL;
            UPDATE spiders SET hatched_on = (SELECT coalesce(b.hatched_on, b.emerged_on) FROM broods b WHERE b.id = spiders.brood_id)
             WHERE hatched_on IS NULL AND brood_id IS NOT NULL;", transaction: tx);
    }

    private void ArchiveLegacyRows(NpgsqlConnection pg, SqliteConnection sqlite, SqliteTransaction tx, object p)
    {
        var tables = pg.Query<(string Table, bool HasId)>(@"
            SELECT c.table_name, bool_or(c2.column_name IS NOT NULL)
            FROM information_schema.columns c
            JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name AND t.table_type = 'BASE TABLE'
            LEFT JOIN information_schema.columns c2 ON c2.table_schema = c.table_schema AND c2.table_name = c.table_name AND c2.column_name = 'id'
            WHERE c.table_schema = 'public' AND c.column_name = 'organization_id'
            GROUP BY c.table_name ORDER BY c.table_name").ToList();

        var now = DbTime.Now();
        const string insert = "INSERT INTO legacy_records (source_table, source_id, data, migrated_at) VALUES (@table, @id, @data, @now)";

        foreach (var (table, hasId) in tables)
        {
            var idExpr = hasId ? "t.id::text" : "t.organization_id::text";
            var rows = pg.Query<(string Id, string Data)>(
                $"SELECT {idExpr}, row_to_json(t)::text FROM \"{table}\" t WHERE t.organization_id = @org", p).ToList();
            foreach (var (id, data) in rows)
            {
                sqlite.Execute(insert, new { table, id, data, now }, tx);
            }

            var stat = _report.Table(table);
            stat.SourceRows = rows.Count;
            stat.Archived = rows.Count;
        }

        var orgRow = pg.QuerySingle<(string Id, string Data)>(
            "SELECT t.id::text, row_to_json(t)::text FROM organizations t WHERE t.id = @org", p);
        sqlite.Execute(insert, new { table = "organizations", id = orgRow.Id, data = orgRow.Data, now }, tx);

        foreach (var stat in _report.Tables.Where(s => s.Target is not null && s.Target != "id_sequences"))
        {
            sqlite.Execute($@"UPDATE legacy_records SET mapped_to = @target || ':' || source_id
                              WHERE source_table = @source AND source_id IN (SELECT id FROM {stat.Target})",
                new { target = stat.Target, source = stat.SourceTable }, tx);
        }
    }

    private void WriteSettings(SqliteConnection sqlite, SqliteTransaction tx, OrganizationInfo org)
    {
        var settings = new Dictionary<string, string?>
        {
            ["station_name"] = org.Name,
            ["source_org_id"] = org.Id,
            ["source_owner_email"] = org.OwnerEmail,
            ["source_timezone"] = org.TimeZone,
            ["migration_timezone"] = _o.TimeZone,
            ["migrated_at"] = DbTime.Now(),
        };
        foreach (var (key, value) in settings)
        {
            sqlite.Execute("INSERT INTO settings (key, value) VALUES (@key, @value)", new { key, value }, tx);
        }
    }

    private void Validate(NpgsqlConnection pg, StationDb station, object p)
    {
        foreach (var problem in station.CheckIntegrity())
        {
            _report.Fatal.Add("Integrity: " + problem);
        }

        using var sqlite = station.Open();
        var archivedCounts = sqlite.Query<(string Table, long Count)>(
            "SELECT source_table, count(*) FROM legacy_records GROUP BY source_table").ToDictionary(x => x.Table, x => x.Count);

        foreach (var stat in _report.Tables)
        {
            var archived = archivedCounts.GetValueOrDefault(stat.SourceTable);
            if (archived != stat.SourceRows)
            {
                _report.Fatal.Add($"{stat.SourceTable}: {stat.SourceRows} rows in Pedigree8 but {archived} archived.");
            }

            if (stat.Target is null || stat.SourceRows == 0)
            {
                continue;
            }

            var columns = pg.Query<string>(@"SELECT column_name FROM information_schema.columns
                                             WHERE table_schema = 'public' AND table_name = @t ORDER BY ordinal_position",
                new { t = stat.SourceTable });
            foreach (var column in columns.Where(c => c != "id" && !MetadataColumns.Contains(c) && !stat.ConsumedColumns.Contains(c)))
            {
                var withValue = pg.ExecuteScalar<long>(
                    $"SELECT count(\"{column}\") FROM \"{stat.SourceTable}\" WHERE organization_id = @org", p);
                if (withValue > 0)
                {
                    stat.ArchiveOnlyColumns.Add($"{column} ({withValue})");
                }
            }
        }

        var mappedBySource = sqlite.Query<(string Table, long Count)>(
            "SELECT source_table, count(*) FROM legacy_records WHERE mapped_to IS NOT NULL GROUP BY source_table")
            .ToDictionary(x => x.Table, x => x.Count);
        foreach (var stat in _report.Tables.Where(s => s.Target is not null && s.Target != "id_sequences"))
        {
            stat.LinkedInArchive = mappedBySource.GetValueOrDefault(stat.SourceTable);
            if (stat.LinkedInArchive != stat.Migrated)
            {
                _report.Fatal.Add($"{stat.SourceTable}: {stat.Migrated} rows written to {stat.Target} but {stat.LinkedInArchive} linked in the archive.");
            }

            if (stat.Skipped > 0 && stat.Target != "media")
            {
                _report.Fatal.Add($"{stat.SourceTable}: {stat.Skipped} rows could not be written to {stat.Target} (see errors).");
            }
        }

        SpotCheckSpiders(pg, sqlite, p);
    }

    private void SpotCheckSpiders(NpgsqlConnection pg, SqliteConnection sqlite, object p)
    {
        var dateExpr = _o.LocalDates
            ? $"coalesce({Sql.Ld("e.occurred_at")}, {Sql.D("e.occurred_on")}, {Sql.Ld("e.created_at")})"
            : $"coalesce({Sql.D("e.occurred_on")}, {Sql.Ld("e.occurred_at")}, {Sql.Ld("e.created_at")})";
        string Source(string table) => $@"(SELECT count(*) FROM {table} e WHERE e.specimen_id = s.id AND e.voided_at IS NULL)::int,
                                         (SELECT max({dateExpr}) FROM {table} e WHERE e.specimen_id = s.id AND e.voided_at IS NULL)";

        var source = pg.Query($@"
            SELECT s.id::text AS id, s.breeder_code AS code, {Source("feeding_events")}, {Source("hydration_events")}, {Source("molt_events")}
            FROM specimens s WHERE s.organization_id = @org ORDER BY s.breeder_code", p)
            .Cast<IDictionary<string, object?>>().ToList();

        foreach (var row in source)
        {
            var values = row.Values.ToList();
            var id = (string)values[0]!;
            var check = new SpiderCheck { Code = (string)values[1]! };
            long Count(int i) => Convert.ToInt64(values[i]);
            var station = sqlite.QuerySingle<(long F, string? Fd, long H, string? Hd, long M, string? Md)>(@"
                SELECT (SELECT count(*) FROM events WHERE spider_id = @id AND type = 'feeding' AND voided_at IS NULL),
                       (SELECT max(occurred_on) FROM events WHERE spider_id = @id AND type = 'feeding' AND voided_at IS NULL),
                       (SELECT count(*) FROM events WHERE spider_id = @id AND type = 'hydration' AND voided_at IS NULL),
                       (SELECT max(occurred_on) FROM events WHERE spider_id = @id AND type = 'hydration' AND voided_at IS NULL),
                       (SELECT count(*) FROM events WHERE spider_id = @id AND type = 'molt' AND voided_at IS NULL),
                       (SELECT max(occurred_on) FROM events WHERE spider_id = @id AND type = 'molt' AND voided_at IS NULL)",
                new { id });
            check.Feedings = new Metric(Count(2), station.F, (string?)values[3], station.Fd);
            check.Hydrations = new Metric(Count(4), station.H, (string?)values[5], station.Hd);
            check.Molts = new Metric(Count(6), station.M, (string?)values[7], station.Md);
            if (!check.Matches)
            {
                _report.Fatal.Add($"Spot check failed for spider {check.Code}.");
            }

            _report.SpiderChecks.Add(check);
        }
    }

    private void BuildDateReview(NpgsqlConnection pg, object p)
    {
        var sources = new[]
        {
            ("feeding_events", "Feeding", "occurred_at", "occurred_on"),
            ("hydration_events", "Hydration", "occurred_at", "occurred_on"),
            ("molt_events", "Molt", "occurred_at", "occurred_on"),
            ("health_events", "Health", "occurred_at", "occurred_on"),
            ("sex_assessments", "Sex", "assessed_at", "assessed_on"),
            ("mortality_events", "Death", "died_at", "died_on"),
            ("acquisitions", "Acquired", "acquired_at", "acquired_on"),
        };
        foreach (var (table, label, at, on) in sources)
        {
            var rows = pg.Query<DateReviewItem>($@"
                SELECT '{label}' AS Kind, coalesce(s.breeder_code, '(enclosure)') AS Spider, e.id::text AS RecordId,
                       {Sql.D("e." + on)} AS StoredDate, {Sql.Ld("e." + at)} AS LocalDate,
                       to_char(e.{at} AT TIME ZONE @tz, 'YYYY-MM-DD HH24:MI') AS LocalTime
                FROM {table} e LEFT JOIN specimens s ON s.id = e.specimen_id
                WHERE e.organization_id = @org AND e.{at} IS NOT NULL AND e.{on} IS NOT NULL
                  AND {Sql.D("e." + on)} <> {Sql.Ld("e." + at)}
                ORDER BY e.{at}", p);
            _report.DateReview.AddRange(rows);
        }
    }

    private void RecordRun(StationDb station)
    {
        using var sqlite = station.Open();
        sqlite.Execute(@"INSERT INTO migration_runs (id, started_at, finished_at, source, status, report)
                         VALUES (@id, @started, @finished, @source, @status, @report)",
            new
            {
                id = Ids.New(),
                started = _report.StartedAt,
                finished = _report.FinishedAt,
                source = $"Pedigree8 lab {_report.Organization?.Name} ({_report.Organization?.Id})",
                status = _report.Succeeded ? "succeeded" : "failed",
                report = ReportWriter.ToJson(_report),
            });
    }

    private void Promote(string buildDir)
    {
        if (Directory.Exists(_o.OutputDir))
        {
            var previous = $"{_o.OutputDir.TrimEnd('\\', '/')}.previous-{DateTime.Now:yyyyMMdd-HHmmss}";
            Directory.Move(_o.OutputDir, previous);
            _report.PreviousOutputKeptAt = previous;
        }

        Directory.Move(buildDir, _o.OutputDir);
    }
}
