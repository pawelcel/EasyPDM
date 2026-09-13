BEGIN;

-- A closed project drops out of the "active projects" lists (project selector, "My
-- projects", the new-item project picker) but is otherwise untouched -- its items stay
-- fully searchable through "Whole database" and the flag can be toggled back off.
ALTER TABLE projects ADD COLUMN IF NOT EXISTS closed BOOLEAN NOT NULL DEFAULT false;

COMMIT;
