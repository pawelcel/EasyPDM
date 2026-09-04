-- Migracja 043: Nazwa 2 klienta jako osobna tabela zamiast kolumny 1:1 na clients -- jeden
-- klient (np. "Bosch") może mieć KILKA drugich nazw/wariantów handlowych, zamiast zmuszać
-- do zakładania osobnego wpisu klienta (z lekko zmienioną nazwą główną, żeby ominąć UNIQUE
-- na clients.name) dla każdej dodatkowej nazwy 2 -- dokładnie ten sam problem, który
-- manufacturer_product_types/subtypes rozwiązały dla typów produktu producenta. Element
-- (Część/Złożenie) trzyma tylko NAZWĘ wybranej nazwy 2 w properties.clientName2, tak samo
-- jak trzyma nazwę klienta -- bez klucza obcego.
-- Uruchom: psql -U pdm_user -d pdm -f 043_client_name2.sql

BEGIN;

CREATE TABLE IF NOT EXISTS client_name2 (
    id        SERIAL PRIMARY KEY,
    client_id INTEGER NOT NULL REFERENCES clients(id) ON DELETE CASCADE,
    name2     TEXT NOT NULL,
    UNIQUE (client_id, name2)
);

CREATE INDEX IF NOT EXISTS idx_client_name2_client ON client_name2 (client_id);

GRANT SELECT, INSERT, UPDATE, DELETE ON client_name2 TO pdm_user;
GRANT USAGE, SELECT ON SEQUENCE client_name2_id_seq TO pdm_user;

-- Przenieś istniejące wartości clients.name2 (dotychczas co najwyżej jedna per klient) do
-- nowej tabeli, zanim kolumna zniknie -- nic z tego, co ktoś już wpisał, nie ginie.
INSERT INTO client_name2 (client_id, name2)
SELECT id, name2 FROM clients WHERE name2 IS NOT NULL AND name2 <> '';

ALTER TABLE clients DROP COLUMN IF EXISTS name2;

COMMIT;
