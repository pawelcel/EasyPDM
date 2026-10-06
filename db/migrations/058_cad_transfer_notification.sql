BEGIN;

-- Raport z biegu makra CAD jako powiadomienie. Makro kończyło dotąd blokującym oknem
-- "wysłano element nr X" w CAD-zie -- a od czasu, gdy fokus po wysyłce wraca do przeglądarki,
-- to okno powstawało ZA nią i wisiało, czekając na kliknięcie, którego nikt nie widział
-- (zgłoszone z praktyki). Raport ma więc trafiać tam, gdzie człowiek i tak patrzy, i zostawać
-- do odszukania później, zamiast znikać razem z zamkniętym oknem.
--
-- JEDEN typ na wysyłkę i pobieranie (rozróżniane przez data->>'kind'), w odróżnieniu od
-- rozdzielenia zrobionego w 053: tam dwa zdarzenia niosły inną pilność i każde dało się
-- wyłączyć osobno, a tu obie strony to ten sam "raport z mojego makra" -- kto nie chce jednego,
-- nie chce i drugiego.
--
-- CHECK trzeba podmienić w całości -- Postgres nie umie dopisać wartości do istniejącego
-- ograniczenia, więc zrzucamy stare i zakładamy nowe z pełną listą.
ALTER TABLE notifications DROP CONSTRAINT IF EXISTS notifications_type_check;
ALTER TABLE notifications ADD CONSTRAINT notifications_type_check CHECK (type IN (
    'status_review', 'status_released', 'status_regressed', 'new_revision',
    'project_assigned', 'project_unassigned', 'project_deleted',
    'password_changed', 'low_disk_space', 'sample_project',
    'client_verification_needs_work', 'client_verification_verified',
    'cad_transfer_finished'
));

COMMIT;
