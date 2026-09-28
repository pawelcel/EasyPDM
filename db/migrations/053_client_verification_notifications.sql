BEGIN;

-- Powiadomienia o weryfikacji klienta. Dwa osobne typy zamiast jednego wspólnego, bo niosą
-- zupełnie inną pilność i każdy da się wyłączyć osobno w Ustawieniach:
--   client_verification_needs_work -- klient zgłosił uwagi, ktoś musi się tym zająć,
--   client_verification_verified   -- klient zaakceptował, nic nie trzeba robić.
-- Wpis "w trakcie weryfikacji" (bez wyniku) świadomie NIE powiadamia: to tylko odnotowanie,
-- że rzecz poszła do klienta, a nie zdarzenie wymagające czyjejś uwagi.
--
-- CHECK trzeba podmienić w całości -- Postgres nie umie dopisać wartości do istniejącego
-- ograniczenia, więc zrzucamy stare i zakładamy nowe z pełną listą.
ALTER TABLE notifications DROP CONSTRAINT IF EXISTS notifications_type_check;
ALTER TABLE notifications ADD CONSTRAINT notifications_type_check CHECK (type IN (
    'status_review', 'status_released', 'status_regressed', 'new_revision',
    'project_assigned', 'project_unassigned', 'project_deleted',
    'password_changed', 'low_disk_space', 'sample_project',
    'client_verification_needs_work', 'client_verification_verified'
));

COMMIT;
