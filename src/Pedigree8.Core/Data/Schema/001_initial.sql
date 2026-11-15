-- Pedigree8 Station schema v1
-- Conventions:
--   ids          TEXT, lowercase UUIDs (original Pedigree8 ids are kept as-is)
--   *_at         TEXT, UTC ISO-8601 'yyyy-MM-ddTHH:mm:ss.fffZ'
--   *_on         TEXT, local calendar date 'yyyy-MM-dd'
--   booleans     INTEGER 0/1
--   soft delete  archived_at (things) / voided_at (events) — the app only hard-deletes a row when
--                undoing the action that created it a few seconds earlier

CREATE TABLE settings (
  key   TEXT PRIMARY KEY,
  value TEXT
) STRICT;

CREATE TABLE species (
  id              TEXT PRIMARY KEY,
  scientific_name TEXT NOT NULL,
  common_name     TEXT,
  notes           TEXT,
  created_at      TEXT NOT NULL,
  updated_at      TEXT NOT NULL,
  archived_at     TEXT
) STRICT;

CREATE TABLE morphs (
  id          TEXT PRIMARY KEY,
  species_id  TEXT REFERENCES species(id),
  name        TEXT NOT NULL,
  description TEXT,
  created_at  TEXT NOT NULL,
  updated_at  TEXT NOT NULL,
  archived_at TEXT
) STRICT;

CREATE TABLE life_stages (
  id          TEXT PRIMARY KEY,
  species_id  TEXT REFERENCES species(id),
  code        TEXT NOT NULL,
  name        TEXT NOT NULL,
  sort_order  INTEGER NOT NULL DEFAULT 0,
  is_adult    INTEGER NOT NULL DEFAULT 0 CHECK (is_adult IN (0, 1)),
  created_at  TEXT NOT NULL,
  updated_at  TEXT NOT NULL,
  archived_at TEXT
) STRICT;

CREATE TABLE prey_types (
  id          TEXT PRIMARY KEY,
  name        TEXT NOT NULL,
  sort_order  INTEGER NOT NULL DEFAULT 0,
  created_at  TEXT NOT NULL,
  updated_at  TEXT NOT NULL,
  archived_at TEXT
) STRICT;

-- Breeders / shops / people spiders came from (Pedigree8 "parties").
CREATE TABLE suppliers (
  id            TEXT PRIMARY KEY,
  name          TEXT NOT NULL,
  kind          TEXT,
  location      TEXT,
  contact_notes TEXT,
  created_at    TEXT NOT NULL,
  updated_at    TEXT NOT NULL,
  archived_at   TEXT
) STRICT;

-- Bloodlines; their code prefixes spider codes (LIN-01, YI-03).
CREATE TABLE lines (
  id          TEXT PRIMARY KEY,
  code        TEXT NOT NULL UNIQUE,
  name        TEXT,
  supplier_id TEXT REFERENCES suppliers(id),
  notes       TEXT,
  created_at  TEXT NOT NULL,
  updated_at  TEXT NOT NULL,
  archived_at TEXT
) STRICT;

CREATE TABLE enclosures (
  id           TEXT PRIMARY KEY,
  code         TEXT NOT NULL UNIQUE,
  qr_token     TEXT NOT NULL UNIQUE,
  type_name    TEXT,
  dimensions   TEXT,
  ventilation  TEXT,
  substrate    TEXT,
  water_method TEXT,
  lighting     TEXT,
  heating      TEXT,
  contents     TEXT,
  setup_notes  TEXT,
  created_at   TEXT NOT NULL,
  updated_at   TEXT NOT NULL,
  archived_at  TEXT
) STRICT;

CREATE TABLE spiders (
  id                TEXT PRIMARY KEY,
  code              TEXT NOT NULL UNIQUE,
  name              TEXT,
  species_id        TEXT NOT NULL REFERENCES species(id),
  morph_id          TEXT REFERENCES morphs(id),
  line_id           TEXT REFERENCES lines(id),
  sex               TEXT NOT NULL DEFAULT 'unknown'
                    CHECK (sex IN ('unknown', 'suspected_male', 'suspected_female', 'male', 'female', 'indeterminate')),
  life_stage_id     TEXT REFERENCES life_stages(id),
  status            TEXT NOT NULL DEFAULT 'alive'
                    CHECK (status IN ('alive', 'deceased', 'sold', 'transferred', 'missing')),
  status_changed_at TEXT,
  origin            TEXT NOT NULL DEFAULT 'unknown'
                    CHECK (origin IN ('founder', 'captive_bred', 'transferred', 'unknown')),
  breeding_status   TEXT NOT NULL DEFAULT 'juvenile'
                    CHECK (breeding_status IN ('not_applicable', 'juvenile', 'candidate', 'active_breeder', 'retired')),
  mother_id         TEXT REFERENCES spiders(id) DEFERRABLE INITIALLY DEFERRED,
  father_id         TEXT REFERENCES spiders(id) DEFERRABLE INITIALLY DEFERRED,
  brood_id          TEXT REFERENCES broods(id) DEFERRABLE INITIALLY DEFERRED,
  hatched_on        TEXT,
  acquired_on       TEXT,
  enclosure_id      TEXT REFERENCES enclosures(id),
  premolt_since     TEXT,
  in_molt_since     TEXT,
  notes             TEXT,
  created_at        TEXT NOT NULL,
  updated_at        TEXT NOT NULL,
  archived_at       TEXT
) STRICT;

CREATE TABLE enclosure_history (
  id           TEXT PRIMARY KEY,
  enclosure_id TEXT NOT NULL REFERENCES enclosures(id),
  spider_id    TEXT REFERENCES spiders(id),
  brood_id     TEXT REFERENCES broods(id) DEFERRABLE INITIALLY DEFERRED,
  placed_at    TEXT NOT NULL,
  removed_at   TEXT,
  notes        TEXT,
  created_at   TEXT NOT NULL,
  CHECK ((spider_id IS NULL) <> (brood_id IS NULL))
) STRICT;

CREATE TABLE pairings (
  id           TEXT PRIMARY KEY,
  code         TEXT NOT NULL UNIQUE,
  female_id    TEXT NOT NULL REFERENCES spiders(id),
  male_id      TEXT NOT NULL REFERENCES spiders(id),
  paired_on    TEXT NOT NULL,
  paired_at    TEXT,
  separated_at TEXT,
  result       TEXT NOT NULL DEFAULT 'unknown'
               CHECK (result IN ('mating_observed', 'possible_mating', 'no_mating', 'aggression', 'injury', 'inconclusive', 'unknown')),
  notes        TEXT,
  details      TEXT CHECK (details IS NULL OR json_valid(details)),
  created_at   TEXT NOT NULL,
  updated_at   TEXT NOT NULL,
  voided_at    TEXT,
  void_reason  TEXT
) STRICT;

-- One egg sac and the spiderlings from it (Pedigree8 clutch + brood merged).
CREATE TABLE broods (
  id              TEXT PRIMARY KEY,
  code            TEXT NOT NULL UNIQUE,
  pairing_id      TEXT REFERENCES pairings(id),
  mother_id       TEXT REFERENCES spiders(id) DEFERRABLE INITIALLY DEFERRED,
  father_id       TEXT REFERENCES spiders(id) DEFERRABLE INITIALLY DEFERRED,
  species_id      TEXT REFERENCES species(id),
  morph_id        TEXT REFERENCES morphs(id),
  laid_on         TEXT,
  hatched_on      TEXT,
  emerged_on      TEXT,
  egg_count       INTEGER,
  hatchling_count INTEGER,
  notes           TEXT,
  details         TEXT CHECK (details IS NULL OR json_valid(details)),
  created_at      TEXT NOT NULL,
  updated_at      TEXT NOT NULL,
  archived_at     TEXT
) STRICT;

-- Every husbandry record, one row per event.
-- occurred_on is always set (local date); occurred_at is set when the exact time is known.
CREATE TABLE events (
  id            TEXT PRIMARY KEY,
  type          TEXT NOT NULL
                CHECK (type IN ('feeding', 'hydration', 'molt', 'health', 'sex', 'death', 'note',
                                'acquired', 'status', 'premolt', 'observation')),
  spider_id     TEXT REFERENCES spiders(id),
  enclosure_id  TEXT REFERENCES enclosures(id),
  brood_id      TEXT REFERENCES broods(id),
  pairing_id    TEXT REFERENCES pairings(id),
  code          TEXT UNIQUE,
  occurred_on   TEXT NOT NULL,
  occurred_at   TEXT,
  until_on      TEXT,
  until_at      TEXT,
  date_quality  TEXT NOT NULL DEFAULT 'exact'
                CHECK (date_quality IN ('exact', 'estimated', 'range', 'unknown')),
  action        TEXT,
  outcome       TEXT,
  prey_type_id  TEXT REFERENCES prey_types(id),
  prey_size     TEXT,
  quantity      REAL,
  from_stage_id TEXT REFERENCES life_stages(id),
  to_stage_id   TEXT REFERENCES life_stages(id),
  sex_value     TEXT,
  severity      TEXT,
  notes         TEXT,
  details       TEXT CHECK (details IS NULL OR json_valid(details)),
  source        TEXT NOT NULL DEFAULT 'station' CHECK (source IN ('station', 'migrated')),
  created_at    TEXT NOT NULL,
  updated_at    TEXT NOT NULL,
  voided_at     TEXT,
  void_reason   TEXT,
  CHECK (spider_id IS NOT NULL OR enclosure_id IS NOT NULL OR brood_id IS NOT NULL OR pairing_id IS NOT NULL)
) STRICT;

CREATE INDEX events_spider_idx    ON events (spider_id, type, occurred_on)    WHERE voided_at IS NULL;
CREATE INDEX events_enclosure_idx ON events (enclosure_id, type, occurred_on) WHERE voided_at IS NULL;
CREATE INDEX events_brood_idx     ON events (brood_id, type, occurred_on)     WHERE voided_at IS NULL;
CREATE INDEX enclosure_history_enclosure_idx ON enclosure_history (enclosure_id, removed_at);
CREATE INDEX enclosure_history_spider_idx    ON enclosure_history (spider_id);
CREATE INDEX spiders_mother_idx ON spiders (mother_id);
CREATE INDEX spiders_father_idx ON spiders (father_id);
CREATE INDEX spiders_brood_idx  ON spiders (brood_id);

-- Photos and documents; files live in the media/ folder next to the database.
CREATE TABLE media (
  id            TEXT PRIMARY KEY,
  spider_id     TEXT REFERENCES spiders(id),
  enclosure_id  TEXT REFERENCES enclosures(id),
  brood_id      TEXT REFERENCES broods(id),
  event_id      TEXT REFERENCES events(id),
  kind          TEXT NOT NULL CHECK (kind IN ('photo', 'video', 'document', 'report', 'other')),
  file_name     TEXT NOT NULL,
  relative_path TEXT NOT NULL UNIQUE,
  mime_type     TEXT,
  byte_size     INTEGER,
  sha256        TEXT,
  caption       TEXT,
  taken_at      TEXT,
  created_at    TEXT NOT NULL,
  archived_at   TEXT
) STRICT;

-- Counters for generated codes (e.g. brood B001, pairing PAIR-000001).
-- next_value is the LAST number handed out (same meaning as in Pedigree8).
CREATE TABLE id_sequences (
  key        TEXT PRIMARY KEY,
  next_value INTEGER NOT NULL
) STRICT;

-- Verbatim copy of every migrated Pedigree8 row, so nothing is lost even when
-- the station does not use a field. mapped_to names the station row it became, if any.
CREATE TABLE legacy_records (
  source_table TEXT NOT NULL,
  source_id    TEXT NOT NULL,
  data         TEXT NOT NULL CHECK (json_valid(data)),
  mapped_to    TEXT,
  migrated_at  TEXT NOT NULL,
  PRIMARY KEY (source_table, source_id)
) STRICT;

CREATE TABLE migration_runs (
  id          TEXT PRIMARY KEY,
  started_at  TEXT NOT NULL,
  finished_at TEXT,
  source      TEXT NOT NULL,
  status      TEXT NOT NULL CHECK (status IN ('running', 'succeeded', 'failed')),
  report      TEXT CHECK (report IS NULL OR json_valid(report))
) STRICT;
