BEGIN;

-- Minimalna liczba cyfr numeru elementu -- 0 (domyślnie) oznacza "bez dopełniania", czyli
-- dotychczasowe zachowanie: 1, 2, 3. Ustawienie 4 daje 0001, 0002... Numer powyżej tej
-- długości NIE jest przycinany (12345 przy ustawieniu 4 to dalej 12345) -- to minimalna
-- szerokość, nie format o stałej długości.
--
-- Trzymane w system_state (jednowierszowa tabela globalnych ustawień), a nie w
-- item_number_prefixes, bo to jedna wartość dla całej bazy, niezależna od rodzaju elementu.
--
-- W ODRÓŻNIENIU od prefiksu (zamrażanego na elemencie przy jego tworzeniu) dopełnienie jest
-- formatem WYŚWIETLANIA, liczonym przy każdym renderowaniu: zmiana tej wartości działa
-- wstecz na wszystkie elementy. Numer w bazie (items.item_number) pozostaje liczbą.
ALTER TABLE system_state
    ADD COLUMN IF NOT EXISTS item_number_digits INTEGER NOT NULL DEFAULT 0
        CHECK (item_number_digits BETWEEN 0 AND 10);

-- system_state bywa pusta aż do pierwszego zasiania przykładowego projektu, a ustawienie
-- musi dać się zapisać także przed tym momentem.
INSERT INTO system_state (id) VALUES (true) ON CONFLICT (id) DO NOTHING;

COMMIT;
