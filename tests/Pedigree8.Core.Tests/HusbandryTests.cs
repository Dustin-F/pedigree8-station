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
    public sealed class HusbandryTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "p8-husbandry-" + Guid.NewGuid().ToString("N"));
        private readonly StationDb _db;
        private readonly Husbandry _husbandry;
        private readonly SpiderRepository _spiders;
        private readonly string _spiderId;
        private readonly string _l7 = Ids.New();
        private readonly string _l8 = Ids.New();
        private readonly string _cricket = Ids.New();

        public HusbandryTests()
        {
            DbTime.UtcNow = () => new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc);
            _db = new StationDb(Path.Combine(_dir, "station.db"));
            _db.Initialize();
            _husbandry = new Husbandry(_db);
            _spiders = new SpiderRepository(_db);

            var speciesId = Ids.New();
            var now = DbTime.Now();
            using (var c = _db.Open())
            {
                c.Execute("INSERT INTO species (id, scientific_name, created_at, updated_at) VALUES (@speciesId, 'Phidippus regius', @now, @now)", new { speciesId, now });
                c.Execute(@"INSERT INTO life_stages (id, species_id, code, name, sort_order, created_at, updated_at) VALUES
                            (@l7, @speciesId, 'L7', 'L7', 7, @now, @now), (@l8, @speciesId, 'L8', 'L8', 8, @now, @now)",
                    new { l7 = _l7, l8 = _l8, speciesId, now });
                c.Execute("INSERT INTO prey_types (id, name, created_at, updated_at) VALUES (@id, 'Cricket', @now, @now)", new { id = _cricket, now });
            }

            var enclosure = new Enclosure { Code = "YI-01-ENC" };
            new EnclosureRepository(_db).Insert(enclosure);
            var spider = new Spider { Code = "YI-01", SpeciesId = speciesId, LifeStageId = _l7, EnclosureId = enclosure.Id };
            _spiders.Insert(spider);
            _spiderId = spider.Id;
        }

        public void Dispose()
        {
            DbTime.UtcNow = () => DateTime.UtcNow;
            SqliteConnection.ClearAllPools();
            Directory.Delete(_dir, recursive: true);
        }

        private SpiderOverview Tile => _spiders.GetOverview(includeInactive: true).Single();

        [Fact]
        public void Fed_logs_today_with_default_prey_and_undo_removes_it()
        {
            var result = _husbandry.Fed(_spiderId);

            Assert.Equal("Fed · YI-01", result.Message);
            Assert.Equal(0, Tile.DaysSinceFed);
            Assert.Equal(_cricket, new EventRepository(_db).GetForSpider(_spiderId).Single().PreyTypeId);

            result.Undo();
            Assert.Null(Tile.LastFedOn);
        }

        [Fact]
        public void Refused_feeding_does_not_count_as_fed()
        {
            _husbandry.Fed(_spiderId, "refused");

            Assert.Null(Tile.LastFedOn);
        }

        [Fact]
        public void Molted_advances_stage_clears_premolt_and_undo_restores_both()
        {
            _husbandry.SetPremolt(_spiderId, on: true);
            Assert.True(Tile.InPremolt);

            var result = _husbandry.Molted(_spiderId);
            Assert.Equal("Molted · YI-01 → L8", result.Message);
            Assert.Equal("L8", Tile.LifeStageName);
            Assert.False(Tile.InPremolt);
            var premolt = new EventRepository(_db).GetForSpider(_spiderId, type: EventTypes.Premolt).Single();
            Assert.NotNull(premolt.UntilAt);

            result.Undo();
            Assert.Equal("L7", Tile.LifeStageName);
            Assert.True(Tile.InPremolt);
            Assert.Null(new EventRepository(_db).GetForSpider(_spiderId, type: EventTypes.Premolt).Single().UntilAt);
            Assert.Null(Tile.LastMoltOn);
        }

        [Fact]
        public void Molting_past_the_highest_known_instar_creates_the_next_one()
        {
            _husbandry.Molted(_spiderId);
            _husbandry.Molted(_spiderId);
            var result = _husbandry.Molted(_spiderId);

            Assert.Equal("Molted · YI-01 → L10", result.Message);
            Assert.Equal("L10", Tile.LifeStageName);
            Assert.Equal(new[] { "L7", "L8", "L9", "L10" }, new LookupRepository(_db).GetLifeStages().Select(s => s.Name).ToArray());
            Assert.Equal("L11", _husbandry.NextInstarName(_spiders.Get(_spiderId)!));
        }

        [Fact]
        public void Instar_picker_offers_L1_to_L15_and_creates_on_demand()
        {
            var instars = new Instars(_db);
            var options = instars.PickerOptions(null);

            Assert.Equal("L1", options.First().Label);
            Assert.Equal("L15", options.Last().Label);
            instars.Ensure(null, 15);
            Assert.Equal("L16", instars.PickerOptions(null).Last().Label);
            Assert.Equal(_l7, options.Single(o => o.Label == "L7").Value);

            var l2 = instars.Resolve(options.Single(o => o.Label == "L2").Value, null);
            Assert.Equal("L2", new LookupRepository(_db).GetLifeStages().First().Name);
            Assert.Equal(l2, instars.Ensure(null, 2));
        }

        [Fact]
        public void Death_hides_spider_from_home_and_undo_brings_it_back()
        {
            var result = _husbandry.Died(_spiderId, "Found curled up");
            Assert.Empty(_spiders.GetOverview());
            Assert.Equal(SpiderStatuses.Deceased, _spiders.Get(_spiderId)!.Status);

            result.Undo();
            Assert.Single(_spiders.GetOverview());
            Assert.Equal(SpiderStatuses.Alive, _spiders.Get(_spiderId)!.Status);
        }

        [Fact]
        public void Removing_an_entry_can_be_undone()
        {
            _husbandry.Fed(_spiderId);
            var id = new EventRepository(_db).GetForSpider(_spiderId).Single().Id;

            var result = _husbandry.RemoveEntry(id);
            Assert.Null(Tile.LastFedOn);

            result.Undo();
            Assert.Equal(0, Tile.DaysSinceFed);
        }

        [Fact]
        public void Move_records_history_and_undo_restores_previous_enclosure()
        {
            var enclosures = new EnclosureRepository(_db);
            var original = _spiders.Get(_spiderId)!.EnclosureId;
            var spare = new Enclosure { Code = "SPARE-ENC" };
            enclosures.Insert(spare);

            var result = _husbandry.MoveToEnclosure(_spiderId, spare.Id);
            Assert.Equal("Moved · YI-01 → SPARE-ENC", result.Message);
            Assert.Equal("SPARE-ENC", Tile.EnclosureCode);
            Assert.True(enclosures.GetOverview().Single(e => e.Code == "YI-01-ENC").IsEmpty);
            using (var c = _db.Open())
            {
                Assert.Equal(1L, c.ExecuteScalar<long>("SELECT count(*) FROM enclosure_history WHERE spider_id = @_spiderId AND removed_at IS NULL", new { _spiderId }));
            }

            result.Undo();
            Assert.Equal(original, _spiders.Get(_spiderId)!.EnclosureId);
            Assert.Equal("YI-01", enclosures.GetOverview().Single(e => e.Code == "YI-01-ENC").Occupants);
            using (var c = _db.Open())
            {
                Assert.Equal(0L, c.ExecuteScalar<long>("SELECT count(*) FROM enclosure_history WHERE spider_id = @_spiderId", new { _spiderId }));
            }
        }

        [Fact]
        public void Editing_a_feeding_moves_its_date_keeps_the_time_and_changes_what_it_ate()
        {
            _husbandry.Fed(_spiderId);
            var events = new EventRepository(_db);
            var original = events.GetForSpider(_spiderId).Single();
            var fly = _husbandry.AddPreyType("Fruit fly");

            var edit = original.Clone();
            edit.OccurredOn = DbTime.ShiftDate(original.OccurredOn, -2);
            edit.PreyTypeId = fly;
            edit.Outcome = "partial";
            edit.Quantity = 3;
            edit.Notes = "  ate two, left one ";
            var result = _husbandry.EditEntry(edit);

            var saved = events.Get(original.Id)!;
            Assert.Equal(DbTime.ShiftDate(original.OccurredOn, -2), saved.OccurredOn);
            Assert.Equal(DbTime.FromDb(original.OccurredAt!).ToLocalTime().TimeOfDay, DbTime.FromDb(saved.OccurredAt!).ToLocalTime().TimeOfDay);
            Assert.Equal(fly, saved.PreyTypeId);
            Assert.Equal("partial", saved.Outcome);
            Assert.Equal(3, saved.Quantity);
            Assert.Equal("ate two, left one", saved.Notes);
            Assert.Equal(2, Tile.DaysSinceFed);

            result.Undo();
            Assert.Equal(original.OccurredOn, events.Get(original.Id)!.OccurredOn);
            Assert.Equal(_cricket, events.Get(original.Id)!.PreyTypeId);
            Assert.Equal(0, Tile.DaysSinceFed);
        }

        [Fact]
        public void Feed_many_logs_selected_spiders_on_the_chosen_date_with_one_undo()
        {
            var second = new Spider { Code = "YI-02", SpeciesId = _spiders.Get(_spiderId)!.SpeciesId };
            var skipped = new Spider { Code = "YI-03", SpeciesId = second.SpeciesId };
            _spiders.Insert(second);
            _spiders.Insert(skipped);
            var yesterday = DbTime.ShiftDate(DbTime.Today(), -1);

            var result = _husbandry.FeedMany(new[] { _spiderId, second.Id }, yesterday, "accepted", _cricket, 2);

            Assert.Equal("Fed · 2 spiders", result.Message);
            var tiles = _spiders.GetOverview().ToDictionary(t => t.Code);
            Assert.Equal(1, tiles["YI-01"].DaysSinceFed);
            Assert.Equal(1, tiles["YI-02"].DaysSinceFed);
            Assert.Null(tiles["YI-03"].LastFedOn);
            Assert.Equal(2, new EventRepository(_db).GetForSpider(second.Id).Single().Quantity);
            Assert.Equal(_cricket, _husbandry.MostRecentPreyType());

            result.Undo();
            Assert.All(_spiders.GetOverview(), t => Assert.Null(t.LastFedOn));
        }

        [Fact]
        public void Mist_many_logs_selected_spiders_with_action_and_date()
        {
            var other = new Spider { Code = "YI-02", SpeciesId = _spiders.Get(_spiderId)!.SpeciesId };
            _spiders.Insert(other);
            var twoDaysAgo = DbTime.ShiftDate(DbTime.Today(), -2);

            var result = _husbandry.MistMany(new[] { _spiderId }, twoDaysAgo, "water_added");

            var ev = new EventRepository(_db).GetForSpider(_spiderId).Single();
            Assert.Equal("water_added", ev.Action);
            Assert.Equal(_spiders.Get(_spiderId)!.EnclosureId, ev.EnclosureId);
            SpiderOverview Tile(string code) => _spiders.GetOverview().Single(t => t.Code == code);
            Assert.Equal(2, Tile("YI-01").DaysSinceHydrated);
            Assert.Null(Tile("YI-02").LastHydratedOn);

            result.Undo();
            Assert.Null(Tile("YI-01").LastHydratedOn);
        }

        [Fact]
        public void Entries_cannot_be_moved_into_the_future()
        {
            _husbandry.Misted(_spiderId);
            var edit = new EventRepository(_db).GetForSpider(_spiderId).Single().Clone();
            edit.OccurredOn = DbTime.ShiftDate(edit.OccurredOn, 1);

            Assert.Throws<InvalidOperationException>(() => _husbandry.EditEntry(edit));
        }

        [Fact]
        public void Adding_an_existing_prey_name_reuses_it()
        {
            Assert.Equal(_cricket, _husbandry.AddPreyType("cricket"));
        }

        [Theory]
        [InlineData(null, CareLevel.Unknown)]
        [InlineData(0, CareLevel.Good)]
        [InlineData(1, CareLevel.Good)]
        [InlineData(2, CareLevel.Due)]
        [InlineData(3, CareLevel.Due)]
        [InlineData(4, CareLevel.Attention)]
        [InlineData(6, CareLevel.Attention)]
        [InlineData(7, CareLevel.Overdue)]
        public void Feeding_colours_match_pedigree8_buckets(int? days, CareLevel expected)
        {
            Assert.Equal(expected, StatusRules.Feeding(days));
        }
    }
}
