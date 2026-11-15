using Dapper;

namespace Pedigree8.Core.Data
{
    // Same rules as Pedigree8: id_sequences.next_value holds the LAST number handed out
    // (despite its name), and codes are LINE-01, B001, B001-01, PAIR-000001.
    public sealed class SequenceRepository
    {
        private readonly StationDb _db;

        public SequenceRepository(StationDb db)
        {
            _db = db;
        }

        public long Next(string key)
        {
            using var connection = _db.Open();
            return connection.ExecuteScalar<long>(@"
                INSERT INTO id_sequences (key, next_value) VALUES (@key, 1)
                ON CONFLICT (key) DO UPDATE SET next_value = next_value + 1
                RETURNING next_value",
                new { key });
        }

        public string NextBroodCode() => NextFree("brood", n => "B" + n.ToString("D3"), "broods");

        public string NextPairingCode() => NextFree("pairing", n => "PAIR-" + n.ToString("D6"), "pairings");

        public string NextSpiderCode(string lineId, string lineCode) =>
            NextFree("source_line:" + lineId, n => $"{lineCode}-{n:D2}", "spiders");

        public string NextOffspringCode(string broodId, string broodCode) =>
            NextFree("brood_offspring:" + broodId, n => $"{broodCode}-{n:D2}", "spiders");

        // Skips numbers already taken by hand-typed codes.
        private string NextFree(string key, System.Func<long, string> format, string table)
        {
            using var connection = _db.Open();
            while (true)
            {
                var code = format(Next(key));
                if (connection.ExecuteScalar<long>($"SELECT count(*) FROM {table} WHERE code = @code COLLATE NOCASE", new { code }) == 0)
                {
                    return code;
                }
            }
        }
    }
}
