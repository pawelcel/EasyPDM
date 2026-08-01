-- Migracja 014: items.project_id -> ON DELETE CASCADE.
-- Migracja 002 stworzyła ten klucz obcy BEZ ON DELETE CASCADE (db/schema.sql od dawna
-- deklaruje go już z CASCADE dla świeżych instalacji, ale istniejące bazy nigdy nie
-- dostały odpowiadającej migracji) — w efekcie DELETE /api/projects/{id} kończył się
-- błędem 500 (naruszenie klucza obcego) dla każdego niepustego projektu.
-- Uruchom: psql -h localhost -U pdm_user -d pdm -f db/migrations/014_items_project_cascade.sql

BEGIN;

ALTER TABLE items DROP CONSTRAINT items_project_id_fkey;
ALTER TABLE items ADD CONSTRAINT items_project_id_fkey
    FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE;

COMMIT;
