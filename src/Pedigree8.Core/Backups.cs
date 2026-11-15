using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;
using Pedigree8.Core.Data;

namespace Pedigree8.Core
{
    public sealed class BackupInfo
    {
        public string Path { get; set; } = "";
        public string Kind { get; set; } = "";
        public DateTime CreatedLocal { get; set; }
        public long Bytes { get; set; }
        public int SchemaVersion { get; set; }
        public long Spiders { get; set; }
        public long Events { get; set; }
        public string FileName => System.IO.Path.GetFileName(Path);
        public string Summary => $"{CreatedLocal:d MMM yyyy HH:mm} · {Spiders} spiders · {Events} records · {Bytes / 1024} KB";
    }

    // A backup is one .zip holding station.db (consistent snapshot via SQLite's backup API),
    // the media folder and a manifest with a checksum. Every backup is re-opened and checked after writing.
    public sealed class Backups
    {
        public const string AutoKind = "auto";
        private readonly StationDb _db;
        private readonly string _dataDir;

        public Backups(StationDb db)
        {
            _db = db;
            _dataDir = Path.GetDirectoryName(db.Path)!;
            LocalDir = Path.Combine(_dataDir, "backups");
        }

        public string LocalDir { get; }

        public BackupInfo Create(string targetDir, string kind)
        {
            Directory.CreateDirectory(targetDir);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var zipPath = Path.Combine(targetDir, $"Pedigree8-{kind}-{stamp}.zip");
            for (var n = 2; File.Exists(zipPath); n++)
            {
                zipPath = Path.Combine(targetDir, $"Pedigree8-{kind}-{stamp}-{n}.zip");
            }
            var snapshot = Path.Combine(Path.GetTempPath(), "p8-snapshot-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                using (var source = _db.Open())
                using (var target = new SqliteConnection($"Data Source={snapshot};Pooling=False"))
                {
                    target.Open();
                    source.BackupDatabase(target);
                    target.Execute("PRAGMA journal_mode = DELETE;");
                }

                var (schema, spiders, events) = Inspect(snapshot);
                var manifest = string.Join("\n", new[]
                {
                    "format=pedigree8-station-backup-1",
                    "kind=" + kind,
                    "created_utc=" + DbTime.Now(),
                    "created_local=" + DateTime.Now.ToString("s", CultureInfo.InvariantCulture),
                    "schema_version=" + schema,
                    "spiders=" + spiders,
                    "events=" + events,
                    "sha256=" + Sha256(snapshot),
                });

                var temp = zipPath + ".partial";
                using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
                {
                    zip.CreateEntryFromFile(snapshot, "station.db", CompressionLevel.Optimal);
                    var media = Path.Combine(_dataDir, "media");
                    if (Directory.Exists(media))
                    {
                        foreach (var file in Directory.EnumerateFiles(media, "*", SearchOption.AllDirectories))
                        {
                            var relative = "media/" + file.Substring(media.Length + 1).Replace('\\', '/');
                            zip.CreateEntryFromFile(file, relative, CompressionLevel.Optimal);
                        }
                    }

                    var entry = zip.CreateEntry("manifest.txt");
                    using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                    writer.Write(manifest);
                }

                File.Move(temp, zipPath);
                return Verify(zipPath);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                File.Delete(snapshot);
            }
        }

        // Throws if the backup is unreadable, damaged or from a newer app version.
        public BackupInfo Verify(string zipPath)
        {
            var extracted = Path.Combine(Path.GetTempPath(), "p8-verify-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                Dictionary<string, string> manifest;
                using (var zip = ZipFile.OpenRead(zipPath))
                {
                    var manifestEntry = zip.GetEntry("manifest.txt") ?? throw new InvalidDataException("Not a Pedigree8 Station backup.");
                    using (var reader = new StreamReader(manifestEntry.Open()))
                    {
                        manifest = reader.ReadToEnd().Split('\n')
                            .Select(l => l.Split(new[] { '=' }, 2)).Where(p => p.Length == 2)
                            .ToDictionary(p => p[0].Trim(), p => p[1].Trim());
                    }

                    var dbEntry = zip.GetEntry("station.db") ?? throw new InvalidDataException("Backup has no database.");
                    dbEntry.ExtractToFile(extracted);
                }

                if (!manifest.TryGetValue("sha256", out var expected) || expected != Sha256(extracted))
                {
                    throw new InvalidDataException("Backup checksum does not match — the file is damaged.");
                }

                var (schema, spiders, events) = Inspect(extracted);
                if (schema > StationDb.LatestSchemaVersion)
                {
                    throw new InvalidDataException("This backup was made by a newer version of Pedigree8 Station.");
                }

                return new BackupInfo
                {
                    Path = zipPath,
                    Kind = manifest.TryGetValue("kind", out var k) ? k : "",
                    CreatedLocal = manifest.TryGetValue("created_local", out var c)
                        ? DateTime.Parse(c, CultureInfo.InvariantCulture)
                        : File.GetLastWriteTime(zipPath),
                    Bytes = new FileInfo(zipPath).Length,
                    SchemaVersion = schema,
                    Spiders = spiders,
                    Events = events,
                };
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                File.Delete(extracted);
            }
        }

        public IReadOnlyList<BackupInfo> List(string dir)
        {
            if (!Directory.Exists(dir))
            {
                return Array.Empty<BackupInfo>();
            }

            var result = new List<BackupInfo>();
            foreach (var file in Directory.EnumerateFiles(dir, "Pedigree8-*.zip"))
            {
                try
                {
                    result.Add(Verify(file));
                }
                catch (Exception)
                {
                    // Damaged or foreign files are simply not offered for restore.
                }
            }

            return result.OrderByDescending(b => b.CreatedLocal).ToList();
        }

        public BackupInfo? LatestLocal() => Directory.Exists(LocalDir)
            ? Directory.EnumerateFiles(LocalDir, "Pedigree8-*.zip").OrderByDescending(File.GetLastWriteTimeUtc)
                .Select(f => new BackupInfo { Path = f, CreatedLocal = File.GetLastWriteTime(f), Bytes = new FileInfo(f).Length })
                .FirstOrDefault()
            : null;

        // Makes an automatic backup when the newest one is older than the interval, keeping the newest `keep`.
        public BackupInfo? AutoBackupIfDue(TimeSpan interval, int keep)
        {
            Directory.CreateDirectory(LocalDir);
            var autos = Directory.EnumerateFiles(LocalDir, $"Pedigree8-{AutoKind}-*.zip")
                .OrderByDescending(File.GetLastWriteTimeUtc).ToList();
            if (autos.Count > 0 && DateTime.UtcNow - File.GetLastWriteTimeUtc(autos[0]) < interval)
            {
                return null;
            }

            var created = Create(LocalDir, AutoKind);
            foreach (var old in Directory.EnumerateFiles(LocalDir, $"Pedigree8-{AutoKind}-*.zip")
                         .OrderByDescending(File.GetLastWriteTimeUtc).Skip(keep))
            {
                File.Delete(old);
            }

            return created;
        }

        // Replaces the current data with the backup. A safety backup of the current data is made first.
        // The app must restart afterwards.
        public BackupInfo Restore(string zipPath)
        {
            var info = Verify(zipPath);
            Create(LocalDir, "before-restore");
            SqliteConnection.ClearAllPools();

            var staging = Path.Combine(_dataDir, "restore-" + Guid.NewGuid().ToString("N"));
            try
            {
                ZipFile.ExtractToDirectory(zipPath, staging);
                foreach (var suffix in new[] { "", "-wal", "-shm" })
                {
                    File.Delete(_db.Path + suffix);
                }

                File.Move(Path.Combine(staging, "station.db"), _db.Path);
                var media = Path.Combine(_dataDir, "media");
                if (Directory.Exists(media))
                {
                    Directory.Delete(media, recursive: true);
                }

                var stagedMedia = Path.Combine(staging, "media");
                if (Directory.Exists(stagedMedia))
                {
                    Directory.Move(stagedMedia, media);
                }
            }
            finally
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, recursive: true);
                }
            }

            return info;
        }

        public static IReadOnlyList<DriveInfo> UsbDrives() =>
            DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Removable && d.IsReady).ToList();

        public static string UsbFolder(DriveInfo drive) => Path.Combine(drive.RootDirectory.FullName, "Pedigree8 Backups");

        private static (int Schema, long Spiders, long Events) Inspect(string dbPath)
        {
            using var c = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False");
            c.Open();
            var integrity = c.ExecuteScalar<string>("PRAGMA integrity_check;");
            if (integrity != "ok")
            {
                throw new InvalidDataException("Backup database failed its integrity check.");
            }

            return (c.ExecuteScalar<int>("PRAGMA user_version;"),
                c.ExecuteScalar<long>("SELECT count(*) FROM spiders"),
                c.ExecuteScalar<long>("SELECT count(*) FROM events WHERE voided_at IS NULL"));
        }

        private static string Sha256(string path)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
