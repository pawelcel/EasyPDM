-- Migracja 006: status Części/Złożenia (W pracy / Sprawdzany / Wydany) + numer rewizji.
-- Status dotyczy tylko item_type IN ('part','assembly') — dla folderów/plików kolumna
-- zostaje NULL (status się do nich nie stosuje).
-- Wymaga uprawnień właściciela tabeli items — uruchom jako superuser/postgres:
-- Uruchom: psql -h localhost -U postgres -d pdm -f db/migrations/006_item_status.sql

BEGIN;

ALTER TABLE items ADD COLUMN IF NOT EXISTS status TEXT
    CHECK (status IN ('w_pracy', 'sprawdzany', 'wydany'));
ALTER TABLE items ADD COLUMN IF NOT EXISTS revision_number INTEGER;

-- Istniejące Części/Złożenia (utworzone przed tą migracją) dostają status startowy,
-- żeby maszyna stanów miała od czego zacząć zamiast NULL.
UPDATE items SET status = 'w_pracy', revision_number = 1
WHERE item_type IN ('part', 'assembly') AND status IS NULL;

COMMIT;
