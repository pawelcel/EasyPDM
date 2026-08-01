-- Migracja 013: właściwości projektu (Klient, Data rozpoczęcia, Data zakończenia).
-- Tworzenie/edycja/usuwanie projektów jest teraz ograniczone do administratora —
-- sprawdzane w kodzie C# (ProjectEndpoints), nie wymaga zmian w SQL-u.
-- Uruchom: psql -h localhost -U pdm_user -d pdm -f db/migrations/013_project_properties.sql

BEGIN;

ALTER TABLE projects ADD COLUMN IF NOT EXISTS client TEXT;
ALTER TABLE projects ADD COLUMN IF NOT EXISTS start_date DATE;
ALTER TABLE projects ADD COLUMN IF NOT EXISTS end_date DATE;

COMMIT;
