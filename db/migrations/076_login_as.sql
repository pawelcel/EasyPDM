-- Log in as (LoginAsEndpoints.cs): an administrator works as another user in the same session.
-- The session row changes its user to that person and impersonator_id keeps the administrator,
-- so no second token exists and logging out ends both. Deleting either account ends the session.
--
-- login_as_log is the record of every switch: start, every request that may change something
-- made while switched (change: method, path and answer), and end (back, signed_out or expired).
-- The ids have no foreign keys and the names are copies, so the record outlives both accounts.
-- Safe to run twice: schema.sql already holds the same.
ALTER TABLE sessions ADD COLUMN IF NOT EXISTS impersonator_id UUID REFERENCES users(id) ON DELETE CASCADE;

CREATE TABLE IF NOT EXISTS login_as_log (
    id             BIGSERIAL PRIMARY KEY,
    at             TIMESTAMPTZ NOT NULL DEFAULT now(),
    kind           TEXT NOT NULL CHECK (kind IN ('start', 'change', 'end')),
    admin_id       UUID NOT NULL,
    admin_name     TEXT NOT NULL,
    target_id      UUID NOT NULL,
    target_name    TEXT NOT NULL,
    detail         TEXT,
    client_address TEXT
);
CREATE INDEX IF NOT EXISTS idx_login_as_log_at ON login_as_log (at DESC, id DESC);
GRANT SELECT, INSERT ON login_as_log TO CURRENT_USER;
GRANT USAGE, SELECT ON SEQUENCE login_as_log_id_seq TO CURRENT_USER;
