using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Dapper;
using Pedigree8.Core.Data;

namespace Pedigree8.Core
{
    // Instars are open-ended (L1, L2 … L12 …). Missing ones are created on demand, and
    // ordering is by instar number because migrated sort_order values are not reliable.
    public sealed class Instars
    {
        private static readonly Regex NumberPattern = new Regex(@"^\s*L\s*(\d+)\s*$", RegexOptions.IgnoreCase);
        private const string NewPrefix = "new:";
        private readonly StationDb _db;

        public Instars(StationDb db)
        {
            _db = db;
        }

        public static int? Number(string? codeOrName)
        {
            var m = NumberPattern.Match(codeOrName ?? "");
            return m.Success ? int.Parse(m.Groups[1].Value) : (int?)null;
        }

        public IReadOnlyList<LookupItem> ForSpecies(string? speciesId)
        {
            using var c = _db.Open();
            return c.Query<LookupItem>(@"
                SELECT id, name, species_id AS ParentId, sort_order AS SortOrder FROM life_stages
                WHERE archived_at IS NULL AND (species_id IS NULL OR @speciesId IS NULL OR species_id = @speciesId)",
                new { speciesId })
                .OrderBy(s => Number(s.Name) ?? int.MaxValue).ThenBy(s => s.SortOrder).ThenBy(s => s.Name)
                .ToList();
        }

        // Picker options: L1 to L15 (or one past the highest instar in use), then any named stages.
        // Values are existing ids, or "new:N" for an instar that will be created when chosen.
        public IReadOnlyList<(string Value, string Label)> PickerOptions(string? speciesId)
        {
            var existing = ForSpecies(speciesId);
            var numbered = existing.Where(s => Number(s.Name) != null).GroupBy(s => Number(s.Name)!.Value).ToDictionary(g => g.Key, g => g.First().Id);
            var top = Math.Max(numbered.Count == 0 ? 0 : numbered.Keys.Max() + 1, 15);
            var options = Enumerable.Range(1, top)
                .Select(n => (numbered.TryGetValue(n, out var id) ? id : NewPrefix + n, "L" + n))
                .ToList();
            options.AddRange(existing.Where(s => Number(s.Name) is null).Select(s => (s.Id, s.Name)));
            return options;
        }

        public string Resolve(string pickerValue, string? speciesId) =>
            pickerValue.StartsWith(NewPrefix, StringComparison.Ordinal)
                ? Ensure(speciesId, int.Parse(pickerValue.Substring(NewPrefix.Length)))
                : pickerValue;

        public string Ensure(string? speciesId, int number)
        {
            var found = ForSpecies(speciesId).FirstOrDefault(s => Number(s.Name) == number);
            if (found != null)
            {
                return found.Id;
            }

            var id = Ids.New();
            var now = DbTime.Now();
            using var c = _db.Open();
            c.Execute(@"INSERT INTO life_stages (id, species_id, code, name, sort_order, created_at, updated_at)
                        VALUES (@id, @speciesId, @code, @code, @number, @now, @now)",
                new { id, speciesId, code = "L" + number, number, now });
            return id;
        }

        // Next instar after the given stage; null when the stage is not numbered (e.g. Adult).
        public int? NextNumber(string? lifeStageId)
        {
            if (lifeStageId is null)
            {
                return null;
            }

            using var c = _db.Open();
            var name = c.ExecuteScalar<string?>("SELECT name FROM life_stages WHERE id = @lifeStageId", new { lifeStageId });
            return Number(name) + 1;
        }
    }
}
