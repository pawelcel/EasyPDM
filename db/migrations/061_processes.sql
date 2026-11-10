BEGIN;

-- Katalog obróbek (menu "Obróbka"): cięcie laserem, gięcie, spawanie, malowanie...
--
-- Każda obróbka mówi, czy dotyczy Części, Złożeń, czy obu -- spawanie bywa obróbką
-- złożenia, gięcie raczej części. Element wykonywany (Część "Wykonywana", Złożenie
-- "Wykonywane") wybiera z tego katalogu dowolną liczbę obróbek albo żadnej; zapisuje je
-- jako listę nazw we właściwościach (properties.processes), tak jak materiał jest zapisany
-- nazwą. Usunięcie albo zmiana nazwy obróbki w katalogu nie przepisuje elementów -- także
-- wydanych, których nikt nie powinien zmieniać po cichu.
--
-- Nazwa jest unikalna bez względu na wielkość liter: "Spawanie" i "spawanie" to ta sama
-- obróbka, inaczej lista do wyboru rozjechałaby się na dwie pozycje.
CREATE TABLE IF NOT EXISTS processes (
    id              SERIAL PRIMARY KEY,
    name            TEXT NOT NULL,
    description     TEXT,
    for_parts       BOOLEAN NOT NULL DEFAULT true,
    for_assemblies  BOOLEAN NOT NULL DEFAULT false,
    CHECK (for_parts OR for_assemblies)
);

CREATE UNIQUE INDEX IF NOT EXISTS processes_name_lower_idx ON processes (lower(name));

COMMIT;
