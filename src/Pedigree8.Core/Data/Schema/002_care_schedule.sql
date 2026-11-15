-- Per-spider care rhythm. NULL means "use the standard rule".
ALTER TABLE spiders ADD COLUMN feed_every_days INTEGER CHECK (feed_every_days IS NULL OR feed_every_days BETWEEN 1 AND 60);
ALTER TABLE spiders ADD COLUMN mist_every_days INTEGER CHECK (mist_every_days IS NULL OR mist_every_days BETWEEN 1 AND 60);

CREATE INDEX media_spider_idx ON media (spider_id) WHERE archived_at IS NULL;
