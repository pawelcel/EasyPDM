BEGIN;

-- Someone assigned to work on an assembly (item_assignees, migration 084) is told so. The CHECK is
-- replaced whole, as Postgres cannot add a value to an existing one.
ALTER TABLE notifications DROP CONSTRAINT IF EXISTS notifications_type_check;
ALTER TABLE notifications ADD CONSTRAINT notifications_type_check CHECK (type IN (
    'status_review', 'status_released', 'status_regressed', 'new_revision',
    'project_assigned', 'project_unassigned', 'project_deleted',
    'password_changed', 'low_disk_space', 'sample_project',
    'client_verification_needs_work', 'client_verification_verified',
    'cad_transfer_finished', 'assembly_assigned'
));

COMMIT;
