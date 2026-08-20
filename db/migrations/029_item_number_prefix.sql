-- Migracja 029: opcjonalny prefiks-litera przed numerem elementu, zależny od "rodzaju"
-- w momencie utworzenia elementu — konfigurowalny w Ustawieniach (Nazewnictwo).
-- Uruchom: psql -h localhost -U pdm_user -d pdm -f db/migrations/029_item_number_prefix.sql

BEGIN;

-- Mapowanie rodzaj -> prefiks. Klucz to string rodzaju (te same 4 wartości co
-- properties.rodzaj Części/Złożenia: Wykonywana/Zakupowa/Normalia/Klienta) — brak
-- wiersza dla danego rodzaju = brak prefiksu dla niego.
CREATE TABLE item_number_prefixes (
    rodzaj TEXT PRIMARY KEY,
    prefix TEXT NOT NULL CHECK (char_length(prefix) BETWEEN 1 AND 4)
);
GRANT SELECT, INSERT, UPDATE, DELETE ON item_number_prefixes TO pdm_user;

-- Zamrożony w momencie utworzenia elementu (na podstawie ówczesnego rodzaju i
-- ówczesnej konfiguracji item_number_prefixes) — NIGDY nie przeliczany retroaktywnie,
-- ani przy zmianie rodzaju elementu, ani przy zmianie mapowania w Ustawieniach.
-- NULL dla wszystkich już istniejących elementów (poprawne: nie zmieniamy przeszłości).
ALTER TABLE items ADD COLUMN IF NOT EXISTS item_number_prefix TEXT;

COMMIT;
