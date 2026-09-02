-- Powiadomienia: zdarzenia dotyczące elementów/projektów/konta, adresowane do
-- konkretnego użytkownika, plus per-użytkownik wyłączenia poszczególnych typów.
-- Uruchom: psql -U pdm_user -d pdm -f 037_notifications.sql

BEGIN;

CREATE TABLE notifications (
    id         UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id    UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    type       TEXT NOT NULL CHECK (type IN (
        'status_review', 'status_released', 'status_regressed', 'new_revision',
        'project_assigned', 'project_unassigned', 'project_deleted',
        'password_changed', 'low_disk_space', 'sample_project'
    )),
    -- Dane do wyrenderowania treści PO STRONIE FRONTU (i18n, 3 języki) — ten sam
    -- wzorzec co HistoryEntry/ItemHistoryPanel: zapisujemy surowe dane (nazwy/numery
    -- w momencie zdarzenia, żeby przetrwały ewentualne późniejsze zmiany/usunięcia),
    -- front dobiera odpowiedni klucz tłumaczenia wg "type".
    data       JSONB NOT NULL DEFAULT '{}',
    -- Cele nawigacji "przejdź do" — ON DELETE SET NULL (nie CASCADE), żeby sama
    -- wiadomość i jej treść (data) przetrwały nawet po skasowaniu elementu/projektu;
    -- front po prostu nie pokazuje przycisku "przejdź", gdy pole jest NULL.
    item_id    UUID REFERENCES items(id) ON DELETE SET NULL,
    project_id UUID REFERENCES projects(id) ON DELETE SET NULL,
    read_at    TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_notifications_user ON notifications (user_id, created_at DESC);
GRANT SELECT, INSERT, UPDATE, DELETE ON notifications TO pdm_user;

-- Per-użytkownik wyłączenia (opt-out): brak wiersza = włączone (domyślnie wszystko
-- włączone bez potrzeby zasiewania wiersza dla każdego usera x każdy typ).
CREATE TABLE notification_preferences (
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    type    TEXT NOT NULL,
    enabled BOOLEAN NOT NULL DEFAULT true,
    PRIMARY KEY (user_id, type)
);
GRANT SELECT, INSERT, UPDATE, DELETE ON notification_preferences TO pdm_user;

COMMIT;
