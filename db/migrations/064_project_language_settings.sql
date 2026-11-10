BEGIN;

-- Języki dokumentacji w projekcie (właściwości projektu):
--
--  * main_language_id -- główny język projektu. Każdy projekt go ma, wielojęzyczny czy nie.
--    Nowy projekt dostaje domyślny język dokumentacji z ustawień (DEFAULT przez funkcję,
--    więc działa dla każdego INSERT-a, także projektu startowego w Program.cs), a istniejące
--    projekty -- ten sam domyślny przy dodaniu kolumny. To migawka z chwili utworzenia:
--    późniejsza zmiana domyślnego języka w ustawieniach nie przestawia istniejących projektów.
--  * multilingual + project_language_links -- czy projekt jest wielojęzyczny i w jakich
--    językach. Lista zawiera też język główny (pilnuje tego API), więc "w jakich językach jest
--    projekt" to po prostu ta tabela. Projekt jednojęzyczny ma ją pustą.
--
-- Język użyty w projekcie (jako główny albo na liście) nie da się usunąć z ustawień
-- (ON DELETE RESTRICT) -- zniknąłby po cichu z projektu, który na nim polega.
--
-- Schemat świeżej instalacji (db/schema.sql) ma już te kolumny i tabelę, a ta migracja i tak
-- się na nim wykona -- stąd IF NOT EXISTS / OR REPLACE.
CREATE OR REPLACE FUNCTION default_project_language_id() RETURNS INTEGER
LANGUAGE sql STABLE AS $$ SELECT id FROM project_languages WHERE is_default $$;

ALTER TABLE projects ADD COLUMN IF NOT EXISTS main_language_id INTEGER NOT NULL
    DEFAULT default_project_language_id() REFERENCES project_languages(id) ON DELETE RESTRICT;
ALTER TABLE projects ADD COLUMN IF NOT EXISTS multilingual BOOLEAN NOT NULL DEFAULT false;

CREATE TABLE IF NOT EXISTS project_language_links (
    project_id   UUID    NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    language_id  INTEGER NOT NULL REFERENCES project_languages(id) ON DELETE RESTRICT,
    PRIMARY KEY (project_id, language_id)
);

COMMIT;
