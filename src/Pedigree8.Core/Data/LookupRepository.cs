using System.Collections.Generic;
using System.Linq;
using Dapper;

namespace Pedigree8.Core.Data
{
    public sealed class LookupRepository
    {
        private readonly StationDb _db;

        public LookupRepository(StationDb db)
        {
            _db = db;
        }

        public IReadOnlyList<LookupItem> GetSpecies() => Query(
            "SELECT id, coalesce(common_name, scientific_name) AS Name FROM species WHERE archived_at IS NULL ORDER BY scientific_name");

        public IReadOnlyList<LookupItem> GetMorphs() => Query(
            "SELECT id, name, species_id AS ParentId FROM morphs WHERE archived_at IS NULL ORDER BY name");

        public IReadOnlyList<LookupItem> GetLifeStages() => new Instars(_db).ForSpecies(null);

        public IReadOnlyList<LookupItem> GetPreyTypes() => Query(
            "SELECT id, name, sort_order AS SortOrder FROM prey_types WHERE archived_at IS NULL ORDER BY sort_order, name");

        public IReadOnlyList<LookupItem> GetLines() => Query(
            "SELECT id, coalesce(name, code) AS Name FROM lines WHERE archived_at IS NULL ORDER BY code");

        private IReadOnlyList<LookupItem> Query(string sql)
        {
            using var connection = _db.Open();
            return connection.Query<LookupItem>(sql).ToList();
        }
    }
}
