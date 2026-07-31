-- Migracja 011: opcjonalny komentarz do rewizji Części/Złożenia — tworzony zarówno
-- w aplikacji webowej (zmiana statusu Wydany -> W pracy), jak i przez makro FreeCAD.
-- Uruchom: psql -h localhost -U pdm_user -d pdm -f db/migrations/011_item_revision_comments.sql

BEGIN;

CREATE TABLE IF NOT EXISTS item_revision_comments (
    item_id         UUID NOT NULL REFERENCES items(id) ON DELETE CASCADE,
    revision_number INTEGER NOT NULL,
    comment         TEXT NOT NULL,
    created_at      TIMESTAMPTZ DEFAULT now(),
    PRIMARY KEY (item_id, revision_number)
);

COMMIT;
