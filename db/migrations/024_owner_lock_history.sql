-- Migracja 024: historia blokady/zwolnienia właściciela (do panelu "Historia" Części/
-- Złożenia) — kto i kiedy zablokował (przejął na własność) albo zwolnił element.
-- Uruchom: psql -h localhost -U pdm_user -d pdm -f db/migrations/024_owner_lock_history.sql

BEGIN;

CREATE TABLE item_owner_history (
    id      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    item_id UUID NOT NULL REFERENCES items(id) ON DELETE CASCADE,
    action  TEXT NOT NULL CHECK (action IN ('locked', 'released')),
    user_id UUID REFERENCES users(id) ON DELETE SET NULL,
    at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_item_owner_history_item ON item_owner_history (item_id);

GRANT SELECT, INSERT ON item_owner_history TO pdm_user;

COMMIT;
