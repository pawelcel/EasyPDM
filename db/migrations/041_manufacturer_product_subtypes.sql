-- Migracja 041: podtypy w obrębie serii/typu producenta (np. seria "Łożyska walcowe" ->
-- podtypy "NU", "NJ", "NUP"). Drugi, opcjonalny poziom pod manufacturer_product_types --
-- element zakupowy może wskazać sam typ albo typ + podtyp, nigdy sam podtyp (stąd FK do
-- typu, a nie do producenta). Element trzyma tylko NAZWĘ w properties.productSubtype, tak
-- samo jak nazwę serii/typu i producenta.
-- Uruchom: psql -U pdm_user -d pdm -f 041_manufacturer_product_subtypes.sql

BEGIN;

CREATE TABLE IF NOT EXISTS manufacturer_product_subtypes (
    id              SERIAL PRIMARY KEY,
    product_type_id INTEGER NOT NULL REFERENCES manufacturer_product_types(id) ON DELETE CASCADE,
    name            TEXT NOT NULL,
    UNIQUE (product_type_id, name)
);

CREATE INDEX IF NOT EXISTS idx_manufacturer_product_subtypes_type
    ON manufacturer_product_subtypes (product_type_id);

GRANT SELECT, INSERT, UPDATE, DELETE ON manufacturer_product_subtypes TO pdm_user;
GRANT USAGE, SELECT ON SEQUENCE manufacturer_product_subtypes_id_seq TO pdm_user;

COMMIT;
