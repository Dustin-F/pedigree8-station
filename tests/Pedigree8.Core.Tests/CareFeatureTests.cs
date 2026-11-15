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
    public sealed class CareFeatureTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "p8-care-" + Guid.NewGuid().ToString("N"));
        private readonly StationDb _db;
        private readonly Husbandry _husbandry;
        private readonly SpiderRepository _spiders;
        private readonly string _speciesId = Ids.New();
        private readonly string _l7 = Ids.New();

        public CareFeatureTests()
        {
            DbTime.UtcNow = () => new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc);
            _db = new StationDb(Path.Combine(_dir, "station.db"));
            _db.Initialize();
            _husbandry = new Husbandry(_db);
            _spiders = new SpiderRepository(_db);
            using var c = _db.Open();
            c.Execute("INSERT INTO species (id, scientific_name, created_at, updated_at) VALUES (@id, 'Phidippus regius', @now, @now)",
                new { id = _speciesId, now = DbTime.Now() });
            c.Execute("INSERT INTO life_stages (id, species_id, code, name, created_at, updated_at) VALUES (@id, @s, 'L7', 'L7', @now, @now)",
                new { id = _l7, s = _speciesId, now = DbTime.Now() });
        }

        public void Dispose()
        {
            DbTime.UtcNow = () => DateTime.UtcNow;
            SqliteConnection.ClearAllPools();
            Directory.Delete(_dir, recursive: true);
        }

        private Spider Add(string code)
        {
            var s = new Spider { Code = code, SpeciesId = _speciesId, LifeStageId = _l7 };
            _spiders.Insert(s);
            return s;
        }

        private SpiderOverview Tile(string code) => _spiders.GetOverview(includeInactive: true).Single(t => t.Code == code);

        [Theory]
        [InlineData(6, 7, CareLevel.Good)]
        [InlineData(7, 7, CareLevel.Due)]
        [InlineData(8, 7, CareLevel.Due)]
        [InlineData(14, 7, CareLevel.Attention)]
        [InlineData(15, 7, CareLevel.Overdue)]
        [InlineData(2, 3, CareLevel.Good)]
        [InlineData(3, 3, CareLevel.Due)]
        public void Feeding_colours_follow_the_spiders_own_schedule(int days, int every, CareLevel expected)
        {
            Assert.Equal(expected, StatusRules.Feeding(days, every));
        }

        [Fact]
        public void Premolt_pauses_feeding_status()
        {
            Assert.Equal(CareLevel.Paused, StatusRules.Feeding(20, null, inPremolt: true));
        }

        [Fact]
        public void Schedule_is_saved_used_for_colours_and_undoable()
        {
            var spider = Add("YI-01");
            new EventRepository(_db).Add(new StationEvent { Type = EventTypes.Feeding, SpiderId = spider.Id, OccurredOn = "2026-11-10" });
            Assert.Equal(CareLevel.Attention, Tile("YI-01").FeedingLevel);

            var result = _husbandry.SetSchedule(spider.Id, 7, 2);
            Assert.Equal(CareLevel.Good, Tile("YI-01").FeedingLevel);
            Assert.Equal(2, _spiders.Get(spider.Id)!.MistEveryDays);

            result.Undo();
            Assert.Null(_spiders.Get(spider.Id)!.FeedEveryDays);
        }

        [Fact]
        public void Needs_care_lists_hungry_spiders_but_not_premolt_or_recently_cared_for()
        {
            var fed = Add("FED");
            var hungry = Add("HUNGRY");
            var premolt = Add("PREMOLT");
            var events = new EventRepository(_db);
            foreach (var s in new[] { fed, hungry, premolt })
            {
                events.Add(new StationEvent { Type = EventTypes.Hydration, SpiderId = s.Id, OccurredOn = "2026-11-15" });
            }

            events.Add(new StationEvent { Type = EventTypes.Feeding, SpiderId = fed.Id, OccurredOn = "2026-11-15" });
            events.Add(new StationEvent { Type = EventTypes.Feeding, SpiderId = hungry.Id, OccurredOn = "2026-11-05" });
            events.Add(new StationEvent { Type = EventTypes.Feeding, SpiderId = premolt.Id, OccurredOn = "2026-11-05" });
            _husbandry.SetPremolt(premolt.Id, on: true);

            var needs = _spiders.GetOverview().Where(s => s.NeedsCare).Select(s => s.Code).ToArray();

            Assert.Equal(new[] { "HUNGRY" }, needs);
        }

        [Fact]
        public void Measurements_build_a_growth_history_with_instar()
        {
            var spider = Add("YI-01");
            _husbandry.AddMeasurement(spider.Id, 6.04);
            var undo = _husbandry.AddMeasurement(spider.Id, 6.5);

            var history = _husbandry.GetMeasurements(spider.Id);
            Assert.Equal(2, history.Count);
            Assert.Equal(6.5, history[0].BodyLengthMm);
            Assert.Equal("L7", history[0].Instar);
            Assert.Equal(6.0, history[1].BodyLengthMm);

            undo.Undo();
            Assert.Single(_husbandry.GetMeasurements(spider.Id));
        }

        [Fact]
        public void Photos_are_stored_as_files_and_can_be_removed_and_restored()
        {
            var spider = Add("YI-01");
            var photos = new Photos(_db);
            var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 0xFF, 0xD9 };

            photos.Add(spider.Id, jpeg);
            var photo = photos.ForSpider(spider.Id).Single();
            Assert.Equal(jpeg, File.ReadAllBytes(photo.FullPath));

            var removed = photos.Remove(photo.Id);
            Assert.Empty(photos.ForSpider(spider.Id));
            removed.Undo();
            Assert.Single(photos.ForSpider(spider.Id));
        }

        [Fact]
        public void Version_1_database_is_upgraded_without_losing_data()
        {
            var path = Path.Combine(_dir, "old.db");
            using (var c = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                c.Open();
                var v1 = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "001_initial.sql"));
                c.Execute(v1);
                c.Execute("PRAGMA user_version = 1;");
                c.Execute("INSERT INTO species (id, scientific_name, created_at, updated_at) VALUES ('s', 'P. regius', 'x', 'x')");
                c.Execute("INSERT INTO spiders (id, code, species_id, created_at, updated_at) VALUES ('a', 'OLD-01', 's', 'x', 'x')");
            }

            var upgraded = new StationDb(path);
            upgraded.Initialize();

            Assert.Equal(StationDb.LatestSchemaVersion, upgraded.GetSchemaVersion());
            var spider = new SpiderRepository(upgraded).Get("a")!;
            Assert.Equal("OLD-01", spider.Code);
            Assert.Null(spider.FeedEveryDays);
            Assert.Empty(upgraded.CheckIntegrity());
        }
    }
}
