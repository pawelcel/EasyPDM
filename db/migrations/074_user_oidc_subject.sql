BEGIN;

-- Logowanie jednokrotne (OpenID Connect, zob. OidcSignIn.cs): użytkownik dostawcy jest wiązany
-- z kontem EasyPDM po identyfikatorze u dostawcy ("<wystawca>|<sub>"), nie po loginie — login
-- u dostawcy może się zmienić, a lokalne konto o tym samym loginie nie może zostać przejęte.
-- NULL = konto logujące się tylko hasłem.
ALTER TABLE users ADD COLUMN IF NOT EXISTS oidc_subject TEXT;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                   WHERE conname = 'users_oidc_subject_key' AND conrelid = 'users'::regclass) THEN
        ALTER TABLE users ADD CONSTRAINT users_oidc_subject_key UNIQUE (oidc_subject);
    END IF;
END $$;

COMMIT;
