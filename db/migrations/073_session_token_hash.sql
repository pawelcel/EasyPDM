BEGIN;

-- Sesje przeglądarki trzymają w bazie tylko skrót SHA-256 tokenu, jak tokeny makr (071). Dotąd
-- kolumna sessions.token zawierała gotowy token z ciasteczka, więc sam odczyt tej tabeli (kopia
-- zapasowa, dostęp tylko do odczytu, zrzut przy awarii) dawał działające sesje każdego użytkownika.
--
-- Istniejące sesje są przeliczane na miejscu, więc nikt nie zostaje wylogowany. Wielkie litery hex
-- odpowiadają Convert.ToHexString w AuthEndpoints.HashToken — inne kodowanie wylogowałoby
-- wszystkich. Zmiana nazwy kolumny na token_hash mówi, że przeliczenie już się odbyło: migracja
-- uruchomiona drugi raz nie liczy skrótu ze skrótu.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = current_schema() AND table_name = 'sessions' AND column_name = 'token'
    ) THEN
        UPDATE sessions SET token = upper(encode(sha256(convert_to(token, 'UTF8')), 'hex'));
        ALTER TABLE sessions RENAME COLUMN token TO token_hash;
    END IF;
END $$;

COMMIT;
