-- Migracja 009: załączniki (pliki dograne "z zewnątrz" do Części/Złożenia/Pliku) —
-- w odróżnieniu od struktury (item_relations), załącznik NIE jest osobnym elementem
-- w drzewku: nie ma własnego item_type, nie pokazuje się po lewej stronie i nie da się
-- go stamtąd usunąć — zarządza się nim wyłącznie z panelu właściwości po prawej.
-- Uruchom: psql -h localhost -U pdm_user -d pdm -f db/migrations/009_item_attachments.sql

BEGIN;

CREATE TABLE IF NOT EXISTS item_attachments (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    item_id     UUID NOT NULL REFERENCES items(id) ON DELETE CASCADE,
    file_name   TEXT NOT NULL,
    file_path   TEXT NOT NULL UNIQUE,
    file_hash   TEXT,
    file_size   BIGINT,
    uploaded_at TIMESTAMPTZ DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_item_attachments_item ON item_attachments (item_id);

COMMIT;
