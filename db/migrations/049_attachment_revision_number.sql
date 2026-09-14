BEGIN;

ALTER TABLE item_attachments ADD COLUMN IF NOT EXISTS revision_number INTEGER;

COMMIT;
