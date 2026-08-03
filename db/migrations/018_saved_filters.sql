-- Migracja 018: zapisane filtry widoku "Cała baza" — każdy użytkownik zapisuje własne
-- zestawy filtrów (wyszukiwanie, tag, typ rekordu, rodzaj części, producent) pod wybraną
-- nazwą; zapis pod istniejącą nazwą nadpisuje poprzedni (UNIQUE (user_id, name) + upsert
-- w zapytaniu aplikacji).
-- Uruchom: psql -h localhost -U pdm_user -d pdm -f db/migrations/018_saved_filters.sql

BEGIN;

CREATE TABLE IF NOT EXISTS saved_filters (
    id         UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id    UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    name       TEXT NOT NULL,
    filters    JSONB NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (user_id, name)
);

GRANT SELECT, INSERT, UPDATE, DELETE ON saved_filters TO pdm_user;

COMMIT;
