using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using Pedigree8.Core.Data;

namespace Pedigree8.Core
{
    public sealed class ActionResult
    {
        public ActionResult(string message, Action undo)
        {
            Message = message;
            Undo = undo;
        }

        public string Message { get; }

        public Action Undo { get; }
    }

    // One-tap station actions. Each returns a message for the undo bar and the undo itself.
    public sealed class Husbandry
    {
        private readonly StationDb _db;
        private readonly SpiderRepository _spiders;
        private readonly EventRepository _events;
        private readonly Instars _instars;

        public Husbandry(StationDb db)
        {
            _db = db;
            _spiders = new SpiderRepository(db);
            _events = new EventRepository(db);
            _instars = new Instars(db);
        }

        public ActionResult Fed(string spiderId, string outcome = "accepted")
        {
            var spider = Require(spiderId);
            var ev = _events.Add(new StationEvent
            {
                Type = EventTypes.Feeding,
                SpiderId = spider.Id,
                Outcome = outcome,
                PreyTypeId = DefaultPreyType(spider.Id),
                Quantity = 1,
            });
            var verb = outcome == "refused" ? "Refused food" : "Fed";
            return new ActionResult($"{verb} · {spider.Code}", () => _events.Void(ev.Id, "Undone on station"));
        }

        public ActionResult Misted(string spiderId)
        {
            var spider = Require(spiderId);
            var ev = _events.Add(new StationEvent
            {
                Type = EventTypes.Hydration,
                SpiderId = spider.Id,
                EnclosureId = spider.EnclosureId,
                Action = "misted",
            });
            return new ActionResult($"Misted · {spider.Code}", () => _events.Void(ev.Id, "Undone on station"));
        }

        // Logs the same feeding for several spiders at once, on the chosen date (current time of day).
        public ActionResult FeedMany(IReadOnlyCollection<string> spiderIds, string date, string outcome, string? preyTypeId, double quantity)
        {
            if (spiderIds.Count == 0)
            {
                throw new InvalidOperationException("Select at least one spider.");
            }

            if (string.CompareOrdinal(date, DbTime.Today()) > 0)
            {
                throw new InvalidOperationException("The date can't be in the future.");
            }

            var now = DbTime.Now();
            var at = date == DbTime.Today() ? now : DbTime.MoveToDate(now, date);
            var ids = spiderIds.Select(id => _events.Add(new StationEvent
            {
                Type = EventTypes.Feeding,
                SpiderId = id,
                OccurredOn = date,
                OccurredAt = at,
                Outcome = outcome,
                PreyTypeId = preyTypeId ?? DefaultPreyType(id),
                Quantity = quantity,
            }).Id).ToList();

            var verb = outcome == "refused" ? "Refused food" : "Fed";
            return new ActionResult($"{verb} · {ids.Count} spider{(ids.Count == 1 ? "" : "s")}", () =>
            {
                foreach (var id in ids)
                {
                    _events.Void(id, "Undone on station");
                }
            });
        }

        // Logs the same misting/water entry for several spiders' enclosures on the chosen date.
        public ActionResult MistMany(IReadOnlyCollection<string> spiderIds, string date, string action)
        {
            if (spiderIds.Count == 0)
            {
                throw new InvalidOperationException("Select at least one spider.");
            }

            if (string.CompareOrdinal(date, DbTime.Today()) > 0)
            {
                throw new InvalidOperationException("The date can't be in the future.");
            }

            var now = DbTime.Now();
            var at = date == DbTime.Today() ? now : DbTime.MoveToDate(now, date);
            var ids = spiderIds.Select(id => _events.Add(new StationEvent
            {
                Type = EventTypes.Hydration,
                SpiderId = id,
                EnclosureId = Require(id).EnclosureId,
                OccurredOn = date,
                OccurredAt = at,
                Action = action,
            }).Id).ToList();

            return new ActionResult($"Misted · {ids.Count} spider{(ids.Count == 1 ? "" : "s")}", () =>
            {
                foreach (var id in ids)
                {
                    _events.Void(id, "Undone on station");
                }
            });
        }

        // The food used most recently in the whole collection, to preselect for "Feed all".
        public string? MostRecentPreyType()
        {
            using var c = _db.Open();
            return c.ExecuteScalar<string?>(@"
                SELECT prey_type_id FROM events
                WHERE type = 'feeding' AND prey_type_id IS NOT NULL AND voided_at IS NULL
                ORDER BY occurred_on DESC, coalesce(occurred_at, created_at) DESC LIMIT 1");
        }

        public ActionResult Molted(string spiderId)
        {
            var spider = Require(spiderId);
            var before = Copy(spider);
            var nextNumber = _instars.NextNumber(spider.LifeStageId);
            var nextStage = nextNumber is null ? null : new LookupItem { Id = _instars.Ensure(spider.SpeciesId, nextNumber.Value), Name = "L" + nextNumber };
            var ev = _events.Add(new StationEvent
            {
                Type = EventTypes.Molt,
                SpiderId = spider.Id,
                FromStageId = spider.LifeStageId,
                ToStageId = nextStage?.Id ?? spider.LifeStageId,
                Outcome = "successful",
            });

            var closedPremolt = ClosePremoltEvents(spider.Id);
            spider.LifeStageId = nextStage?.Id ?? spider.LifeStageId;
            spider.PremoltSince = null;
            spider.InMoltSince = null;
            _spiders.Update(spider);

            var message = nextStage is null ? $"Molted · {spider.Code}" : $"Molted · {spider.Code} → {nextStage.Name}";
            return new ActionResult(message, () =>
            {
                _events.Void(ev.Id, "Undone on station");
                ReopenPremoltEvents(closedPremolt);
                _spiders.Update(before);
            });
        }

        public ActionResult SetPremolt(string spiderId, bool on)
        {
            var spider = Require(spiderId);
            var before = Copy(spider);
            if (on)
            {
                var ev = _events.Add(new StationEvent { Type = EventTypes.Premolt, SpiderId = spider.Id, Action = "suspected_premolt" });
                spider.PremoltSince = ev.OccurredAt;
                _spiders.Update(spider);
                return new ActionResult($"Premolt watch on · {spider.Code}", () =>
                {
                    _events.Void(ev.Id, "Undone on station");
                    _spiders.Update(before);
                });
            }

            var closed = ClosePremoltEvents(spider.Id);
            spider.PremoltSince = null;
            spider.InMoltSince = null;
            _spiders.Update(spider);
            return new ActionResult($"Premolt watch off · {spider.Code}", () =>
            {
                ReopenPremoltEvents(closed);
                _spiders.Update(before);
            });
        }

        public ActionResult AddNote(string spiderId, string text)
        {
            var spider = Require(spiderId);
            var ev = _events.Add(new StationEvent { Type = EventTypes.Note, SpiderId = spider.Id, Notes = text.Trim() });
            return new ActionResult($"Note saved · {spider.Code}", () => _events.Void(ev.Id, "Undone on station"));
        }

        public ActionResult AddHealth(string spiderId, string severity, string? text)
        {
            var spider = Require(spiderId);
            var ev = _events.Add(new StationEvent
            {
                Type = EventTypes.Health,
                SpiderId = spider.Id,
                Severity = severity,
                Outcome = "ongoing",
                Notes = string.IsNullOrWhiteSpace(text) ? null : text!.Trim(),
            });
            return new ActionResult($"Health note saved · {spider.Code}", () => _events.Void(ev.Id, "Undone on station"));
        }

        public ActionResult Died(string spiderId, string? text)
        {
            var spider = Require(spiderId);
            var before = Copy(spider);
            var ev = _events.Add(new StationEvent
            {
                Type = EventTypes.Death,
                SpiderId = spider.Id,
                Notes = string.IsNullOrWhiteSpace(text) ? null : text!.Trim(),
            });
            spider.Status = SpiderStatuses.Deceased;
            spider.StatusChangedAt = ev.OccurredAt;
            _spiders.Update(spider);
            return new ActionResult($"Recorded death · {spider.Code}", () =>
            {
                _events.Void(ev.Id, "Undone on station");
                _spiders.Update(before);
            });
        }

        public ActionResult SetStatus(string spiderId, string status, string? note)
        {
            if (status == SpiderStatuses.Deceased)
            {
                return Died(spiderId, note);
            }

            var spider = Require(spiderId);
            var before = spider.Clone();
            var ev = _events.Add(new StationEvent
            {
                Type = EventTypes.Status,
                SpiderId = spider.Id,
                Action = status,
                Notes = string.IsNullOrWhiteSpace(note) ? null : note!.Trim(),
                Details = $"{{\"from\":\"{spider.Status}\"}}",
            });
            spider.Status = status;
            spider.StatusChangedAt = ev.OccurredAt;
            _spiders.Update(spider);
            return new ActionResult($"{spider.Code} is now {status}", () =>
            {
                _events.Void(ev.Id, "Undone on station");
                _spiders.Update(before);
            });
        }

        public ActionResult SetSex(string spiderId, string sex)
        {
            var spider = Require(spiderId);
            var before = spider.Clone();
            var ev = _events.Add(new StationEvent { Type = EventTypes.Sex, SpiderId = spider.Id, SexValue = sex });
            spider.Sex = sex;
            _spiders.Update(spider);
            return new ActionResult($"{spider.Code} sex: {sex.Replace('_', ' ')}", () =>
            {
                _events.Void(ev.Id, "Undone on station");
                _spiders.Update(before);
            });
        }

        public ActionResult SetLifeStage(string spiderId, string lifeStageId)
        {
            var spider = Require(spiderId);
            var before = spider.Clone();
            spider.LifeStageId = lifeStageId;
            _spiders.Update(spider);
            return new ActionResult($"{spider.Code} stage changed", () => _spiders.Update(before));
        }

        public ActionResult UpdateNameAndNotes(string spiderId, string? name, string? notes)
        {
            var spider = Require(spiderId);
            var before = spider.Clone();
            spider.Name = string.IsNullOrWhiteSpace(name) ? null : name!.Trim();
            spider.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes!.Trim();
            _spiders.Update(spider);
            return new ActionResult($"{spider.Code} details saved", () => _spiders.Update(before));
        }

        public ActionResult SetSchedule(string spiderId, int? feedEveryDays, int? mistEveryDays)
        {
            var spider = Require(spiderId);
            var before = spider.Clone();
            spider.FeedEveryDays = feedEveryDays;
            spider.MistEveryDays = mistEveryDays;
            _spiders.Update(spider);
            return new ActionResult($"{spider.Code} care schedule saved", () => _spiders.Update(before));
        }

        public ActionResult AddMeasurement(string spiderId, double bodyLengthMm)
        {
            var spider = Require(spiderId);
            var length = Math.Round(bodyLengthMm, 1).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            var ev = _events.Add(new StationEvent
            {
                Type = EventTypes.Observation,
                SpiderId = spider.Id,
                Action = "measurement",
                Notes = $"Body length: {length} mm",
                Details = $"{{\"body_length_mm\":{length},\"life_stage_id\":{(spider.LifeStageId is null ? "null" : $"\"{spider.LifeStageId}\"")}}}",
            });
            return new ActionResult($"{spider.Code} measured: {length} mm", () => _events.Void(ev.Id, "Undone on station"));
        }

        // Body-length history, newest first. Includes measurements migrated from Pedigree8.
        public IReadOnlyList<Measurement> GetMeasurements(string spiderId)
        {
            using var c = _db.Open();
            return c.Query<Measurement>(@"
                SELECT e.occurred_on AS OccurredOn, json_extract(e.details, '$.body_length_mm') AS BodyLengthMm, ls.name AS Instar
                FROM events e LEFT JOIN life_stages ls ON ls.id = json_extract(e.details, '$.life_stage_id')
                WHERE e.spider_id = @spiderId AND e.type = 'observation' AND e.action = 'measurement'
                  AND e.voided_at IS NULL AND json_extract(e.details, '$.body_length_mm') IS NOT NULL
                ORDER BY e.occurred_on DESC, e.created_at DESC", new { spiderId }).ToList();
        }

        public ActionResult NewSpider(Spider spider)
        {
            var now = DbTime.Now();
            spider.AcquiredOn ??= DbTime.Today();
            _spiders.Insert(spider);
            var ev = _events.Add(new StationEvent { Type = EventTypes.Acquired, SpiderId = spider.Id, OccurredOn = spider.AcquiredOn! });
            string? historyId = null;
            if (spider.EnclosureId != null)
            {
                historyId = Ids.New();
                using var c = _db.Open();
                c.Execute(@"INSERT INTO enclosure_history (id, enclosure_id, spider_id, placed_at, created_at)
                            VALUES (@historyId, @enclosureId, @spiderId, @now, @now)",
                    new { historyId, enclosureId = spider.EnclosureId, spiderId = spider.Id, now });
            }

            return new ActionResult($"Added {spider.Code}", () => DeleteNewSpiders(new[] { spider.Id }));
        }

        // Only used to undo a spider created moments ago; it has no other records yet.
        internal void DeleteNewSpiders(IEnumerable<string> spiderIds)
        {
            using var c = _db.Open();
            using var tx = c.BeginTransaction();
            var ids = spiderIds.ToList();
            c.Execute("DELETE FROM events WHERE spider_id IN @ids", new { ids }, tx);
            c.Execute("DELETE FROM enclosure_history WHERE spider_id IN @ids", new { ids }, tx);
            c.Execute("DELETE FROM spiders WHERE id IN @ids", new { ids }, tx);
            tx.Commit();
        }

        public ActionResult MoveToEnclosure(string spiderId, string enclosureId)
        {
            var spider = Require(spiderId);
            var before = spider.Clone();
            var enclosure = new EnclosureRepository(_db).Get(enclosureId) ?? throw new InvalidOperationException("Enclosure not found.");
            var now = DbTime.Now();
            var historyId = Ids.New();

            using (var connection = _db.Open())
            using (var tx = connection.BeginTransaction())
            {
                var closed = connection.Query<string>(
                    "SELECT id FROM enclosure_history WHERE spider_id = @spiderId AND removed_at IS NULL", new { spiderId }, tx).ToList();
                connection.Execute("UPDATE enclosure_history SET removed_at = @now WHERE id IN @closed", new { now, closed }, tx);
                connection.Execute(@"INSERT INTO enclosure_history (id, enclosure_id, spider_id, placed_at, created_at)
                                     VALUES (@historyId, @enclosureId, @spiderId, @now, @now)",
                    new { historyId, enclosureId, spiderId, now }, tx);
                connection.Execute("UPDATE spiders SET enclosure_id = @enclosureId, updated_at = @now WHERE id = @spiderId",
                    new { enclosureId, spiderId, now }, tx);
                tx.Commit();

                return new ActionResult($"Moved · {spider.Code} → {enclosure.Code}", () =>
                {
                    using var c = _db.Open();
                    using var undoTx = c.BeginTransaction();
                    c.Execute("DELETE FROM enclosure_history WHERE id = @historyId", new { historyId }, undoTx);
                    c.Execute("UPDATE enclosure_history SET removed_at = NULL WHERE id IN @closed", new { closed }, undoTx);
                    undoTx.Commit();
                    _spiders.Update(before);
                });
            }
        }

        // Saves corrections to a history entry; moving the date keeps the original time of day.
        public ActionResult EditEntry(StationEvent edited)
        {
            var original = _events.Get(edited.Id) ?? throw new InvalidOperationException("Entry not found.");
            if (string.CompareOrdinal(edited.OccurredOn, DbTime.Today()) > 0)
            {
                throw new InvalidOperationException("The date can't be in the future.");
            }

            if (edited.OccurredOn != original.OccurredOn && original.OccurredAt != null)
            {
                edited.OccurredAt = DbTime.MoveToDate(original.OccurredAt, edited.OccurredOn);
            }

            edited.Notes = string.IsNullOrWhiteSpace(edited.Notes) ? null : edited.Notes!.Trim();
            _events.UpdateEditable(edited);
            return new ActionResult("Entry updated", () => _events.UpdateEditable(original));
        }

        public string AddPreyType(string name)
        {
            var clean = name.Trim();
            using var c = _db.Open();
            var existing = c.ExecuteScalar<string?>(
                "SELECT id FROM prey_types WHERE name = @clean COLLATE NOCASE AND archived_at IS NULL", new { clean });
            if (existing != null)
            {
                return existing;
            }

            var id = Ids.New();
            var now = DbTime.Now();
            c.Execute("INSERT INTO prey_types (id, name, sort_order, created_at, updated_at) VALUES (@id, @clean, 0, @now, @now)",
                new { id, clean, now });
            return id;
        }

        public ActionResult RemoveEntry(string eventId)
        {
            var ev = _events.Get(eventId) ?? throw new InvalidOperationException("Entry not found.");
            _events.Void(eventId, "Removed on station");
            return new ActionResult("Entry removed", () => _events.Restore(eventId));
        }

        // Name of the instar a molt would move to (it is created when the molt is logged).
        public string? NextInstarName(Spider spider) =>
            _instars.NextNumber(spider.LifeStageId) is { } n ? "L" + n : null;

        private string? DefaultPreyType(string spiderId)
        {
            using var connection = _db.Open();
            return connection.ExecuteScalar<string?>(@"
                SELECT coalesce(
                    (SELECT prey_type_id FROM events
                      WHERE spider_id = @spiderId AND type = 'feeding' AND prey_type_id IS NOT NULL AND voided_at IS NULL
                      ORDER BY occurred_on DESC, coalesce(occurred_at, created_at) DESC LIMIT 1),
                    (SELECT id FROM prey_types WHERE archived_at IS NULL ORDER BY sort_order, name LIMIT 1))",
                new { spiderId });
        }

        private List<string> ClosePremoltEvents(string spiderId)
        {
            var now = DbTime.Now();
            using var connection = _db.Open();
            var open = connection.Query<string>(
                "SELECT id FROM events WHERE spider_id = @spiderId AND type = 'premolt' AND until_at IS NULL AND voided_at IS NULL",
                new { spiderId }).ToList();
            connection.Execute(
                "UPDATE events SET until_at = @now, until_on = @today, updated_at = @now WHERE id IN @open",
                new { now, today = DbTime.Today(), open });
            return open;
        }

        private void ReopenPremoltEvents(List<string> ids)
        {
            using var connection = _db.Open();
            connection.Execute(
                "UPDATE events SET until_at = NULL, until_on = NULL, updated_at = @now WHERE id IN @ids",
                new { now = DbTime.Now(), ids });
        }

        private Spider Require(string spiderId) =>
            _spiders.Get(spiderId) ?? throw new InvalidOperationException("Spider not found.");

        private static Spider Copy(Spider s) => s.Clone();
    }
}
