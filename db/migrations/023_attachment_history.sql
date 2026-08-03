-- Migracja 023: historia dodawania/usuwania załączników (do panelu "Historia" Części/
-- Złożenia) — osobna tabela, bo usunięty załącznik znika z item_attachments, więc samo
-- to nie wystarczy do zachowania śladu "kto i kiedy go usunął".
-- Uruchom: psql -h localhost -U pdm_user -d pdm -f db/migrations/023_attachment_history.sql

BEGIN;

CREATE TABLE item_attachment_history (
    id        UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    item_id   UUID NOT NULL REFERENCES items(id) ON DELETE CASCADE,
    file_name TEXT NOT NULL,
    action    TEXT NOT NULL CHECK (action IN ('added', 'removed')),
    user_id   UUID REFERENCES users(id) ON DELETE SET NULL,
    at        TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_item_attachment_history_item ON item_attachment_history (item_id);

GRANT SELECT, INSERT ON item_attachment_history TO pdm_user;

COMMIT;
