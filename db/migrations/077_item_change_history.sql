-- Who changed an item's name, properties and tags, and when (the item's History panel). Before
-- this, only creation, status, revisions, attachments and owner lock/release recorded their
-- author. One row per real change: sending the same value again adds nothing.
-- old_value/new_value hold the value as text (a JSON string without its quotes, anything else as
-- JSON); NULL means the key or name did not exist on that side.
-- Safe to run twice: schema.sql already holds the same table.

CREATE TABLE IF NOT EXISTS item_change_history (
    id        UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    item_id   UUID NOT NULL REFERENCES items(id) ON DELETE CASCADE,
    change    TEXT NOT NULL CHECK (change IN ('name', 'property_set', 'property_removed', 'tag_added', 'tag_removed')),
    prop_key  TEXT,
    old_value TEXT,
    new_value TEXT,
    user_id   UUID REFERENCES users(id) ON DELETE SET NULL,
    at        TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_item_change_history_item ON item_change_history (item_id);

GRANT SELECT, INSERT ON item_change_history TO CURRENT_USER;
