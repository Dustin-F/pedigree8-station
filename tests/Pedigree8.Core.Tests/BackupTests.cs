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
    public sealed class BackupTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "p8-backup-" + Guid.NewGuid().ToString("N"));
        private readonly StationDb _db;
        private readonly Backups _backups;
        private readonly SpiderRepository _spiders;
        private readonly string _speciesId = Ids.New();

        public BackupTests()
        {
            _db = new StationDb(Path.Combine(_dir, "data", "station.db"));
            _db.Initialize();
            _backups = new Backups(_db);
            _spiders = new SpiderRepository(_db);
            using (var c = _db.Open())
            {
                c.Execute("INSERT INTO species (id, scientific_name, created_at, updated_at) VALUES (@id, 'Phidippus regius', @now, @now)",
                    new { id = _speciesId, now = DbTime.Now() });
            }

            _spiders.Insert(new Spider { Code = "YI-01", SpeciesId = _speciesId });
            Directory.CreateDirectory(Path.Combine(_dir, "data", "media", "abc"));
            File.WriteAllText(Path.Combine(_dir, "data", "media", "abc", "report.html"), "<p>report</p>");
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(_dir, recursive: true);
        }

        [Fact]
        public void Backup_is_created_and_verified_with_counts()
        {
            var info = _backups.Create(Path.Combine(_dir, "usb"), "manual");

            Assert.True(File.Exists(info.Path));
            Assert.Equal(1, info.Spiders);
            Assert.Equal(StationDb.LatestSchemaVersion, info.SchemaVersion);
            Assert.Single(_backups.List(Path.Combine(_dir, "usb")));
        }

        [Fact]
        public void Damaged_backup_is_rejected()
        {
            var info = _backups.Create(Path.Combine(_dir, "usb"), "manual");
            var bytes = File.ReadAllBytes(info.Path);
            bytes[bytes.Length / 3] ^= 0xFF;
            File.WriteAllBytes(info.Path, bytes);

            Assert.ThrowsAny<Exception>(() => _backups.Verify(info.Path));
            Assert.Empty(_backups.List(Path.Combine(_dir, "usb")));
        }

        [Fact]
        public void Restore_brings_back_data_and_media_and_keeps_a_safety_copy()
        {
            var backup = _backups.Create(Path.Combine(_dir, "usb"), "manual");
            _spiders.Insert(new Spider { Code = "YI-02", SpeciesId = _speciesId });
            File.Delete(Path.Combine(_dir, "data", "media", "abc", "report.html"));

            _backups.Restore(backup.Path);

            Assert.Equal(new[] { "YI-01" }, _spiders.GetOverview().Select(s => s.Code).ToArray());
            Assert.True(File.Exists(Path.Combine(_dir, "data", "media", "abc", "report.html")));
            var safety = _backups.List(_backups.LocalDir).Single(b => b.Kind == "before-restore");
            Assert.Equal(2, safety.Spiders);
        }

        [Fact]
        public void Auto_backup_runs_once_per_interval_and_keeps_only_the_newest()
        {
            Assert.NotNull(_backups.AutoBackupIfDue(TimeSpan.FromDays(1), keep: 2));
            Assert.Null(_backups.AutoBackupIfDue(TimeSpan.FromDays(1), keep: 2));

            Assert.NotNull(_backups.AutoBackupIfDue(TimeSpan.Zero, keep: 2));
            Assert.NotNull(_backups.AutoBackupIfDue(TimeSpan.Zero, keep: 2));

            Assert.Equal(2, Directory.GetFiles(_backups.LocalDir, "Pedigree8-auto-*.zip").Length);
        }
    }
}
