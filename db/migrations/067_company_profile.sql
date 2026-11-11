BEGIN;

-- Dane firmy (Ustawienia -> Firma): nazwa i logo do tabelki rysunkowej. Jeden wiersz, jak
-- system_state. Logo trzymane w bazie, nie w magazynie plików: jest małe, a dzięki temu wchodzi
-- do każdego backupu (pg_dump) bez osobnej obsługi. Tylko PNG/JPEG -- pilnuje API po
-- sygnaturze pliku, bo SVG serwowany z tej samej domeny mógłby przemycić skrypt.
CREATE TABLE IF NOT EXISTS company_profile (
    id                 BOOLEAN PRIMARY KEY DEFAULT true CHECK (id),
    name               TEXT,
    logo               BYTEA,
    logo_content_type  TEXT,
    logo_updated_at    TIMESTAMPTZ
);

COMMIT;
