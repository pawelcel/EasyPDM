-- Migracja 044: Nazwa 2 klienta dostaje własne właściwości -- adres i kontakty, osobne od
-- klienta-rodzica. Kontakty klienta (name2_id IS NULL) są odziedziczone: widoczne (tylko do
-- odczytu) pod każdą jego Nazwą 2, ale kontakt dodany bezpośrednio do jednej Nazwy 2
-- (name2_id ustawiony) jest widoczny WYŁĄCZNIE tam.
-- Uruchom: psql -U pdm_user -d pdm -f 044_client_name2_own_properties.sql

BEGIN;

ALTER TABLE client_name2 ADD COLUMN IF NOT EXISTS location TEXT;

ALTER TABLE client_contacts ADD COLUMN IF NOT EXISTS name2_id INTEGER REFERENCES client_name2(id) ON DELETE CASCADE;
CREATE INDEX IF NOT EXISTS idx_client_contacts_name2 ON client_contacts (name2_id);

COMMIT;
