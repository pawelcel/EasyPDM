-- Migracja 019: przypisania użytkowników do projektów — zwykły użytkownik ("user") widzi
-- i może przeglądać strukturę tylko tych projektów, do których został przypisany.
-- Administrator zawsze widzi wszystko (sprawdzane w kodzie aplikacji, nie tutaj).
-- Uruchom: psql -h localhost -U pdm_user -d pdm -f db/migrations/019_project_users.sql

BEGIN;

CREATE TABLE IF NOT EXISTS project_users (
    project_id UUID NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    user_id    UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    PRIMARY KEY (project_id, user_id)
);

GRANT SELECT, INSERT, DELETE ON project_users TO pdm_user;

COMMIT;
