BEGIN;

-- Podpisy w dokumentach (Ustawienia -> Język -> Podpisy w dokumentach): nagłówki kolumn eksportu
-- CSV, a w kolejnej wersji też pola tabelki rysunkowej, osobno dla każdego języka dokumentacji.
-- Wiersz istnieje tylko dla podpisu zmienionego przez administratora; brak wiersza = podpis
-- wbudowany (PL/EN/DE według skrótu języka, dla pozostałych angielski) — zob. DocumentLabels.cs.
-- Usunięcie języka usuwa jego podpisy.
CREATE TABLE IF NOT EXISTS document_labels (
    language_id  INTEGER NOT NULL REFERENCES project_languages(id) ON DELETE CASCADE,
    key          TEXT NOT NULL,
    text         TEXT NOT NULL CHECK (btrim(text) <> ''),
    PRIMARY KEY (language_id, key)
);

COMMIT;
