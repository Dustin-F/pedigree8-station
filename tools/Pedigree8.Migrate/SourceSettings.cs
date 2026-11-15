using Npgsql;

namespace Pedigree8.Migrate;

// Connection details are read from the old project's .env at run time, so no
// credentials are ever stored in this tool or in the station package.
public sealed record SourceSettings(string PostgresConnectionString, Uri MinioEndpoint, string MinioAccessKey, string MinioSecretKey, string MinioBucket)
{
    public static SourceSettings FromEnvFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Pedigree8 .env file not found: {path}");
        }

        var values = File.ReadAllLines(path)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#') && l.Contains('='))
            .Select(l => l.Split('=', 2))
            .ToDictionary(p => p[0].Trim(), p => p[1].Trim().Trim('"'), StringComparer.OrdinalIgnoreCase);

        string Get(string key) => values.TryGetValue(key, out var v) && v.Length > 0
            ? v
            : throw new InvalidOperationException($"{key} is missing from {path}");

        var databaseUrl = new Uri(Get("DATABASE_URL"));
        var userInfo = databaseUrl.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = databaseUrl.Host,
            Port = databaseUrl.Port > 0 ? databaseUrl.Port : 5432,
            Database = databaseUrl.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
            ApplicationName = "Pedigree8.Migrate (read-only)",
            Options = "-c default_transaction_read_only=on",
        };

        return new SourceSettings(
            builder.ConnectionString,
            new Uri(Get("MINIO_ENDPOINT")),
            Get("MINIO_ACCESS_KEY"),
            Get("MINIO_SECRET_KEY"),
            values.TryGetValue("MINIO_BUCKET", out var bucket) && bucket.Length > 0 ? bucket : "pedigree8");
    }
}
