BEGIN;

-- Prowadzący projekt -- opiekun po stronie klienta, wybierany z jego listy kontaktów
-- (client_contacts): zarówno kontakty samego klienta (name2_id IS NULL) jak i kontakty
-- przypisane do konkretnej Nazwy 2 wskazanej na projekcie (client_name2_id). Walidacja tej
-- zgodności jest po stronie aplikacji (ProjectEndpoints.ValidateLeadContactAsync), tak samo
-- jak przy client_name2_id -- FK samo w sobie nie wymusza spójności między dwiema kolumnami.
ALTER TABLE projects ADD COLUMN IF NOT EXISTS lead_contact_id INTEGER REFERENCES client_contacts(id) ON DELETE SET NULL;
CREATE INDEX IF NOT EXISTS idx_projects_lead_contact ON projects (lead_contact_id);

COMMIT;
