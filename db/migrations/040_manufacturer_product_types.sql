-- Migracja 040: typy produktów producenta -- wolna lista nazw per producent (np. "Łożyska",
-- "Silniki krokowe"), do wyboru przy elemencie ZAKUPOWYM (Część albo Złożenie) obok samego
-- producenta i do filtrowania w widoku "Cała baza". Świadomie osobna tabela, nie kolumna
-- tablicowa -- typ jest bytem katalogu producenta (dodawany/usuwany w zakładce Producenci),
-- a nie właściwością pojedynczego elementu; sam element trzyma tylko NAZWĘ wybranego typu
-- w properties.productType, tak samo jak trzyma nazwę producenta/materiału.
-- Uruchom: psql -U pdm_user -d pdm -f 040_manufacturer_product_types.sql

BEGIN;

CREATE TABLE IF NOT EXISTS manufacturer_product_types (
    id              SERIAL PRIMARY KEY,
    manufacturer_id INTEGER NOT NULL REFERENCES manufacturers(id) ON DELETE CASCADE,
    name            TEXT NOT NULL,
    UNIQUE (manufacturer_id, name)
);

CREATE INDEX IF NOT EXISTS idx_manufacturer_product_types_manufacturer
    ON manufacturer_product_types (manufacturer_id);

GRANT SELECT, INSERT, UPDATE, DELETE ON manufacturer_product_types TO pdm_user;
GRANT USAGE, SELECT ON SEQUENCE manufacturer_product_types_id_seq TO pdm_user;

COMMIT;
