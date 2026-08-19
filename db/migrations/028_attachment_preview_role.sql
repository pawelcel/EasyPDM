-- Migracja 028: opcjonalna "rola" załącznika (pdf/step) — pozwala wprost oznaczyć KTÓRY
-- załącznik jest "tym" rysunkiem 2D, a który "tym" modelem 3D dla stałego podglądu w
-- panelu właściwości elementu, zamiast zgadywać po pierwszym pasującym rozszerzeniu (co
-- było niejednoznaczne przy więcej niż jednym pliku PDF/STEP na element).
-- Uruchom: psql -h localhost -U pdm_user -d pdm -f db/migrations/028_attachment_preview_role.sql

BEGIN;

ALTER TABLE item_attachments
    ADD COLUMN IF NOT EXISTS preview_role TEXT CHECK (preview_role IN ('pdf', 'step'));

COMMIT;
