BEGIN;

-- Języki projektu (Ustawienia -> Język -> Języki projektu): lista języków, w których firma
-- prowadzi dokumentację -- niezależna od języka interfejsu, który każdy wybiera dla siebie.
-- Na niej oprze się wielojęzyczność danych elementów, z których makro uzupełni rysunek.
--
-- Skrót to 1-3 wielkie litery łacińskie (PL, EN, DE, CZE): trafi do nazw pól i kolumn, więc
-- musi być krótki, jednoznaczny i bez znaków, które coś by rozjechały. Zarówno skrót, jak i
-- nazwa są unikalne -- nazwa bez względu na wielkość liter, żeby "Polski" i "polski" nie
-- były dwoma językami.
--
-- Dokładnie jeden język jest domyślnym językiem dokumentacji (is_default): częściowy indeks
-- unikalny nie pozwala na dwa, a API nie pozwala usunąć domyślnego, więc lista nigdy nie
-- zostaje bez niego. Start: English i Polish, domyślny English.
--
-- Schemat świeżej instalacji (db/schema.sql) ma już tę tabelę i te wiersze, a ta migracja
-- i tak się na nim wykona -- stąd IF NOT EXISTS i ON CONFLICT.
CREATE TABLE IF NOT EXISTS project_languages (
    id          SERIAL PRIMARY KEY,
    name        TEXT NOT NULL,
    code        TEXT NOT NULL UNIQUE CHECK (code ~ '^[A-Z]{1,3}$'),
    is_default  BOOLEAN NOT NULL DEFAULT false
);

CREATE UNIQUE INDEX IF NOT EXISTS project_languages_name_lower_idx ON project_languages (lower(name));
CREATE UNIQUE INDEX IF NOT EXISTS project_languages_one_default_idx ON project_languages (is_default) WHERE is_default;

INSERT INTO project_languages (name, code) VALUES ('English', 'EN'), ('Polish', 'PL')
ON CONFLICT DO NOTHING;

UPDATE project_languages SET is_default = true
WHERE code = 'EN' AND NOT EXISTS (SELECT 1 FROM project_languages WHERE is_default);

COMMIT;
