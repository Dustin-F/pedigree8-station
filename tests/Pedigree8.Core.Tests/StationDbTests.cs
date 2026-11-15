using System;
using System.IO;
using System.Linq;
using Dapper;
using Microsoft.Data.Sqlite;
using Pedigree8.Core;
using Pedigree8.Core.Data;
using Xunit;

namespace Pedigree8.Core.Tests
{
    public sealed class StationDbTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "p8-tests-" + Guid.NewGuid().ToString("N"));
        private readonly StationDb _db;

        public StationDbTests()
        {
            _db = new StationDb(Path.Combine(_dir, "station.db"));
            _db.Initialize();
            DbTime.UtcNow = () => new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc);
        }

        public void Dispose()
        {
            DbTime.UtcNow = () => DateTime.UtcNow;
            SqliteConnection.ClearAllPools();
            Directory.Delete(_dir, recursive: true);
        }

        [Fact]
        public void Initialize_creates_schema_and_is_repeatable()
        {
            _db.Initialize();

            Assert.Equal(StationDb.LatestSchemaVersion, _db.GetSchemaVersion());
            using var connection = _db.Open();
            var tables = connection.Query<string>("SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name").ToList();
            Assert.Contains("spiders", tables);
            Assert.Contains("events", tables);
            Assert.Contains("legacy_records", tables);
            Assert.Empty(_db.CheckIntegrity());
        }

        [Fact]
        public void Foreign_keys_are_enforced()
        {
            var spider = new Spider { Code = "X-01", SpeciesId = "missing-species" };

            Assert.Throws<SqliteException>(() => new SpiderRepository(_db).Insert(spider));
        }

        [Fact]
        public void Invalid_status_is_rejected()
        {
            var speciesId = SeedSpecies();
            var spider = new Spider { Code = "X-01", SpeciesId = speciesId, Status = "lost" };

            Assert.Throws<SqliteException>(() => new SpiderRepository(_db).Insert(spider));
        }

        [Fact]
        public void Overview_reports_last_feeding_hydration_and_molt()
        {
            var (spiderId, enclosureId) = SeedSpiderInEnclosure("YI-01");
            var events = new EventRepository(_db);
            events.Add(new StationEvent { Type = EventTypes.Feeding, SpiderId = spiderId, OccurredOn = "2026-11-10" });
            events.Add(new StationEvent { Type = EventTypes.Feeding, SpiderId = spiderId, OccurredOn = "2026-11-13", Outcome = "refused" });
            events.Add(new StationEvent { Type = EventTypes.Hydration, EnclosureId = enclosureId, OccurredOn = "2026-11-14" });
            events.Add(new StationEvent { Type = EventTypes.Molt, SpiderId = spiderId, OccurredOn = "2026-10-01" });

            var tile = new SpiderRepository(_db).GetOverview().Single();

            Assert.Equal("YI-01", tile.Code);
            Assert.Equal("Y-ENC", tile.EnclosureCode);
            Assert.Equal("2026-11-10", tile.LastFedOn);
            Assert.Equal("2026-11-14", tile.LastHydratedOn);
            Assert.Equal("2026-10-01", tile.LastMoltOn);
        }

        [Fact]
        public void One_tap_event_gets_current_time_and_local_date()
        {
            var (spiderId, _) = SeedSpiderInEnclosure("YI-02");

            var ev = new EventRepository(_db).Add(new StationEvent { Type = EventTypes.Feeding, SpiderId = spiderId });

            Assert.Equal("2026-11-15T10:00:00.000Z", ev.OccurredAt);
            Assert.Equal(DbTime.LocalDate(new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc)), ev.OccurredOn);
        }

        [Fact]
        public void Voided_event_is_hidden_and_restore_brings_it_back()
        {
            var (spiderId, _) = SeedSpiderInEnclosure("YI-03");
            var events = new EventRepository(_db);
            var spiders = new SpiderRepository(_db);
            var ev = events.Add(new StationEvent { Type = EventTypes.Feeding, SpiderId = spiderId, OccurredOn = "2026-11-12" });

            events.Void(ev.Id, "tapped by mistake");
            Assert.Null(spiders.GetOverview().Single().LastFedOn);
            Assert.Equal("tapped by mistake", events.Get(ev.Id)!.VoidReason);

            events.Restore(ev.Id);
            Assert.Equal("2026-11-12", spiders.GetOverview().Single().LastFedOn);
        }

        [Fact]
        public void Deceased_spiders_are_hidden_unless_requested()
        {
            SeedSpiderInEnclosure("LIN-01");
            var speciesId = new LookupRepository(_db).GetSpecies().Single().Id;
            var spiders = new SpiderRepository(_db);
            spiders.Insert(new Spider { Code = "LIN-02", SpeciesId = speciesId, Status = SpiderStatuses.Deceased });

            Assert.Single(spiders.GetOverview());
            Assert.Equal(2, spiders.GetOverview(includeInactive: true).Count);
        }

        [Fact]
        public void Family_links_resolve_offspring()
        {
            var speciesId = SeedSpecies();
            var spiders = new SpiderRepository(_db);
            var mother = new Spider { Code = "F-01", SpeciesId = speciesId, Sex = "female" };
            var father = new Spider { Code = "M-01", SpeciesId = speciesId, Sex = "male" };
            spiders.Insert(mother);
            spiders.Insert(father);
            spiders.Insert(new Spider { Code = "B001-01", SpeciesId = speciesId, MotherId = mother.Id, FatherId = father.Id });

            Assert.Equal("B001-01", spiders.GetOffspring(mother.Id).Single().Code);
            Assert.Equal("B001-01", spiders.GetOffspring(father.Id).Single().Code);
        }

        [Fact]
        public void Sequences_start_at_one_and_increment()
        {
            var sequences = new SequenceRepository(_db);

            Assert.Equal("B001", sequences.NextBroodCode());
            Assert.Equal("B002", sequences.NextBroodCode());
            Assert.Equal("PAIR-000001", sequences.NextPairingCode());
        }

        [Fact]
        public void Migrated_counters_continue_after_the_last_code_issued()
        {
            var speciesId = SeedSpecies();
            var spiders = new SpiderRepository(_db);
            for (var i = 1; i <= 5; i++)
            {
                spiders.Insert(new Spider { Code = $"YI-{i:D2}", SpeciesId = speciesId });
            }

            using (var c = _db.Open())
            {
                c.Execute("INSERT INTO id_sequences (key, next_value) VALUES ('source_line:yi', 5)");
            }

            var sequences = new SequenceRepository(_db);
            Assert.Equal("YI-06", sequences.NextSpiderCode("yi", "YI"));

            spiders.Insert(new Spider { Code = "YI-07", SpeciesId = speciesId });
            Assert.Equal("YI-08", sequences.NextSpiderCode("yi", "YI"));
        }

        [Fact]
        public void Enclosure_found_by_scanned_label()
        {
            var (_, enclosureId) = SeedSpiderInEnclosure("YI-04");
            var enclosures = new EnclosureRepository(_db);
            var enclosure = enclosures.Get(enclosureId)!;

            Assert.Equal(enclosureId, enclosures.FindByScan(QrPayload.Encode(enclosure.QrToken))!.Id);
            Assert.Equal(enclosureId, enclosures.FindByScan("http://192.168.1.5:5173/qr/" + enclosure.QrToken)!.Id);
            Assert.Null(enclosures.FindByScan("not a label"));
        }

        private string SeedSpecies()
        {
            var id = Ids.New();
            using var connection = _db.Open();
            connection.Execute(
                "INSERT INTO species (id, scientific_name, created_at, updated_at) VALUES (@id, 'Phidippus regius', @now, @now)",
                new { id, now = DbTime.Now() });
            return id;
        }

        private (string SpiderId, string EnclosureId) SeedSpiderInEnclosure(string code)
        {
            var speciesId = SeedSpecies();
            var enclosure = new Enclosure { Code = "Y-ENC" };
            new EnclosureRepository(_db).Insert(enclosure);
            var spider = new Spider { Code = code, SpeciesId = speciesId, EnclosureId = enclosure.Id };
            new SpiderRepository(_db).Insert(spider);
            return (spider.Id, enclosure.Id);
        }
    }
}
