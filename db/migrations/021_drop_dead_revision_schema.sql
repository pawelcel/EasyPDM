-- Migracja 021: usunięcie martwego schematu z wczesnej, porzuconej koncepcji rewizji/checkout.
-- Żadna z poniższych tabel/kolumn nigdy nie była czytana ani zapisywana przez kod aplikacji
-- (backend ani frontend) — realny system rewizji/statusu to items.status + items.revision_number
-- + item_revision_comments (patrz PATCH /api/items/{id}/status), a realny system właściciela/
-- blokady to items.owner_id + items.owner_locked (migracja 020). Te martwe pozostałości tylko
-- mylą — wyglądają jak osobny, działający system "checkout", którego nigdy faktycznie nie było.
-- Uruchom: psql -h localhost -U pdm_user -d pdm -f db/migrations/021_drop_dead_revision_schema.sql

BEGIN;

DROP TABLE IF EXISTS checkout_history;

ALTER TABLE items DROP CONSTRAINT IF EXISTS fk_items_current_revision;
ALTER TABLE items DROP COLUMN IF EXISTS current_revision_id;
ALTER TABLE items DROP COLUMN IF EXISTS checked_out_by;
ALTER TABLE items DROP COLUMN IF EXISTS checked_out_at;

DROP TABLE IF EXISTS item_revisions;
DROP TYPE IF EXISTS item_status;

DROP TABLE IF EXISTS property_definitions;

COMMIT;
