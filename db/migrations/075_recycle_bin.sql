-- Recycle bin (RecycleBin.cs, BinEndpoints.cs). Deleting an item no longer destroys it: the item,
-- what goes with it, and every row that hangs off them (relations, attachments, history,
-- revisions, client verifications...) are written here as one JSON payload, keyed by
-- table name, and then removed from the live tables. The stored files stay on disk. Restoring
-- writes the rows back; purging deletes the payload and the files for good.
--
-- The rows leave the live tables rather than staying there with a deleted flag, because every
-- read of items and the tree would otherwise need a filter, and one missed filter shows something
-- deleted. Out of the tables, a deleted item answers 404 everywhere without a change.
--
-- project_id, parent_id and deleted_by have no foreign keys: the project, folder or person may go
-- while the entry waits, and the entry must still list and restore (restore asks for a folder).
-- schema.sql holds this table too and a fresh install runs this migration after it: it may run twice.
CREATE TABLE IF NOT EXISTS recycle_bin (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    root_item_id    UUID NOT NULL,
    item_type       TEXT NOT NULL,
    label           TEXT NOT NULL,
    item_count      INTEGER NOT NULL,
    project_id      UUID,
    project_name    TEXT,
    parent_ids      UUID[] NOT NULL DEFAULT '{}',
    location        TEXT,
    deleted_by      UUID,
    deleted_by_name TEXT NOT NULL,
    deleted_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    payload         JSONB NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_recycle_bin_deleted_at ON recycle_bin (deleted_at DESC);
CREATE INDEX IF NOT EXISTS idx_recycle_bin_deleted_by ON recycle_bin (deleted_by);
GRANT SELECT, INSERT, DELETE ON recycle_bin TO CURRENT_USER;
