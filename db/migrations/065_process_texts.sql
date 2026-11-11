BEGIN;

-- Nazwa i opis obróbki w każdym języku dokumentacji (Ustawienia -> Język). Katalog pokazuje
-- wersję w domyślnym języku dokumentacji; po jego zmianie cały katalog przełącza się na nowy
-- język, a obróbka bez tłumaczenia pokazuje tekst z innego języka (oznaczony jego skrótem).
-- Okno tłumaczeń obróbki uzupełnia pozostałe języki.
--
-- Dotychczasowe processes.name/description trafiają do języka, który jest domyślny w chwili
-- migracji -- nie da się zgadnąć, w jakim języku je wpisano. Jeśli to inny język, przenosi się
-- je w oknie tłumaczeń.
--
-- Usunięcie języka usuwa jego teksty (CASCADE); API nie pozwala jednak usunąć języka, w którym
-- jakaś obróbka ma swój jedyny tekst, żeby nie została bez nazwy.
--
-- Świeża instalacja ma już w db/schema.sql stan po tej migracji (bez processes.name), więc
-- schema.sql oznacza ją -- razem z 061/062, które zakładały kolumnę name -- jako zastosowaną.
CREATE TABLE IF NOT EXISTS process_texts (
    process_id   INTEGER NOT NULL REFERENCES processes(id) ON DELETE CASCADE,
    language_id  INTEGER NOT NULL REFERENCES project_languages(id) ON DELETE CASCADE,
    name         TEXT NOT NULL CHECK (btrim(name) <> ''),
    description  TEXT,
    PRIMARY KEY (process_id, language_id)
);

INSERT INTO process_texts (process_id, language_id, name, description)
SELECT id, default_project_language_id(), name, description FROM processes
ON CONFLICT DO NOTHING;

ALTER TABLE processes DROP COLUMN IF EXISTS name;
ALTER TABLE processes DROP COLUMN IF EXISTS description;

COMMIT;
