-- Migracja 022: historia Części/Złożenia (do wyświetlenia na dole panelu właściwości) —
-- kto i kiedy utworzył element, zmiany statusu (kto/kiedy/z-na) oraz rewizje z komentarzem
-- (kto/kiedy/opis, rozszerzenie już istniejącej item_revision_comments o autora).
-- Uruchom: psql -h localhost -U pdm_user -d pdm -f db/migrations/022_item_history.sql

BEGIN;

ALTER TABLE items ADD COLUMN created_by UUID REFERENCES users(id) ON DELETE SET NULL;

ALTER TABLE item_revision_comments ADD COLUMN created_by UUID REFERENCES users(id) ON DELETE SET NULL;

CREATE TABLE item_status_history (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    item_id     UUID NOT NULL REFERENCES items(id) ON DELETE CASCADE,
    from_status TEXT,
    to_status   TEXT NOT NULL,
    changed_by  UUID REFERENCES users(id) ON DELETE SET NULL,
    changed_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_item_status_history_item ON item_status_history (item_id);

GRANT SELECT, INSERT ON item_status_history TO pdm_user;

COMMIT;
