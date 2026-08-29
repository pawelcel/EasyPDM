-- Migracja 034: pole adresu dla osoby kontaktowej klienta (ujednolicone z osobą
-- kontaktową producenta, zob. 033_manufacturer_contact_address.sql).
-- Uruchom: psql -h localhost -U pdm_user -d pdm -f db/migrations/034_client_contact_address.sql

BEGIN;

ALTER TABLE client_contacts
    ADD COLUMN IF NOT EXISTS address TEXT;

COMMIT;
