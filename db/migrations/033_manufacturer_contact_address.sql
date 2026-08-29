-- Migracja 033: pole adresu dla osoby kontaktowej producenta.
-- Uruchom: psql -h localhost -U pdm_user -d pdm -f db/migrations/033_manufacturer_contact_address.sql

BEGIN;

ALTER TABLE manufacturer_contacts
    ADD COLUMN IF NOT EXISTS address TEXT;

COMMIT;
