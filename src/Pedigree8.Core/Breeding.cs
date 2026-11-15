using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using Pedigree8.Core.Data;

namespace Pedigree8.Core
{
    public sealed class PairingOverview
    {
        public string Id { get; set; } = "";
        public string Code { get; set; } = "";
        public string FemaleId { get; set; } = "";
        public string FemaleCode { get; set; } = "";
        public string MaleId { get; set; } = "";
        public string MaleCode { get; set; } = "";
        public string PairedOn { get; set; } = "";
        public string Result { get; set; } = "unknown";
        public int BroodCount { get; set; }
        public string ResultText => Result.Replace('_', ' ');
        public string Title => $"{FemaleCode} × {MaleCode}";
    }

    public sealed class BroodOverview
    {
        public string Id { get; set; } = "";
        public string Code { get; set; } = "";
        public string? PairingId { get; set; }
        public string? SpeciesId { get; set; }
        public string? MotherCode { get; set; }
        public string? FatherCode { get; set; }
        public string? LaidOn { get; set; }
        public string? HatchedOn { get; set; }
        public string? EmergedOn { get; set; }
        public int? EggCount { get; set; }
        public int OffspringCount { get; set; }
        public string Parents => $"{MotherCode ?? "?"} × {FatherCode ?? "?"}";
    }

    public sealed class FamilyMember
    {
        public string Id { get; set; } = "";
        public string Code { get; set; } = "";
        public string Status { get; set; } = "";
        public string? MotherId { get; set; }
        public string? FatherId { get; set; }
    }

    public sealed class FamilyTree
    {
        public FamilyMember Self { get; set; } = null!;
        public FamilyMember? Mother { get; set; }
        public FamilyMember? Father { get; set; }
        public FamilyMember? MothersMother { get; set; }
        public FamilyMember? MothersFather { get; set; }
        public FamilyMember? FathersMother { get; set; }
        public FamilyMember? FathersFather { get; set; }
        public IReadOnlyList<FamilyMember> Offspring { get; set; } = Array.Empty<FamilyMember>();
        public IReadOnlyList<FamilyMember> Siblings { get; set; } = Array.Empty<FamilyMember>();
    }

    public sealed class Breeding
    {
        private readonly StationDb _db;
        private readonly SequenceRepository _sequences;
        private readonly Husbandry _husbandry;

        public Breeding(StationDb db, Husbandry husbandry)
        {
            _db = db;
            _husbandry = husbandry;
            _sequences = new SequenceRepository(db);
        }

        public IReadOnlyList<PairingOverview> GetPairings()
        {
            using var c = _db.Open();
            return c.Query<PairingOverview>(@"
                SELECT p.id, p.code, p.female_id AS FemaleId, f.code AS FemaleCode, p.male_id AS MaleId, m.code AS MaleCode,
                       p.paired_on AS PairedOn, p.result,
                       (SELECT count(*) FROM broods b WHERE b.pairing_id = p.id AND b.archived_at IS NULL) AS BroodCount
                FROM pairings p JOIN spiders f ON f.id = p.female_id JOIN spiders m ON m.id = p.male_id
                WHERE p.voided_at IS NULL
                ORDER BY p.paired_on DESC, p.created_at DESC").ToList();
        }

        public IReadOnlyList<BroodOverview> GetBroods(string? pairingId = null)
        {
            using var c = _db.Open();
            return c.Query<BroodOverview>(@"
                SELECT b.id, b.code, b.pairing_id AS PairingId, coalesce(b.species_id, mo.species_id) AS SpeciesId, mo.code AS MotherCode, fa.code AS FatherCode,
                       b.laid_on AS LaidOn, b.hatched_on AS HatchedOn, b.emerged_on AS EmergedOn, b.egg_count AS EggCount,
                       (SELECT count(*) FROM spiders s WHERE s.brood_id = b.id) AS OffspringCount
                FROM broods b LEFT JOIN spiders mo ON mo.id = b.mother_id LEFT JOIN spiders fa ON fa.id = b.father_id
                WHERE b.archived_at IS NULL AND (@pairingId IS NULL OR b.pairing_id = @pairingId)
                ORDER BY coalesce(b.laid_on, b.created_at) DESC", new { pairingId }).ToList();
        }

        public BroodOverview GetBrood(string broodId) => GetBroods().First(b => b.Id == broodId);

        public ActionResult CreatePairing(string femaleId, string maleId)
        {
            if (femaleId == maleId)
            {
                throw new InvalidOperationException("Choose two different spiders.");
            }

            var id = Ids.New();
            var code = _sequences.NextPairingCode();
            var now = DbTime.Now();
            using (var c = _db.Open())
            {
                c.Execute(@"INSERT INTO pairings (id, code, female_id, male_id, paired_on, paired_at, created_at, updated_at)
                            VALUES (@id, @code, @femaleId, @maleId, @today, @now, @now, @now)",
                    new { id, code, femaleId, maleId, today = DbTime.Today(), now });
            }

            return new ActionResult($"Pairing {code} saved", () => Void("pairings", id));
        }

        public ActionResult SetPairingResult(string pairingId, string result)
        {
            using var c = _db.Open();
            var before = c.ExecuteScalar<string>("SELECT result FROM pairings WHERE id = @pairingId", new { pairingId });
            c.Execute("UPDATE pairings SET result = @result, updated_at = @now WHERE id = @pairingId",
                new { pairingId, result, now = DbTime.Now() });
            return new ActionResult($"Result: {result.Replace('_', ' ')}", () =>
            {
                using var u = _db.Open();
                u.Execute("UPDATE pairings SET result = @before WHERE id = @pairingId", new { pairingId, before });
            });
        }

        public ActionResult AddBrood(string pairingId)
        {
            using var c = _db.Open();
            var pairing = c.QuerySingle<(string FemaleId, string MaleId)>(
                "SELECT female_id, male_id FROM pairings WHERE id = @pairingId", new { pairingId });
            var mother = new SpiderRepository(_db).Get(pairing.FemaleId)!;
            var id = Ids.New();
            var code = _sequences.NextBroodCode();
            var now = DbTime.Now();
            c.Execute(@"INSERT INTO broods (id, code, pairing_id, mother_id, father_id, species_id, morph_id, laid_on, created_at, updated_at)
                        VALUES (@id, @code, @pairingId, @motherId, @fatherId, @speciesId, @morphId, @today, @now, @now)",
                new
                {
                    id, code, pairingId, motherId = pairing.FemaleId, fatherId = pairing.MaleId,
                    speciesId = mother.SpeciesId, morphId = mother.MorphId, today = DbTime.Today(), now,
                });
            return new ActionResult($"Egg sac {code} recorded", () =>
            {
                using var u = _db.Open();
                u.Execute("UPDATE broods SET archived_at = @now WHERE id = @id", new { id, now = DbTime.Now() });
            });
        }

        // field: laid_on, hatched_on or emerged_on
        public ActionResult SetBroodDate(string broodId, string field, string? date)
        {
            if (field != "laid_on" && field != "hatched_on" && field != "emerged_on")
            {
                throw new ArgumentException("Unknown brood date.", nameof(field));
            }

            using var c = _db.Open();
            var before = c.ExecuteScalar<string?>($"SELECT {field} FROM broods WHERE id = @broodId", new { broodId });
            c.Execute($"UPDATE broods SET {field} = @date, updated_at = @now WHERE id = @broodId", new { broodId, date, now = DbTime.Now() });
            return new ActionResult("Brood date saved", () =>
            {
                using var u = _db.Open();
                u.Execute($"UPDATE broods SET {field} = @before WHERE id = @broodId", new { broodId, before });
            });
        }

        public ActionResult SetEggCount(string broodId, int? count)
        {
            using var c = _db.Open();
            var before = c.ExecuteScalar<int?>("SELECT egg_count FROM broods WHERE id = @broodId", new { broodId });
            c.Execute("UPDATE broods SET egg_count = @count, updated_at = @now WHERE id = @broodId", new { broodId, count, now = DbTime.Now() });
            return new ActionResult("Egg count saved", () =>
            {
                using var u = _db.Open();
                u.Execute("UPDATE broods SET egg_count = @before WHERE id = @broodId", new { broodId, before });
            });
        }

        public ActionResult AddOffspring(string broodId, int count, string? lifeStageId)
        {
            if (count < 1 || count > 200)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Add between 1 and 200 spiderlings at a time.");
            }

            using var c = _db.Open();
            var brood = c.QuerySingle<(string Code, string? MotherId, string? FatherId, string? SpeciesId, string? MorphId, string? Hatched)>(
                "SELECT code, mother_id, father_id, species_id, morph_id, coalesce(hatched_on, emerged_on) FROM broods WHERE id = @broodId",
                new { broodId });
            var speciesId = brood.SpeciesId
                ?? c.ExecuteScalar<string?>("SELECT species_id FROM spiders WHERE id = @id", new { id = brood.MotherId })
                ?? throw new InvalidOperationException("This brood has no species or mother recorded.");
            var spiders = new SpiderRepository(_db);
            var created = new List<string>();
            for (var i = 0; i < count; i++)
            {
                var spider = new Spider
                {
                    Code = _sequences.NextOffspringCode(broodId, brood.Code),
                    SpeciesId = speciesId,
                    MorphId = brood.MorphId,
                    LifeStageId = lifeStageId,
                    Origin = "captive_bred",
                    BreedingStatus = "juvenile",
                    MotherId = brood.MotherId,
                    FatherId = brood.FatherId,
                    BroodId = broodId,
                    HatchedOn = brood.Hatched ?? DbTime.Today(),
                };
                spiders.Insert(spider);
                created.Add(spider.Id);
            }

            return new ActionResult($"Added {count} spiderling{(count == 1 ? "" : "s")} to {brood.Code}",
                () => _husbandry.DeleteNewSpiders(created));
        }

        public IReadOnlyList<FamilyMember> GetBroodOffspring(string broodId)
        {
            using var c = _db.Open();
            return c.Query<FamilyMember>(
                "SELECT id, code, status, mother_id AS MotherId, father_id AS FatherId FROM spiders WHERE brood_id = @broodId ORDER BY code",
                new { broodId }).ToList();
        }

        public FamilyTree Family(string spiderId)
        {
            using var c = _db.Open();
            FamilyMember? Get(string? id) => id is null
                ? null
                : c.QuerySingleOrDefault<FamilyMember>(
                    "SELECT id, code, status, mother_id AS MotherId, father_id AS FatherId FROM spiders WHERE id = @id", new { id });

            var self = Get(spiderId)!;
            var mother = Get(self.MotherId);
            var father = Get(self.FatherId);
            return new FamilyTree
            {
                Self = self,
                Mother = mother,
                Father = father,
                MothersMother = Get(mother?.MotherId),
                MothersFather = Get(mother?.FatherId),
                FathersMother = Get(father?.MotherId),
                FathersFather = Get(father?.FatherId),
                Offspring = c.Query<FamilyMember>(@"
                    SELECT id, code, status, mother_id AS MotherId, father_id AS FatherId FROM spiders
                    WHERE (mother_id = @spiderId OR father_id = @spiderId) AND archived_at IS NULL ORDER BY code",
                    new { spiderId }).ToList(),
                Siblings = self.MotherId is null && self.FatherId is null
                    ? Array.Empty<FamilyMember>()
                    : c.Query<FamilyMember>(@"
                        SELECT id, code, status, mother_id AS MotherId, father_id AS FatherId FROM spiders
                        WHERE id <> @spiderId AND archived_at IS NULL
                          AND coalesce(mother_id, '') = coalesce(@m, '') AND coalesce(father_id, '') = coalesce(@f, '')
                        ORDER BY code", new { spiderId, m = self.MotherId, f = self.FatherId }).ToList(),
            };
        }

        private void Void(string table, string id)
        {
            using var c = _db.Open();
            c.Execute($"UPDATE {table} SET voided_at = @now, void_reason = 'Undone on station' WHERE id = @id", new { id, now = DbTime.Now() });
        }
    }
}
