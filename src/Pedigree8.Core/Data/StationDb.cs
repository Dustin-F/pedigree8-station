using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Pedigree8.Core.Data
{
    public sealed class StationDb
    {
        private const string SchemaResourcePrefix = "Pedigree8.Core.Data.Schema.";

        public StationDb(string path)
        {
            Path = System.IO.Path.GetFullPath(path);
        }

        public string Path { get; }

        public static int LatestSchemaVersion => LoadMigrations().Count;

        public SqliteConnection Open()
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = Path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private,
                ForeignKeys = true,
                Pooling = false,
            };
            var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            connection.Execute("PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA busy_timeout = 3000;");
            return connection;
        }

        public int GetSchemaVersion()
        {
            using var connection = Open();
            return connection.ExecuteScalar<int>("PRAGMA user_version;");
        }

        public void Initialize()
        {
            var directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var connection = Open();
            var current = connection.ExecuteScalar<int>("PRAGMA user_version;");
            var migrations = LoadMigrations();
            if (current > migrations.Count)
            {
                throw new InvalidOperationException(
                    $"Database schema version {current} is newer than this app supports ({migrations.Count}).");
            }

            for (var version = current + 1; version <= migrations.Count; version++)
            {
                using var transaction = connection.BeginTransaction();
                connection.Execute(migrations[version - 1], transaction: transaction);
                connection.Execute($"PRAGMA user_version = {version};", transaction: transaction);
                transaction.Commit();
            }
        }

        public IReadOnlyList<string> CheckIntegrity()
        {
            using var connection = Open();
            var problems = new List<string>();
            var integrity = connection.Query<string>("PRAGMA integrity_check;").ToList();
            if (!(integrity.Count == 1 && integrity[0] == "ok"))
            {
                problems.AddRange(integrity);
            }

            foreach (var row in connection.Query<ForeignKeyProblem>("PRAGMA foreign_key_check;"))
            {
                problems.Add($"Broken link in {row.Table} row {row.RowId} -> {row.Parent}");
            }

            return problems;
        }

        private sealed class ForeignKeyProblem
        {
            public string Table { get; set; } = "";
            public long? RowId { get; set; }
            public string Parent { get; set; } = "";
        }

        private static List<string> LoadMigrations()
        {
            var assembly = typeof(StationDb).GetTypeInfo().Assembly;
            return assembly.GetManifestResourceNames()
                .Where(n => n.StartsWith(SchemaResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.Ordinal))
                .OrderBy(n => n, StringComparer.Ordinal)
                .Select(n =>
                {
                    using var stream = assembly.GetManifestResourceStream(n)!;
                    using var reader = new StreamReader(stream);
                    return reader.ReadToEnd();
                })
                .ToList();
        }
    }
}
