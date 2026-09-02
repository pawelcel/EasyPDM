-- Migracja 038: znacznik "to jest przykładowy projekt startowy" (zob. Program.cs,
-- EnsureSampleProjectAsync) -- do ewentualnego rozpoznania/obsługi w przyszłości,
-- dziś czysto informacyjny.
-- Uruchom: psql -U pdm_user -d pdm -f 038_project_is_sample.sql

BEGIN;

ALTER TABLE projects ADD COLUMN IF NOT EXISTS is_sample BOOLEAN NOT NULL DEFAULT false;

COMMIT;
