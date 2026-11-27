BEGIN;

-- One person can hold several roles in a project (purchasing and acceptance, say), so a row is
-- identified by its own id instead of (project_id, contact_id). The same person with the very
-- same role twice is still refused.
ALTER TABLE project_contacts ADD COLUMN IF NOT EXISTS id BIGSERIAL;
ALTER TABLE project_contacts DROP CONSTRAINT IF EXISTS project_contacts_pkey;
ALTER TABLE project_contacts ADD PRIMARY KEY (id);
CREATE UNIQUE INDEX IF NOT EXISTS uq_project_contacts_role ON project_contacts (project_id, contact_id, role);
CREATE INDEX IF NOT EXISTS idx_project_contacts_project ON project_contacts (project_id);

COMMIT;
