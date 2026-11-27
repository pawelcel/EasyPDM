BEGIN;

-- Progress reports on an assembly by the people working on it (item_assignees): a note and how
-- far along it is, in percent, one after another. The project's lead accepts an entry or sends
-- it back with remarks. The author can delete their latest entry only for a while after
-- writing it (system_state.progress_delete_minutes, Settings -> Users) and only until reviewed.
CREATE TABLE IF NOT EXISTS item_progress (
    id              BIGSERIAL PRIMARY KEY,
    item_id         UUID NOT NULL REFERENCES items(id) ON DELETE CASCADE,
    user_id         UUID REFERENCES users(id) ON DELETE SET NULL,
    note            TEXT NOT NULL,
    percent         INTEGER NOT NULL CHECK (percent BETWEEN 0 AND 100),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    review_status   TEXT CHECK (review_status IN ('accepted', 'remarks')),
    review_comment  TEXT,
    reviewed_by     UUID REFERENCES users(id) ON DELETE SET NULL,
    reviewed_at     TIMESTAMPTZ
);
CREATE INDEX IF NOT EXISTS idx_item_progress_item ON item_progress (item_id, created_at);

ALTER TABLE system_state ADD COLUMN IF NOT EXISTS progress_delete_minutes INTEGER NOT NULL DEFAULT 15
    CHECK (progress_delete_minutes BETWEEN 0 AND 10080);

-- The author is told when the lead reviews an entry.
ALTER TABLE notifications DROP CONSTRAINT IF EXISTS notifications_type_check;
ALTER TABLE notifications ADD CONSTRAINT notifications_type_check CHECK (type IN (
    'status_review', 'status_released', 'status_regressed', 'new_revision',
    'project_assigned', 'project_unassigned', 'project_deleted',
    'password_changed', 'low_disk_space', 'sample_project',
    'client_verification_needs_work', 'client_verification_verified',
    'cad_transfer_finished', 'assembly_assigned', 'progress_reviewed'
));

COMMIT;
