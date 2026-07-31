-- Schemat bazy danych systemu PDM
-- Uruchom jako: psql -U <user> -d <database> -f schema.sql

CREATE EXTENSION IF NOT EXISTS "pgcrypto"; -- dla gen_random_uuid()

-- ============================================================
-- Użytkownicy
-- ============================================================
CREATE TABLE users (
    id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    username     TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL,
    email        TEXT
);

-- ============================================================
-- Projekty (kontener grupujący elementy)
-- ============================================================
CREATE TABLE projects (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name        TEXT NOT NULL UNIQUE,
    description TEXT,
    created_at  TIMESTAMPTZ DEFAULT now()
);

-- ============================================================
-- Elementy (pliki CAD i powiązane dokumenty)
-- ============================================================
CREATE TABLE items (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id          UUID NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    item_type           TEXT NOT NULL DEFAULT 'file'
                            CHECK (item_type IN ('folder', 'part', 'file', 'assembly')),
    item_number         INTEGER,                -- numer nadawany automatycznie Częściom i Złożeniom (item_number_seq)
    file_path           TEXT UNIQUE,             -- ścieżka w wewnętrznym magazynie API; puste dla folderów/Części (to kontenery bez własnego pliku)
    file_name           TEXT NOT NULL,           -- nazwa pliku (dla typu 'file') albo nazwa folderu/Części
    file_type           TEXT,                    -- sldprt, sldasm, slddrw, step, dxf... — puste dla folderów/Części
    file_hash           TEXT,
    file_size           BIGINT,
    created_at          TIMESTAMPTZ DEFAULT now(),
    modified_at         TIMESTAMPTZ,
    last_scanned_at     TIMESTAMPTZ,            -- zostaje na przyszłość: wypełni to skaner/wtyczka CAD, nie wypełniane przy ręcznym dodaniu
    properties          JSONB DEFAULT '{}',
    current_revision_id UUID,                   -- FK dodane niżej (cykliczna referencja)
    checked_out_by      UUID REFERENCES users(id),
    checked_out_at      TIMESTAMPTZ,
    show_in_tree        BOOLEAN NOT NULL DEFAULT true  -- dla elementów bez rodzica: czy pokazywać jako korzeń w drzewku
);

CREATE SEQUENCE item_number_seq START 1;
GRANT USAGE, SELECT ON SEQUENCE item_number_seq TO pdm_user;

CREATE INDEX idx_items_properties ON items USING GIN (properties);
CREATE INDEX idx_items_file_type ON items (file_type);
CREATE INDEX idx_items_project ON items (project_id);

-- ============================================================
-- Rewizje
-- ============================================================
CREATE TYPE item_status AS ENUM ('w_pracy', 'w_rewizji', 'zwolniony');

CREATE TABLE item_revisions (
    id             UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    item_id        UUID NOT NULL REFERENCES items(id) ON DELETE CASCADE,
    revision_label TEXT NOT NULL,
    status         item_status NOT NULL DEFAULT 'w_pracy',
    file_hash      TEXT,
    snapshot_path  TEXT,
    comment        TEXT,
    created_by     UUID REFERENCES users(id),
    created_at     TIMESTAMPTZ DEFAULT now(),
    UNIQUE (item_id, revision_label)
);

ALTER TABLE items
    ADD CONSTRAINT fk_items_current_revision
    FOREIGN KEY (current_revision_id) REFERENCES item_revisions(id);

-- ============================================================
-- Historia checkout/checkin (audyt)
-- ============================================================
CREATE TABLE checkout_history (
    id                     UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    item_id                UUID NOT NULL REFERENCES items(id) ON DELETE CASCADE,
    user_id                UUID NOT NULL REFERENCES users(id),
    checked_out_at         TIMESTAMPTZ NOT NULL,
    checked_in_at          TIMESTAMPTZ,
    resulting_revision_id  UUID REFERENCES item_revisions(id)
);

-- ============================================================
-- Definicje właściwości (opcjonalne, dla porządku/walidacji)
-- ============================================================
CREATE TABLE property_definitions (
    key          TEXT PRIMARY KEY,
    display_name TEXT NOT NULL,
    data_type    TEXT NOT NULL,   -- text, number, enum, date
    enum_values  TEXT[]
);

-- Kilka przykładowych definicji na start
INSERT INTO property_definitions (key, display_name, data_type, enum_values) VALUES
    ('material', 'Materiał', 'enum', ARRAY['Stal S235', 'Stal nierdzewna 304', 'Aluminium 6061', 'Tworzywo POM']),
    ('mass', 'Masa [kg]', 'number', NULL),
    ('supplier', 'Dostawca', 'text', NULL),
    ('rodzaj', 'Rodzaj', 'enum', ARRAY['Zakupowa', 'Wykonywana']);

-- ============================================================
-- Tagi
-- ============================================================
CREATE TABLE tags (
    id   SERIAL PRIMARY KEY,
    name TEXT NOT NULL UNIQUE
);

CREATE TABLE item_tags (
    item_id UUID REFERENCES items(id) ON DELETE CASCADE,
    tag_id  INT REFERENCES tags(id) ON DELETE CASCADE,
    PRIMARY KEY (item_id, tag_id)
);

-- ============================================================
-- Relacje między elementami (BOM/złożenia — pod przyszłą Fazę 3)
-- ============================================================
CREATE TABLE item_relations (
    parent_id UUID REFERENCES items(id) ON DELETE CASCADE,
    child_id  UUID REFERENCES items(id) ON DELETE CASCADE,
    quantity  NUMERIC DEFAULT 1,
    PRIMARY KEY (parent_id, child_id)
);
