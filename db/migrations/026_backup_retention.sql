-- Migracja 026: liczba przechowywanych automatycznych kopii zapasowych, konfigurowalna
-- w Ustawieniach (wcześniej sztywno zakodowana jako 14 w ScheduledBackupService).
-- Uruchom: psql -h localhost -U pdm_user -d pdm -f db/migrations/026_backup_retention.sql

BEGIN;

ALTER TABLE backup_schedule
    ADD COLUMN retention_count INT NOT NULL DEFAULT 14 CHECK (retention_count BETWEEN 1 AND 365);

COMMIT;
