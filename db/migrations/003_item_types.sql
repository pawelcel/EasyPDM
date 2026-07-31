-- Migracja 003: typy elementów (Folder / Część / Plik) + numeracja Części.
-- Foldery i Części to kontenery bez własnego pliku — pliki dołącza się do nich
-- jako osobne elementy-dzieci w istniejącej strukturze item_relations.
-- Bezpieczna do uruchomienia na już istniejącej bazie (nie kasuje danych,
-- wszystkie dotychczasowe wiersze items stają się typu 'file').
-- Wymaga uprawnień właściciela tabeli items — uruchom jako superuser/postgres,
-- nie jako pdm_user (ta sama zasada, co przy tworzeniu schematu):
-- Uruchom: psql -h localhost -U postgres -d pdm -f db/migrations/003_item_types.sql

BEGIN;

ALTER TABLE items ADD COLUMN IF NOT EXISTS item_type TEXT NOT NULL DEFAULT 'file';
ALTER TABLE items ADD CONSTRAINT items_item_type_check
    CHECK (item_type IN ('folder', 'part', 'file'));

-- Tylko elementy typu 'file' mają rzeczywisty plik — reszta kolumn plikowych
-- staje się opcjonalna.
ALTER TABLE items ALTER COLUMN file_path DROP NOT NULL;
ALTER TABLE items ALTER COLUMN file_type DROP NOT NULL;

CREATE SEQUENCE IF NOT EXISTS item_number_seq START 1;
GRANT USAGE, SELECT ON SEQUENCE item_number_seq TO pdm_user;
ALTER TABLE items ADD COLUMN IF NOT EXISTS item_number INTEGER;

INSERT INTO property_definitions (key, display_name, data_type, enum_values) VALUES
    ('rodzaj', 'Rodzaj', 'enum', ARRAY['Zakupowa', 'Wykonywana'])
ON CONFLICT (key) DO NOTHING;

COMMIT;
