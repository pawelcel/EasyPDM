-- Schemat bazy danych systemu PDM
-- Uruchom jako: psql -U <user> -d <database> -f schema.sql

CREATE EXTENSION IF NOT EXISTS "pgcrypto"; -- dla gen_random_uuid()

-- ============================================================
-- Użytkownicy
-- ============================================================
CREATE TABLE users (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    username      TEXT NOT NULL UNIQUE,
    display_name  TEXT NOT NULL,
    email         TEXT,
    password_hash TEXT NOT NULL,
    role          TEXT NOT NULL DEFAULT 'user' CHECK (role IN ('admin', 'user'))
);

-- Sesje logowania (ciasteczko httpOnly "pdm_session" trzyma losowy token z tej tabeli).
CREATE TABLE sessions (
    token      TEXT PRIMARY KEY,
    user_id    UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    created_at TIMESTAMPTZ DEFAULT now(),
    expires_at TIMESTAMPTZ NOT NULL
);

CREATE INDEX idx_sessions_user ON sessions (user_id);

-- ============================================================
-- Projekty (kontener grupujący elementy)
-- ============================================================
CREATE TABLE projects (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name        TEXT NOT NULL UNIQUE,
    description TEXT,
    client      TEXT,
    start_date  DATE,
    end_date    DATE,
    created_at  TIMESTAMPTZ DEFAULT now()
);

-- Przypisania użytkowników do projektów — zwykły użytkownik ("user") widzi i może
-- przeglądać strukturę tylko przypisanych projektów; administrator zawsze widzi wszystko
-- (sprawdzane w kodzie aplikacji, nie tutaj).
CREATE TABLE project_users (
    project_id UUID NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    user_id    UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    PRIMARY KEY (project_id, user_id)
);

GRANT SELECT, INSERT, DELETE ON project_users TO pdm_user;

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
    show_in_tree        BOOLEAN NOT NULL DEFAULT true,  -- dla elementów bez rodzica: czy pokazywać jako korzeń w drzewku
    status              TEXT CHECK (status IN ('w_pracy', 'sprawdzany', 'wydany')),  -- tylko dla part/assembly
    revision_number     INTEGER,                    -- tylko dla part/assembly, rośnie przy przejściu wydany -> w_pracy
    root_position       INTEGER NOT NULL DEFAULT 1,  -- kolejność wśród korzeni tego samego projektu (przeciąganie w drzewku)
    owner_id            UUID REFERENCES users(id) ON DELETE SET NULL,  -- właściciel part/assembly — patrz owner_locked
    owner_locked        BOOLEAN NOT NULL DEFAULT false  -- gdy true, tylko owner_id może edytować (nawet admin nie omija)
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

-- ============================================================
-- Materiały (katalog do wyboru w Części, zarządzany z panelu bocznego)
-- ============================================================
CREATE TABLE materials (
    id            SERIAL PRIMARY KEY,
    name          TEXT NOT NULL UNIQUE,
    group_name    TEXT,  -- czysto porządkowe/filtrujące, nigdy nie trafia do właściwości Części
    subgroup_name TEXT   -- jw., podrzędne wobec grupy
);

-- ============================================================
-- Producenci (katalog z osobami kontaktowymi, zarządzany z panelu bocznego)
-- ============================================================
CREATE TABLE manufacturers (
    id   SERIAL PRIMARY KEY,
    name TEXT NOT NULL UNIQUE
);

CREATE TABLE manufacturer_contacts (
    id              SERIAL PRIMARY KEY,
    manufacturer_id INTEGER NOT NULL REFERENCES manufacturers(id) ON DELETE CASCADE,
    first_name      TEXT,
    last_name       TEXT,
    phone           TEXT,
    position        TEXT,
    email           TEXT
);

CREATE INDEX idx_manufacturer_contacts_manufacturer ON manufacturer_contacts (manufacturer_id);

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
    position  INTEGER NOT NULL DEFAULT 1,  -- L.p. w BOM rodzica — ręcznie edytowalne/przeciągalne w UI
    PRIMARY KEY (parent_id, child_id)
);

-- ============================================================
-- Załączniki (pliki dograne "z zewnątrz" do Części/Złożenia/Pliku) — w odróżnieniu
-- od item_relations NIE są osobnym elementem w drzewku, zarządzane tylko z panelu
-- właściwości po prawej stronie.
-- ============================================================
CREATE TABLE item_attachments (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    item_id     UUID NOT NULL REFERENCES items(id) ON DELETE CASCADE,
    file_name   TEXT NOT NULL,
    file_path   TEXT NOT NULL UNIQUE,
    file_hash   TEXT,
    file_size   BIGINT,
    uploaded_at TIMESTAMPTZ DEFAULT now()
);

CREATE INDEX idx_item_attachments_item ON item_attachments (item_id);

-- ============================================================
-- Komentarze do rewizji Części/Złożenia (opcjonalne) — tworzone przy zmianie statusu
-- Wydany -> W pracy, zarówno w aplikacji webowej, jak i przez makro FreeCAD.
-- ============================================================
CREATE TABLE item_revision_comments (
    item_id         UUID NOT NULL REFERENCES items(id) ON DELETE CASCADE,
    revision_number INTEGER NOT NULL,
    comment         TEXT NOT NULL,
    created_at      TIMESTAMPTZ DEFAULT now(),
    PRIMARY KEY (item_id, revision_number)
);

-- ============================================================
-- Zapisane filtry widoku "Cała baza" — każdy użytkownik zapisuje własne zestawy filtrów
-- (wyszukiwanie, tag, typ rekordu, rodzaj części, producent) pod wybraną nazwą; zapis pod
-- istniejącą nazwą nadpisuje poprzedni (UNIQUE (user_id, name) + upsert w zapytaniu aplikacji).
-- ============================================================
CREATE TABLE saved_filters (
    id         UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id    UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    name       TEXT NOT NULL,
    filters    JSONB NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (user_id, name)
);

GRANT SELECT, INSERT, UPDATE, DELETE ON saved_filters TO pdm_user;
