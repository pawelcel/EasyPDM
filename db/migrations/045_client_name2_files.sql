BEGIN;

-- NULL = węzeł należy do samego klienta (rodzica), widoczny (tylko do odczytu z tego
-- poziomu) pod każdą jego Nazwą 2 -- ten sam wzorzec co client_contacts.name2_id z
-- migracji 044. Nie-NULL = węzeł należy WYŁĄCZNIE do tej jednej Nazwy 2.
ALTER TABLE client_nodes ADD COLUMN IF NOT EXISTS name2_id INTEGER REFERENCES client_name2(id) ON DELETE CASCADE;
CREATE INDEX IF NOT EXISTS idx_client_nodes_name2 ON client_nodes (name2_id);

COMMIT;
