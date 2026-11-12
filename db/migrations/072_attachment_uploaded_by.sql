BEGIN;

-- Who uploaded each item attachment. The panel showed when and how big a CAD file was but not
-- who sent it; that was only in the item's History. ON DELETE SET NULL like every other
-- "who" column, so deleting an account never deletes files.
ALTER TABLE item_attachments
    ADD COLUMN IF NOT EXISTS uploaded_by UUID REFERENCES users(id) ON DELETE SET NULL;

-- Existing files: take the author from item_attachment_history, which has recorded every
-- "added" since migration 023. The history row is written right after the attachment row, so
-- the nearest "added" entry for the same item and file name within a minute is the upload
-- itself; anything older or ambiguous stays NULL rather than naming the wrong person.
UPDATE item_attachments a
SET uploaded_by = (
    SELECT h.user_id
    FROM item_attachment_history h
    WHERE h.item_id = a.item_id
      AND h.file_name = a.file_name
      AND h.action = 'added'
      AND abs(extract(epoch FROM h.at - a.uploaded_at)) < 60
    ORDER BY abs(extract(epoch FROM h.at - a.uploaded_at))
    LIMIT 1
)
WHERE a.uploaded_by IS NULL AND a.uploaded_at IS NOT NULL;

COMMIT;
