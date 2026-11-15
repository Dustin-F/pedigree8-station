using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Dapper;
using Pedigree8.Core.Data;

namespace Pedigree8.Core
{
    // Spider photos live as JPEG files in media/<id>/ next to the database (included in backups).
    public sealed class Photos
    {
        private readonly StationDb _db;
        private readonly string _dataDir;

        public Photos(StationDb db)
        {
            _db = db;
            _dataDir = Path.GetDirectoryName(db.Path)!;
        }

        public ActionResult Add(string spiderId, byte[] jpeg)
        {
            var id = Ids.New();
            var now = DbTime.Now();
            var fileName = $"photo-{System.DateTime.Now:yyyyMMdd-HHmmss}.jpg";
            var relative = $"media/{id}/{fileName}";
            var full = Path.Combine(_dataDir, "media", id, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, jpeg);

            string sha;
            using (var hasher = SHA256.Create())
            {
                sha = string.Concat(hasher.ComputeHash(jpeg).Select(b => b.ToString("x2")));
            }

            using (var c = _db.Open())
            {
                c.Execute(@"INSERT INTO media (id, spider_id, kind, file_name, relative_path, mime_type, byte_size, sha256, taken_at, created_at)
                            VALUES (@id, @spiderId, 'photo', @fileName, @relative, 'image/jpeg', @size, @sha, @now, @now)",
                    new { id, spiderId, fileName, relative, size = (long)jpeg.Length, sha, now });
            }

            return new ActionResult("Photo saved", () => Archive(id));
        }

        public IReadOnlyList<Photo> ForSpider(string spiderId)
        {
            using var c = _db.Open();
            return c.Query<Photo>(@"
                SELECT id, relative_path AS RelativePath, taken_at AS TakenAt FROM media
                WHERE spider_id = @spiderId AND kind = 'photo' AND archived_at IS NULL
                ORDER BY coalesce(taken_at, created_at) DESC", new { spiderId })
                .Select(p =>
                {
                    p.FullPath = Path.Combine(_dataDir, p.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                    return p;
                })
                .Where(p => File.Exists(p.FullPath))
                .ToList();
        }

        // Hidden from the app but the file is kept, so removing can be undone.
        public ActionResult Remove(string photoId)
        {
            Archive(photoId);
            return new ActionResult("Photo removed", () =>
            {
                using var c = _db.Open();
                c.Execute("UPDATE media SET archived_at = NULL WHERE id = @photoId", new { photoId });
            });
        }

        private void Archive(string photoId)
        {
            using var c = _db.Open();
            c.Execute("UPDATE media SET archived_at = @now WHERE id = @photoId", new { photoId, now = DbTime.Now() });
        }
    }
}
