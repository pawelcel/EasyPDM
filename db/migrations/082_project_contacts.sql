BEGIN;

-- People on the client's side involved in a project, each with their role in it (purchasing,
-- acceptance, quality...), next to the project lead (projects.lead_contact_id). They come from
-- the same contacts as the lead: the client's own (name2_id IS NULL) and those of the project's
-- Name 2; the application keeps that in step when the project's client or Name 2 changes.
CREATE TABLE IF NOT EXISTS project_contacts (
    project_id  UUID NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    contact_id  INTEGER NOT NULL REFERENCES client_contacts(id) ON DELETE CASCADE,
    role        TEXT NOT NULL DEFAULT '',
    added_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (project_id, contact_id)
);
CREATE INDEX IF NOT EXISTS idx_project_contacts_contact ON project_contacts (contact_id);

COMMIT;
