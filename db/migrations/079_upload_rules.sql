BEGIN;

-- Dozwolone typy przesyłanych plików (Ustawienia -> Magazyn plików, zob. UploadPolicy.cs).
-- Pusta tabela = każdy typ i rozmiar, jak dotąd. Wiersz = rozszerzenie (z kropką, małymi
-- literami) dozwolone z limitem w MB; NULL = bez limitu. Numer 079, bo 074-078 zajmują
-- otwarte pull requesty.
CREATE TABLE IF NOT EXISTS upload_rules (
    extension    TEXT PRIMARY KEY CHECK (extension ~ '^\.[a-z0-9][a-z0-9_-]{0,15}$'),
    max_size_mb  INTEGER CHECK (max_size_mb IS NULL OR max_size_mb > 0)
);

COMMIT;
