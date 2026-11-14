-- A released parts list stays as it was released (Release.cs, TECHNICAL.md "Released parts
-- lists"). Safe to run twice: schema.sql already holds the same tables, and the copies below
-- skip what is already there.
--
-- item_releases: one row per released revision of a part or assembly, with its name, number and
-- properties as they were. Revisions only go forward and a revision is released once, so a row
-- never changes. A row for the current revision of an item means its list is item_release_lines,
-- not item_relations.
--
-- item_release_lines: one row per line of a released list, naming the revision of the child.
-- child_id has no foreign key on purpose: deleting the child is refused while a release names it,
-- and a cascade would quietly shorten a released list.
CREATE TABLE IF NOT EXISTS item_releases (
    id                    UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    item_id               UUID NOT NULL REFERENCES items(id) ON DELETE CASCADE,
    revision_number       INTEGER,
    released_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
    released_by           UUID REFERENCES users(id) ON DELETE SET NULL,
    file_name             TEXT NOT NULL,
    item_number           INTEGER,
    item_number_prefix    TEXT,
    item_number_digits    INTEGER,
    item_number_with_name BOOLEAN,
    properties            JSONB NOT NULL DEFAULT '{}'
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_item_releases_revision ON item_releases (item_id, COALESCE(revision_number, -1));
GRANT SELECT, INSERT ON item_releases TO CURRENT_USER;

CREATE TABLE IF NOT EXISTS item_release_lines (
    release_id     UUID NOT NULL REFERENCES item_releases(id) ON DELETE CASCADE,
    child_id       UUID NOT NULL,
    child_revision INTEGER,
    quantity       NUMERIC,
    position       INTEGER NOT NULL,
    PRIMARY KEY (release_id, child_id)
);
CREATE INDEX IF NOT EXISTS idx_item_release_lines_child ON item_release_lines (child_id);
GRANT SELECT, INSERT ON item_release_lines TO CURRENT_USER;

-- What is released today is copied as it stands. One whose part or sub-assembly has already gone
-- back to work is left out: its released list is not known any more, so it stays read live.
INSERT INTO item_releases
    (item_id, revision_number, released_at, file_name, item_number, item_number_prefix,
     item_number_digits, item_number_with_name, properties)
SELECT i.id, i.revision_number,
       COALESCE((SELECT max(h.changed_at) FROM item_status_history h
                 WHERE h.item_id = i.id AND h.to_status = 'wydany'), now()),
       i.file_name, i.item_number, i.item_number_prefix, i.item_number_digits, i.item_number_with_name, i.properties
FROM items i
WHERE i.status IN ('wydany', 'anulowana') AND i.item_type IN ('part', 'assembly')
  AND NOT EXISTS (SELECT 1 FROM item_relations ir JOIN items c ON c.id = ir.child_id
                  WHERE ir.parent_id = i.id AND c.item_type IN ('part', 'assembly')
                    AND c.status IS DISTINCT FROM 'wydany' AND c.status IS DISTINCT FROM 'anulowana')
ON CONFLICT DO NOTHING;

INSERT INTO item_release_lines (release_id, child_id, child_revision, quantity, position)
SELECT r.id, ir.child_id, c.revision_number, ir.quantity, ir.position
FROM item_releases r
JOIN items p ON p.id = r.item_id AND p.revision_number IS NOT DISTINCT FROM r.revision_number
JOIN item_relations ir ON ir.parent_id = r.item_id
JOIN items c ON c.id = ir.child_id
WHERE r.released_by IS NULL AND p.status IN ('wydany', 'anulowana')
ON CONFLICT DO NOTHING;
