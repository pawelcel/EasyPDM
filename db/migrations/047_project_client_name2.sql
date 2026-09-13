BEGIN;

-- Projekt wskazuje dziś klienta wyłącznie przez client_id -- ale klient może mieć wiele
-- Nazw 2 (client_name2, relacja 1:N), więc to nie wystarcza, gdy projekt trzeba pokazać z
-- konkretną Nazwą 2 (np. "Projekt (Klient, Nazwa2)" w selektorze projektów). ON DELETE
-- SET NULL (nie CASCADE) -- usunięcie Nazwy 2 ma tylko odpiąć projekt, nie go kasować, tak
-- samo jak istniejący projects.client_id.
ALTER TABLE projects ADD COLUMN IF NOT EXISTS client_name2_id INTEGER REFERENCES client_name2(id) ON DELETE SET NULL;
CREATE INDEX IF NOT EXISTS idx_projects_client_name2 ON projects (client_name2_id);

COMMIT;
