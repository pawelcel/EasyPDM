-- Migracja 016: kolejność elementów bez rodzica (korzeni drzewka) w obrębie projektu.
-- Dotąd korzenie sortowały się tylko alfabetycznie (kolejność z GET /api/items) — teraz
-- mają własną, przeciąganą ręcznie kolejność, tak jak L.p. w item_relations dla dzieci.
-- Backfill zachowuje dzisiejszy porządek alfabetyczny, żeby nic się wizualnie nie zmieniło
-- do czasu pierwszego ręcznego przeciągnięcia.
-- Uruchom: psql -h localhost -U pdm_user -d pdm -f db/migrations/016_root_position.sql

BEGIN;

ALTER TABLE items ADD COLUMN IF NOT EXISTS root_position INTEGER;

WITH numbered AS (
    SELECT id, ROW_NUMBER() OVER (PARTITION BY project_id ORDER BY file_name) AS rn
    FROM items
)
UPDATE items i SET root_position = n.rn
FROM numbered n
WHERE i.id = n.id;

ALTER TABLE items ALTER COLUMN root_position SET NOT NULL;
ALTER TABLE items ALTER COLUMN root_position SET DEFAULT 1;

COMMIT;
