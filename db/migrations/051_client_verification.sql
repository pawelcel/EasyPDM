BEGIN;

-- Weryfikacja klienta -- ślad akceptacji (albo uwag) klienta dla WYDANEJ Części/Złożenia,
-- prowadzony jako rosnąca lista wpisów: każdy ma wynik, opcjonalny komentarz i własne
-- załączniki (np. e-mail z potwierdzeniem).
--
-- Dlaczego project_id obok item_id: ta sama Część/Złożenie bywa współdzielona przez kilka
-- projektów (item_relations nie zna granic projektu), a weryfikuje ją KONKRETNY klient
-- konkretnego projektu -- akceptacja w jednym projekcie nie mówi nic o drugim. Dlatego
-- weryfikacja wisi na PARZE (element, projekt), a nie na samym elemencie, i dlatego nie
-- pokazuje się w "Całej bazie", gdzie element ogląda się bez kontekstu projektu.
--
-- revision_number: rewizja elementu w chwili wpisu (jak przy item_attachments) -- po powrocie
-- do "w pracy" i wydaniu nowej rewizji stare wpisy zostają, ale widać, że dotyczyły
-- POPRZEDNIEJ wersji i nowa czeka na własną weryfikację.
-- result NULL = wpis bez rozstrzygnięcia, czyli "w trakcie weryfikacji": rzecz poszła do
-- klienta i czekamy na odpowiedź. To pełnoprawny, świadomie wybierany stan (w oknie nie ma
-- domyślnie zaznaczonego wyniku), a nie brak danych — stąd kolumna dopuszcza NULL zamiast
-- trzeciej wartości w CHECK: "brak wyniku" to dosłownie brak wyniku.
CREATE TABLE IF NOT EXISTS item_client_verifications (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    item_id         UUID NOT NULL REFERENCES items(id) ON DELETE CASCADE,
    project_id      UUID NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    result          TEXT CHECK (result IN ('zweryfikowany', 'do_poprawy')),
    comment         TEXT,
    revision_number INTEGER,
    created_by      UUID REFERENCES users(id) ON DELETE SET NULL,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Indeks pod dwa realne odczyty: lista wpisów dla jednego elementu w jednym projekcie
-- (okno weryfikacji) i zbiorcze podsumowanie ostatnich wyników dla całego projektu
-- (znaczniki w drzewie) -- to drugie filtruje po samym project_id, stąd osobny indeks.
CREATE INDEX IF NOT EXISTS idx_item_client_verifications_item_project
    ON item_client_verifications (item_id, project_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_item_client_verifications_project
    ON item_client_verifications (project_id);

-- Osobna tabela zamiast kolumny z jednym plikiem -- jeden wpis potrafi nieść kilka dowodów
-- (np. e-mail klienta plus zrzut ekranu z uwagami).
CREATE TABLE IF NOT EXISTS item_client_verification_attachments (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    verification_id UUID NOT NULL REFERENCES item_client_verifications(id) ON DELETE CASCADE,
    file_name       TEXT NOT NULL,
    file_path       TEXT NOT NULL UNIQUE,
    file_size       BIGINT,
    uploaded_at     TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_item_client_verification_attachments_verification
    ON item_client_verification_attachments (verification_id);

GRANT SELECT, INSERT, UPDATE, DELETE ON item_client_verifications TO CURRENT_USER;
GRANT SELECT, INSERT, UPDATE, DELETE ON item_client_verification_attachments TO CURRENT_USER;

COMMIT;
