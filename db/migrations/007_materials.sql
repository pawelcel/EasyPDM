-- Migracja 007: katalog materiałów (do wyboru w Części) — ten sam kształt co istniejąca
-- tabela tags (SERIAL id + unikalna nazwa), zarządzany z panelu bocznego w UI.
-- Uruchom: psql -h localhost -U pdm_user -d pdm -f db/migrations/007_materials.sql

BEGIN;

CREATE TABLE IF NOT EXISTS materials (
    id   SERIAL PRIMARY KEY,
    name TEXT NOT NULL UNIQUE
);

-- Materiały już używane w istniejących Częściach (z wolnego tekstu) — żeby nic nie zniknęło
-- z widoku po przejściu na listę wyboru.
INSERT INTO materials (name)
SELECT DISTINCT properties->>'material'
FROM items
WHERE properties->>'material' IS NOT NULL AND properties->>'material' <> ''
ON CONFLICT (name) DO NOTHING;

COMMIT;
