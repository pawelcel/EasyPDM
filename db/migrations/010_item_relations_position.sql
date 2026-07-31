-- Migracja 010: kolejność pozycji BOM (L.p.) — pozwala ręcznie ustawić numer porządkowy
-- podelementu w złożeniu (wpisanie liczby albo przeciągnięcie w inne miejsce w UI).
-- Uruchom: psql -h localhost -U pdm_user -d pdm -f db/migrations/010_item_relations_position.sql

BEGIN;

ALTER TABLE item_relations ADD COLUMN IF NOT EXISTS position INTEGER;

-- Backfill: istniejące relacje dostają kolejne numery w ramach każdego rodzica
-- (kolejność dowolna — user i tak może je potem przenumerować).
WITH numbered AS (
    SELECT parent_id, child_id, ROW_NUMBER() OVER (PARTITION BY parent_id ORDER BY child_id) AS rn
    FROM item_relations
    WHERE position IS NULL
)
UPDATE item_relations ir
SET position = numbered.rn
FROM numbered
WHERE ir.parent_id = numbered.parent_id AND ir.child_id = numbered.child_id;

ALTER TABLE item_relations ALTER COLUMN position SET NOT NULL;

COMMIT;
