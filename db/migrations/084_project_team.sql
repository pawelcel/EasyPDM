BEGIN;

-- The company's side of a project: its lead among EasyPDM users, and the people taking part, each
-- with a role (one person may hold several). Picked from the users who can see the project
-- (project_users) and the administrators; the application keeps the list to those.
ALTER TABLE projects ADD COLUMN IF NOT EXISTS lead_user_id UUID REFERENCES users(id) ON DELETE SET NULL;

CREATE TABLE IF NOT EXISTS project_team (
    id          BIGSERIAL PRIMARY KEY,
    project_id  UUID NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    user_id     UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    role        TEXT NOT NULL DEFAULT '',
    added_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_project_team_role ON project_team (project_id, user_id, role);
CREATE INDEX IF NOT EXISTS idx_project_team_user ON project_team (user_id);

-- Who works on an assembly: people of its project's team (or its lead), any number of them, one
-- of whom may lead the work on it. A person may work on several assemblies.
CREATE TABLE IF NOT EXISTS item_assignees (
    item_id   UUID NOT NULL REFERENCES items(id) ON DELETE CASCADE,
    user_id   UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    is_lead   BOOLEAN NOT NULL DEFAULT false,
    added_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (item_id, user_id)
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_item_assignees_lead ON item_assignees (item_id) WHERE is_lead;
CREATE INDEX IF NOT EXISTS idx_item_assignees_user ON item_assignees (user_id);

COMMIT;
