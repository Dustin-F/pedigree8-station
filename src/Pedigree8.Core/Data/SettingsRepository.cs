using Dapper;

namespace Pedigree8.Core.Data
{
    public sealed class SettingsRepository
    {
        private readonly StationDb _db;

        public SettingsRepository(StationDb db)
        {
            _db = db;
        }

        public string? Get(string key)
        {
            using var c = _db.Open();
            return c.ExecuteScalar<string?>("SELECT value FROM settings WHERE key = @key", new { key });
        }

        public void Set(string key, string? value)
        {
            using var c = _db.Open();
            c.Execute("INSERT INTO settings (key, value) VALUES (@key, @value) ON CONFLICT (key) DO UPDATE SET value = excluded.value",
                new { key, value });
        }
    }
}
