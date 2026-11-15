using System.Collections.Generic;
using System.Linq;
using Dapper;

namespace Pedigree8.Core.Data
{
    public sealed class SpiderRepository
    {
        private const string Columns = @"
            id, code, name, species_id AS SpeciesId, morph_id AS MorphId, line_id AS LineId, sex,
            life_stage_id AS LifeStageId, status, status_changed_at AS StatusChangedAt, origin,
            breeding_status AS BreedingStatus, mother_id AS MotherId, father_id AS FatherId,
            brood_id AS BroodId, hatched_on AS HatchedOn, acquired_on AS AcquiredOn,
            enclosure_id AS EnclosureId, premolt_since AS PremoltSince, in_molt_since AS InMoltSince,
            notes, feed_every_days AS FeedEveryDays, mist_every_days AS MistEveryDays,
            created_at AS CreatedAt, updated_at AS UpdatedAt, archived_at AS ArchivedAt";

        private readonly StationDb _db;

        public SpiderRepository(StationDb db)
        {
            _db = db;
        }

        public IReadOnlyList<SpiderOverview> GetOverview(bool includeInactive = false)
        {
            const string sql = @"
                SELECT s.id, s.code, s.name, s.sex, s.status,
                       s.feed_every_days AS FeedEveryDays, s.mist_every_days AS MistEveryDays,
                       s.premolt_since AS PremoltSince, s.in_molt_since AS InMoltSince,
                       sp.scientific_name AS SpeciesName, m.name AS MorphName, ls.name AS LifeStageName,
                       s.enclosure_id AS EnclosureId, e.code AS EnclosureCode,
                       (SELECT max(f.occurred_on) FROM events f
                         WHERE f.spider_id = s.id AND f.type = 'feeding' AND f.voided_at IS NULL
                           AND (f.outcome IS NULL OR f.outcome IN ('accepted', 'partial'))) AS LastFedOn,
                       (SELECT max(h.occurred_on) FROM events h
                         WHERE h.type = 'hydration' AND h.voided_at IS NULL
                           AND (h.spider_id = s.id OR (h.spider_id IS NULL AND h.enclosure_id = s.enclosure_id))) AS LastHydratedOn,
                       (SELECT max(mo.occurred_on) FROM events mo
                         WHERE mo.spider_id = s.id AND mo.type = 'molt' AND mo.voided_at IS NULL) AS LastMoltOn
                FROM spiders s
                JOIN species sp ON sp.id = s.species_id
                LEFT JOIN morphs m ON m.id = s.morph_id
                LEFT JOIN life_stages ls ON ls.id = s.life_stage_id
                LEFT JOIN enclosures e ON e.id = s.enclosure_id
                WHERE s.archived_at IS NULL AND (@includeInactive = 1 OR s.status = 'alive')
                ORDER BY s.code";

            using var connection = _db.Open();
            return connection.Query<SpiderOverview>(sql, new { includeInactive = includeInactive ? 1 : 0 }).ToList();
        }

        public Spider? Get(string id)
        {
            using var connection = _db.Open();
            return connection.QuerySingleOrDefault<Spider>($"SELECT {Columns} FROM spiders WHERE id = @id", new { id });
        }

        public Spider? GetByCode(string code)
        {
            using var connection = _db.Open();
            return connection.QuerySingleOrDefault<Spider>(
                $"SELECT {Columns} FROM spiders WHERE code = @code COLLATE NOCASE", new { code });
        }

        public IReadOnlyList<Spider> GetOffspring(string parentId)
        {
            using var connection = _db.Open();
            return connection.Query<Spider>(
                $"SELECT {Columns} FROM spiders WHERE mother_id = @parentId OR father_id = @parentId ORDER BY code",
                new { parentId }).ToList();
        }

        public void Insert(Spider spider)
        {
            var now = DbTime.Now();
            if (string.IsNullOrEmpty(spider.Id))
            {
                spider.Id = Ids.New();
            }

            if (string.IsNullOrEmpty(spider.CreatedAt))
            {
                spider.CreatedAt = now;
            }

            spider.UpdatedAt = now;
            using var connection = _db.Open();
            connection.Execute(@"
                INSERT INTO spiders (id, code, name, species_id, morph_id, line_id, sex, life_stage_id, status,
                    status_changed_at, origin, breeding_status, mother_id, father_id, brood_id, hatched_on,
                    acquired_on, enclosure_id, premolt_since, in_molt_since, notes, feed_every_days, mist_every_days,
                    created_at, updated_at, archived_at)
                VALUES (@Id, @Code, @Name, @SpeciesId, @MorphId, @LineId, @Sex, @LifeStageId, @Status,
                    @StatusChangedAt, @Origin, @BreedingStatus, @MotherId, @FatherId, @BroodId, @HatchedOn,
                    @AcquiredOn, @EnclosureId, @PremoltSince, @InMoltSince, @Notes, @FeedEveryDays, @MistEveryDays,
                    @CreatedAt, @UpdatedAt, @ArchivedAt)",
                spider);
        }

        public void Update(Spider spider)
        {
            spider.UpdatedAt = DbTime.Now();
            using var connection = _db.Open();
            connection.Execute(@"
                UPDATE spiders SET code = @Code, name = @Name, species_id = @SpeciesId, morph_id = @MorphId,
                    line_id = @LineId, sex = @Sex, life_stage_id = @LifeStageId, status = @Status,
                    status_changed_at = @StatusChangedAt, origin = @Origin, breeding_status = @BreedingStatus,
                    mother_id = @MotherId, father_id = @FatherId, brood_id = @BroodId, hatched_on = @HatchedOn,
                    acquired_on = @AcquiredOn, enclosure_id = @EnclosureId, premolt_since = @PremoltSince,
                    in_molt_since = @InMoltSince, notes = @Notes, feed_every_days = @FeedEveryDays,
                    mist_every_days = @MistEveryDays, updated_at = @UpdatedAt, archived_at = @ArchivedAt
                WHERE id = @Id",
                spider);
        }
    }
}
