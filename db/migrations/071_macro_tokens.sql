BEGIN;

-- Tokeny makr CAD (FreeCAD, SolidWorks, Inventor): osobne od sesji przeglądarki, widoczne na
-- liście w Ustawienia -> Użytkownicy i odwoływalne pojedynczo. Dotąd makro logowało się jak
-- przeglądarka i trzymało zwykły token sesji (30 dni) w ustawieniach CAD na dysku — nie było jak
-- go zobaczyć ani odwołać bez zmiany hasła.
--
-- W bazie tylko skrót SHA-256 tokenu: wyciek kopii bazy nie daje gotowych tokenów. Token
-- wygasa po roku od wydania; zmiana hasła usuwa wszystkie tokeny makr użytkownika.
CREATE TABLE IF NOT EXISTS macro_tokens (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id       UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    token_hash    TEXT NOT NULL UNIQUE,
    client        TEXT NOT NULL,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    last_used_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    expires_at    TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS macro_tokens_user_idx ON macro_tokens (user_id);

COMMIT;
