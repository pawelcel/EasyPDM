BEGIN;

-- Czy status "Wydany" może nadać tylko administrator.
--
-- Dotąd wydać element mógł każdy, kto ma do niego dostęp — także jego autor. W małym
-- zespole to wygodne, ale w firmie, gdzie konstruktor nie powinien sam zatwierdzać własnej
-- części, potrzebna jest druga para oczu. Ustawienie globalne, domyślnie wyłączone, więc po
-- aktualizacji nic się nie zmienia, dopóki administrator go nie włączy.
ALTER TABLE system_state
    ADD COLUMN IF NOT EXISTS release_admin_only BOOLEAN NOT NULL DEFAULT false;

INSERT INTO system_state (id) VALUES (true) ON CONFLICT (id) DO NOTHING;

COMMIT;
