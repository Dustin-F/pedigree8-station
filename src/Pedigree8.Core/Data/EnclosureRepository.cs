using System.Collections.Generic;
using System.Linq;
using Dapper;

namespace Pedigree8.Core.Data
{
    public sealed class EnclosureRepository
    {
        private const string Columns = @"
            id, code, qr_token AS QrToken, type_name AS TypeName, dimensions, ventilation, substrate,
            water_method AS WaterMethod, lighting, heating, contents, setup_notes AS SetupNotes,
            created_at AS CreatedAt, updated_at AS UpdatedAt, archived_at AS ArchivedAt";

        private readonly StationDb _db;

        public EnclosureRepository(StationDb db)
        {
            _db = db;
        }

        public IReadOnlyList<Enclosure> GetAll(bool includeArchived = false)
        {
            using var connection = _db.Open();
            return connection.Query<Enclosure>(
                $"SELECT {Columns} FROM enclosures WHERE @includeArchived = 1 OR archived_at IS NULL ORDER BY code",
                new { includeArchived = includeArchived ? 1 : 0 }).ToList();
        }

        public IReadOnlyList<EnclosureOverview> GetOverview()
        {
            using var connection = _db.Open();
            return connection.Query<EnclosureOverview>(@"
                SELECT e.id, e.code, e.qr_token AS QrToken, e.type_name AS TypeName,
                       (SELECT group_concat(s.code, ', ') FROM spiders s
                         WHERE s.enclosure_id = e.id AND s.status = 'alive' AND s.archived_at IS NULL) AS Occupants,
                       (SELECT s.id FROM spiders s
                         WHERE s.enclosure_id = e.id AND s.status = 'alive' AND s.archived_at IS NULL ORDER BY s.code LIMIT 1) AS OccupantId,
                       (SELECT count(*) FROM spiders s
                         WHERE s.enclosure_id = e.id AND s.status = 'alive' AND s.archived_at IS NULL) AS OccupantCount
                FROM enclosures e WHERE e.archived_at IS NULL ORDER BY e.code").ToList();
        }

        public Enclosure? Get(string id)
        {
            using var connection = _db.Open();
            return connection.QuerySingleOrDefault<Enclosure>($"SELECT {Columns} FROM enclosures WHERE id = @id", new { id });
        }

        public Enclosure? FindByQrToken(string qrToken)
        {
            using var connection = _db.Open();
            return connection.QuerySingleOrDefault<Enclosure>(
                $"SELECT {Columns} FROM enclosures WHERE qr_token = @qrToken AND archived_at IS NULL",
                new { qrToken = qrToken.ToLowerInvariant() });
        }

        public Enclosure? FindByScan(string scannedText) =>
            QrPayload.TryDecode(scannedText, out var token) ? FindByQrToken(token) : null;

        public void Insert(Enclosure enclosure)
        {
            var now = DbTime.Now();
            if (string.IsNullOrEmpty(enclosure.Id))
            {
                enclosure.Id = Ids.New();
            }

            if (string.IsNullOrEmpty(enclosure.QrToken))
            {
                enclosure.QrToken = Ids.New();
            }

            if (string.IsNullOrEmpty(enclosure.CreatedAt))
            {
                enclosure.CreatedAt = now;
            }

            enclosure.UpdatedAt = now;
            using var connection = _db.Open();
            connection.Execute(@"
                INSERT INTO enclosures (id, code, qr_token, type_name, dimensions, ventilation, substrate, water_method,
                    lighting, heating, contents, setup_notes, created_at, updated_at, archived_at)
                VALUES (@Id, @Code, @QrToken, @TypeName, @Dimensions, @Ventilation, @Substrate, @WaterMethod,
                    @Lighting, @Heating, @Contents, @SetupNotes, @CreatedAt, @UpdatedAt, @ArchivedAt)",
                enclosure);
        }

        public void Update(Enclosure enclosure)
        {
            enclosure.UpdatedAt = DbTime.Now();
            using var connection = _db.Open();
            connection.Execute(@"
                UPDATE enclosures SET code = @Code, type_name = @TypeName, dimensions = @Dimensions,
                    ventilation = @Ventilation, substrate = @Substrate, water_method = @WaterMethod,
                    lighting = @Lighting, heating = @Heating, contents = @Contents, setup_notes = @SetupNotes,
                    updated_at = @UpdatedAt, archived_at = @ArchivedAt
                WHERE id = @Id",
                enclosure);
        }
    }
}
