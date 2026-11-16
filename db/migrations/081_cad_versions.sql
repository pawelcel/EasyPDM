BEGIN;

-- Every CAD file version an item has had (one row per upload with role "cad"), kept also after
-- a later upload of the same revision replaces the attachment itself. Lets a CAD client ask
-- whether its local copy of a component is the current file, an older one, or one the server
-- has never seen (POST /api/items/cad-versions, CadVersionEndpoints.cs).
--   content_version  identifier the CAD client stamped into the file before sending it (the
--                    FreeCAD add-on writes it into the document's Meta); survives re-saving the
--                    file without changes, which a hash does not. NULL from clients without it.
--   file_hash        SHA-256 of the uploaded bytes, the fallback for files without a stamp.
CREATE TABLE IF NOT EXISTS item_cad_versions (
    id               UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    item_id          UUID NOT NULL REFERENCES items(id) ON DELETE CASCADE,
    attachment_id    UUID,
    file_name        TEXT NOT NULL,
    content_version  TEXT,
    file_hash        TEXT,
    revision_number  INTEGER,
    uploaded_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    uploaded_by      UUID REFERENCES users(id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS idx_item_cad_versions_item ON item_cad_versions (item_id);

ALTER TABLE item_attachments ADD COLUMN IF NOT EXISTS content_version TEXT;

-- The CAD files already stored become the first known versions, so a component downloaded
-- before this migration is recognised by its hash.
INSERT INTO item_cad_versions (item_id, attachment_id, file_name, file_hash, revision_number, uploaded_at, uploaded_by)
SELECT a.item_id, a.id, a.file_name, a.file_hash, a.revision_number, COALESCE(a.uploaded_at, now()), a.uploaded_by
FROM item_attachments a
WHERE a.preview_role = 'cad'
  AND NOT EXISTS (SELECT 1 FROM item_cad_versions v WHERE v.attachment_id = a.id);

COMMIT;
