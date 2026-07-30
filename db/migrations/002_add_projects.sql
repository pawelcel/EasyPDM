-- Migracja 002: dodanie projektów jako kontenera dla elementów.
-- Bezpieczna do uruchomienia na już istniejącej bazie (nie kasuje danych).
-- Uruchom: psql -h localhost -U pdm_user -d pdm -f db/migrations/002_add_projects.sql

BEGIN;

CREATE TABLE IF NOT EXISTS projects (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name        TEXT NOT NULL UNIQUE,
    description TEXT,
    created_at  TIMESTAMPTZ DEFAULT now()
);

-- Element z Fazy skanowania dysku (DoBlokadOkna.FCStd) nie miał jeszcze pojęcia projektu —
-- trafi tu, żeby nic nie stracić, zamiast blokować migrację brakiem project_id.
INSERT INTO projects (name, description)
VALUES ('Zaimportowane ze skanera', 'Elementy dodane przed wprowadzeniem systemu projektów')
ON CONFLICT (name) DO NOTHING;

ALTER TABLE items ADD COLUMN IF NOT EXISTS project_id UUID REFERENCES projects(id);

UPDATE items
SET project_id = (SELECT id FROM projects WHERE name = 'Zaimportowane ze skanera')
WHERE project_id IS NULL;

ALTER TABLE items ALTER COLUMN project_id SET NOT NULL;

CREATE INDEX IF NOT EXISTS idx_items_project ON items (project_id);

COMMIT;
