BEGIN;

-- Which EasyPDM drawing sheet a PDF was exported from ("easypdm-A3", "easypdm-A2",
-- "easypdm-A1"), sent by the CAD macros with the drawing's PDF. When the item is released the
-- server stamps "approved by" and the date into that PDF at the positions the layout defines
-- (DrawingStamp.cs). NULL = any other PDF, which is never touched.
ALTER TABLE item_attachments ADD COLUMN IF NOT EXISTS sheet_layout TEXT;

COMMIT;
