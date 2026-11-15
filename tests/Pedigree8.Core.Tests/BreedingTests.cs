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
    public sealed class BreedingTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "p8-breeding-" + Guid.NewGuid().ToString("N"));
        private readonly StationDb _db;
        private readonly Husbandry _husbandry;
        private readonly Breeding _breeding;
        private readonly SpiderRepository _spiders;
        private readonly string _speciesId = Ids.New();

        public BreedingTests()
        {
            DbTime.UtcNow = () => new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc);
            _db = new StationDb(Path.Combine(_dir, "station.db"));
            _db.Initialize();
            _husbandry = new Husbandry(_db);
            _breeding = new Breeding(_db, _husbandry);
            _spiders = new SpiderRepository(_db);
            using var c = _db.Open();
            c.Execute("INSERT INTO species (id, scientific_name, created_at, updated_at) VALUES (@id, 'Phidippus regius', @now, @now)",
                new { id = _speciesId, now = DbTime.Now() });
        }

        public void Dispose()
        {
            DbTime.UtcNow = () => DateTime.UtcNow;
            SqliteConnection.ClearAllPools();
            Directory.Delete(_dir, recursive: true);
        }

        private Spider Add(string code, string? motherId = null, string? fatherId = null)
        {
            var s = new Spider { Code = code, SpeciesId = _speciesId, MotherId = motherId, FatherId = fatherId };
            _spiders.Insert(s);
            return s;
        }

        [Fact]
        public void Pairing_to_brood_to_offspring_links_the_family()
        {
            var female = Add("YI-01");
            var male = Add("LIN-01");

            _breeding.CreatePairing(female.Id, male.Id);
            var pairing = _breeding.GetPairings().Single();
            Assert.Equal("PAIR-000001", pairing.Code);
            Assert.Equal("YI-01 × LIN-01", pairing.Title);

            _breeding.AddBrood(pairing.Id);
            var brood = _breeding.GetBroods(pairing.Id).Single();
            Assert.Equal("B001", brood.Code);
            Assert.Equal("2026-11-15", brood.LaidOn);

            _breeding.SetBroodDate(brood.Id, "hatched_on", "2026-12-10");
            _breeding.AddOffspring(brood.Id, 3, null);

            var offspring = _breeding.GetBroodOffspring(brood.Id);
            Assert.Equal(new[] { "B001-01", "B001-02", "B001-03" }, offspring.Select(o => o.Code).ToArray());
            var first = _spiders.Get(offspring[0].Id)!;
            Assert.Equal(female.Id, first.MotherId);
            Assert.Equal(male.Id, first.FatherId);
            Assert.Equal("2026-12-10", first.HatchedOn);
            Assert.Equal("captive_bred", first.Origin);
            Assert.Equal(3, _breeding.GetBroods().Single().OffspringCount);
        }

        [Fact]
        public void Undo_offspring_removes_only_the_new_spiders()
        {
            var female = Add("YI-01");
            var male = Add("LIN-01");
            _breeding.CreatePairing(female.Id, male.Id);
            _breeding.AddBrood(_breeding.GetPairings().Single().Id);
            var brood = _breeding.GetBroods().Single();

            var result = _breeding.AddOffspring(brood.Id, 2, null);
            Assert.Equal(4, _spiders.GetOverview().Count);

            result.Undo();
            Assert.Equal(2, _spiders.GetOverview().Count);
        }

        [Fact]
        public void Family_tree_finds_parents_grandparents_siblings_and_offspring()
        {
            var gm = Add("GM");
            var gf = Add("GF");
            var mother = Add("MOM", gm.Id, gf.Id);
            var father = Add("DAD");
            var self = Add("ME", mother.Id, father.Id);
            Add("SIS", mother.Id, father.Id);
            Add("KID", self.Id);

            var tree = _breeding.Family(self.Id);

            Assert.Equal("MOM", tree.Mother!.Code);
            Assert.Equal("DAD", tree.Father!.Code);
            Assert.Equal("GM", tree.MothersMother!.Code);
            Assert.Equal("GF", tree.MothersFather!.Code);
            Assert.Null(tree.FathersMother);
            Assert.Equal("SIS", tree.Siblings.Single().Code);
            Assert.Equal("KID", tree.Offspring.Single().Code);
        }

        [Fact]
        public void Founders_have_no_siblings()
        {
            Add("A");
            var b = Add("B");

            Assert.Empty(_breeding.Family(b.Id).Siblings);
        }

        [Fact]
        public void Status_change_is_recorded_and_undoable()
        {
            var spider = Add("YI-02");

            var result = _husbandry.SetStatus(spider.Id, SpiderStatuses.Sold, "Sold to a friend");
            Assert.Equal(SpiderStatuses.Sold, _spiders.Get(spider.Id)!.Status);
            Assert.Empty(_spiders.GetOverview());
            var ev = new EventRepository(_db).GetForSpider(spider.Id).Single();
            Assert.Equal(EventTypes.Status, ev.Type);
            Assert.Equal("sold", ev.Action);

            result.Undo();
            Assert.Equal(SpiderStatuses.Alive, _spiders.Get(spider.Id)!.Status);
        }

        [Fact]
        public void New_spider_gets_acquired_entry_and_enclosure()
        {
            var enclosure = new Enclosure { Code = "NEW-ENC" };
            new EnclosureRepository(_db).Insert(enclosure);
            var spider = new Spider { Code = "YI-06", SpeciesId = _speciesId, EnclosureId = enclosure.Id };

            var result = _husbandry.NewSpider(spider);

            Assert.Equal("YI-06", new EnclosureRepository(_db).GetOverview().Single().Occupants);
            Assert.Equal(EventTypes.Acquired, new EventRepository(_db).GetForSpider(spider.Id).Single().Type);
            result.Undo();
            Assert.Null(_spiders.Get(spider.Id));
        }
    }
}
