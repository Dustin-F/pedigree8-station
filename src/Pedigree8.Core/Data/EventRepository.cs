using System.Collections.Generic;
using System.Linq;
using Dapper;

namespace Pedigree8.Core.Data
{
    public sealed class EventRepository
    {
        private const string Columns = @"
            id, type, spider_id AS SpiderId, enclosure_id AS EnclosureId, brood_id AS BroodId,
            pairing_id AS PairingId, code, occurred_on AS OccurredOn, occurred_at AS OccurredAt,
            until_on AS UntilOn, until_at AS UntilAt, date_quality AS DateQuality, action, outcome,
            prey_type_id AS PreyTypeId, prey_size AS PreySize, quantity, from_stage_id AS FromStageId,
            to_stage_id AS ToStageId, sex_value AS SexValue, severity, notes, details, source,
            created_at AS CreatedAt, updated_at AS UpdatedAt, voided_at AS VoidedAt, void_reason AS VoidReason";

        private readonly StationDb _db;

        public EventRepository(StationDb db)
        {
            _db = db;
        }

        // Fills in id, timestamps and today's date when they are not set, so a one-tap
        // log only needs Type plus the spider/enclosure.
        public StationEvent Add(StationEvent ev)
        {
            var now = DbTime.Now();
            if (string.IsNullOrEmpty(ev.Id))
            {
                ev.Id = Ids.New();
            }

            if (string.IsNullOrEmpty(ev.OccurredOn))
            {
                ev.OccurredAt ??= now;
                ev.OccurredOn = DbTime.LocalDate(DbTime.FromDb(ev.OccurredAt));
            }

            if (string.IsNullOrEmpty(ev.CreatedAt))
            {
                ev.CreatedAt = now;
            }

            ev.UpdatedAt = now;
            using var connection = _db.Open();
            connection.Execute(@"
                INSERT INTO events (id, type, spider_id, enclosure_id, brood_id, pairing_id, code, occurred_on,
                    occurred_at, until_on, until_at, date_quality, action, outcome, prey_type_id, prey_size, quantity,
                    from_stage_id, to_stage_id, sex_value, severity, notes, details, source, created_at, updated_at,
                    voided_at, void_reason)
                VALUES (@Id, @Type, @SpiderId, @EnclosureId, @BroodId, @PairingId, @Code, @OccurredOn,
                    @OccurredAt, @UntilOn, @UntilAt, @DateQuality, @Action, @Outcome, @PreyTypeId, @PreySize, @Quantity,
                    @FromStageId, @ToStageId, @SexValue, @Severity, @Notes, @Details, @Source, @CreatedAt, @UpdatedAt,
                    @VoidedAt, @VoidReason)",
                ev);
            return ev;
        }

        // Only the fields a keeper can correct from the history screen.
        public void UpdateEditable(StationEvent ev)
        {
            ev.UpdatedAt = DbTime.Now();
            using var connection = _db.Open();
            connection.Execute(@"
                UPDATE events SET occurred_on = @OccurredOn, occurred_at = @OccurredAt, date_quality = @DateQuality,
                    action = @Action, outcome = @Outcome, prey_type_id = @PreyTypeId, prey_size = @PreySize,
                    quantity = @Quantity, severity = @Severity, notes = @Notes, updated_at = @UpdatedAt
                WHERE id = @Id", ev);
        }

        public void Void(string id, string? reason = null)
        {
            using var connection = _db.Open();
            connection.Execute(
                "UPDATE events SET voided_at = @now, void_reason = @reason, updated_at = @now WHERE id = @id AND voided_at IS NULL",
                new { id, reason, now = DbTime.Now() });
        }

        public void Restore(string id)
        {
            using var connection = _db.Open();
            connection.Execute(
                "UPDATE events SET voided_at = NULL, void_reason = NULL, updated_at = @now WHERE id = @id",
                new { id, now = DbTime.Now() });
        }

        public StationEvent? Get(string id)
        {
            using var connection = _db.Open();
            return connection.QuerySingleOrDefault<StationEvent>($"SELECT {Columns} FROM events WHERE id = @id", new { id });
        }

        public IReadOnlyList<StationEvent> GetForSpider(string spiderId, int limit = 100, string? type = null)
        {
            using var connection = _db.Open();
            return connection.Query<StationEvent>($@"
                SELECT {Columns} FROM events
                WHERE voided_at IS NULL AND (@type IS NULL OR type = @type)
                  AND (spider_id = @spiderId
                       OR (spider_id IS NULL AND enclosure_id = (SELECT enclosure_id FROM spiders WHERE id = @spiderId)))
                ORDER BY occurred_on DESC, coalesce(occurred_at, created_at) DESC
                LIMIT @limit",
                new { spiderId, limit, type }).ToList();
        }
    }
}
