-- Migracja 025: harmonogram automatycznej kopii zapasowej (Ustawienia -> Magazyn plików).
-- Jeden wiersz-singleton (wymuszony przez "id BOOLEAN PRIMARY KEY DEFAULT true CHECK (id)",
-- więc nigdy nie da się wstawić drugiego) — czytany/aktualizowany przez
-- GET/PATCH /api/settings/backup-schedule oraz przez tło (ScheduledBackupService), które co
-- jakiś czas sprawdza, czy nadszedł czas na kolejną automatyczną kopię.
-- Uruchom: psql -h localhost -U pdm_user -d pdm -f db/migrations/025_backup_schedule.sql

BEGIN;

CREATE TABLE backup_schedule (
    id            BOOLEAN PRIMARY KEY DEFAULT true CHECK (id),
    enabled       BOOLEAN NOT NULL DEFAULT false,
    frequency     TEXT NOT NULL DEFAULT 'daily' CHECK (frequency IN ('daily', 'weekly', 'monthly')),
    -- 0 = niedziela .. 6 = sobota (System.DayOfWeek), używane tylko gdy frequency = 'weekly'.
    day_of_week   INT CHECK (day_of_week BETWEEN 0 AND 6),
    -- 1..31, używane tylko gdy frequency = 'monthly'; w krótszych miesiącach przycinane do
    -- ostatniego dnia miesiąca (np. 31 w lutym uruchomi kopię 28/29 lutego).
    day_of_month  INT CHECK (day_of_month BETWEEN 1 AND 31),
    hour          INT NOT NULL DEFAULT 2 CHECK (hour BETWEEN 0 AND 23),
    minute        INT NOT NULL DEFAULT 0 CHECK (minute BETWEEN 0 AND 59),
    last_run_at   TIMESTAMPTZ
);

INSERT INTO backup_schedule (id, day_of_week, day_of_month) VALUES (true, 0, 1);

GRANT SELECT, INSERT, UPDATE ON backup_schedule TO pdm_user;

COMMIT;
