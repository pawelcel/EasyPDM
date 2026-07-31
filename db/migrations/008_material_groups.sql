-- Migracja 008: grupa materiału — pole czysto porządkowe/filtrujące na katalogu
-- materiałów, nigdy nie trafia do właściwości Części (Część zapisuje tylko nazwę materiału).
-- Uruchom: psql -h localhost -U pdm_user -d pdm -f db/migrations/008_material_groups.sql

BEGIN;

ALTER TABLE materials ADD COLUMN IF NOT EXISTS group_name TEXT;

COMMIT;
