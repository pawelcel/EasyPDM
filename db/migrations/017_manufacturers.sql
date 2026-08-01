-- Migracja 017: katalog producentów (dla razie niezależny od Części — samo repozytorium
-- nazw producentów i ich osób kontaktowych, zarządzany z nowej pozycji w panelu bocznym).
-- Uruchom: psql -h localhost -U pdm_user -d pdm -f db/migrations/017_manufacturers.sql

BEGIN;

CREATE TABLE IF NOT EXISTS manufacturers (
    id   SERIAL PRIMARY KEY,
    name TEXT NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS manufacturer_contacts (
    id              SERIAL PRIMARY KEY,
    manufacturer_id INTEGER NOT NULL REFERENCES manufacturers(id) ON DELETE CASCADE,
    first_name      TEXT,
    last_name       TEXT,
    phone           TEXT,
    position        TEXT,
    email           TEXT
);

CREATE INDEX IF NOT EXISTS idx_manufacturer_contacts_manufacturer ON manufacturer_contacts (manufacturer_id);

COMMIT;
