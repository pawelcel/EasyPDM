BEGIN;

-- Czy katalogi firmy (Materiały, Obróbki, Producenci, Klienci) może zmieniać tylko
-- administrator.
--
-- Dotąd dodawać, poprawiać i usuwać ich wpisy mógł każdy zalogowany. W małym zespole to
-- wygodne, ale katalogi są wspólne dla całej firmy: usunięty klient albo przemianowany materiał
-- dotyka wszystkich. Ustawienie globalne (Ustawienia -> Użytkownicy), domyślnie wyłączone, więc
-- po aktualizacji nic się nie zmienia, dopóki administrator go nie włączy. Odczyt i wybieranie
-- z katalogów w elementach zostają dla wszystkich.
ALTER TABLE system_state
    ADD COLUMN IF NOT EXISTS catalogs_admin_only BOOLEAN NOT NULL DEFAULT false;

INSERT INTO system_state (id) VALUES (true) ON CONFLICT (id) DO NOTHING;

COMMIT;
