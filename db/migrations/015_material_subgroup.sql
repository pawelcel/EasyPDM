-- Migracja 015: podgrupa materiału — kolejne pole czysto porządkowe/filtrujące na
-- katalogu materiałów (obok istniejącej grupy), nigdy nie trafia do właściwości Części.
-- Uruchom: psql -h localhost -U pdm_user -d pdm -f db/migrations/015_material_subgroup.sql

BEGIN;

ALTER TABLE materials ADD COLUMN IF NOT EXISTS subgroup_name TEXT;

COMMIT;
