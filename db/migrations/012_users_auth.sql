-- Migracja 012: logowanie (nazwa użytkownika + hasło) i role (admin/user).
-- Tabela users już istniała (pusta — nic dotąd do niej nie wpisywało), więc bezpiecznie
-- dodajemy kolumny NOT NULL. Domyślne konto administratora (login "admin") jest tworzone
-- przez EasyPDM.Api przy starcie, jeśli tabela users jest pusta — nie tutaj, bo hasło
-- trzeba zahaszować kodem C#, nie SQL-em.
-- Uruchom: psql -h localhost -U pdm_user -d pdm -f db/migrations/012_users_auth.sql

BEGIN;

ALTER TABLE users ADD COLUMN IF NOT EXISTS password_hash TEXT NOT NULL;
ALTER TABLE users ADD COLUMN IF NOT EXISTS role TEXT NOT NULL DEFAULT 'user';
ALTER TABLE users ADD CONSTRAINT users_role_check CHECK (role IN ('admin', 'user'));

CREATE TABLE IF NOT EXISTS sessions (
    token      TEXT PRIMARY KEY,
    user_id    UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    created_at TIMESTAMPTZ DEFAULT now(),
    expires_at TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_sessions_user ON sessions (user_id);

GRANT SELECT, INSERT, UPDATE, DELETE ON sessions TO pdm_user;

COMMIT;
