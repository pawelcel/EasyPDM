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
    show_in_tree        BOOLEAN NOT NULL DEFAULT true,  -- dla elementów bez rodzica: czy pokazywać jako korzeń w drzewku
    status              TEXT CHECK (status IN ('w_pracy', 'sprawdzany', 'wydany')),  -- tylko dla part/assembly
    revision_number     INTEGER,                    -- tylko dla part/assembly, rośnie przy przejściu wydany -> w_pracy
    root_position       INTEGER NOT NULL DEFAULT 1,  -- kolejność wśród korzeni tego samego projektu (przeciąganie w drzewku)
    owner_id            UUID REFERENCES users(id) ON DELETE SET NULL,  -- właściciel part/assembly — patrz owner_locked
    owner_locked        BOOLEAN NOT NULL DEFAULT false,  -- gdy true, tylko owner_id może edytować (nawet admin nie omija)
    created_by          UUID REFERENCES users(id) ON DELETE SET NULL  -- kto utworzył element (niezmienne, w odróżnieniu od owner_id)
);

CREATE SEQUENCE item_number_seq START 1;
GRANT USAGE, SELECT ON SEQUENCE item_number_seq TO pdm_user;

CREATE INDEX idx_items_properties ON items USING GIN (properties);
CREATE INDEX idx_items_file_type ON items (file_type);
CREATE INDEX idx_items_project ON items (project_id);

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
    created_by      UUID REFERENCES users(id) ON DELETE SET NULL,
    PRIMARY KEY (item_id, revision_number)
);

-- ============================================================
-- Historia zmian statusu Części/Złożenia (kto/kiedy/z-na) — razem z created_by na items
-- i item_revision_comments daje pełną historię wyświetlaną na dole panelu właściwości.
-- ============================================================
CREATE TABLE item_status_history (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    item_id     UUID NOT NULL REFERENCES items(id) ON DELETE CASCADE,
    from_status TEXT,
    to_status   TEXT NOT NULL,
    changed_by  UUID REFERENCES users(id) ON DELETE SET NULL,
    changed_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_item_status_history_item ON item_status_history (item_id);
GRANT SELECT, INSERT ON item_status_history TO pdm_user;

-- Historia dodawania/usuwania załączników — osobna tabela, bo usunięty załącznik znika
-- z item_attachments, więc samo to nie wystarczy do zachowania śladu kto/kiedy go usunął.
CREATE TABLE item_attachment_history (
    id        UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    item_id   UUID NOT NULL REFERENCES items(id) ON DELETE CASCADE,
    file_name TEXT NOT NULL,
    action    TEXT NOT NULL CHECK (action IN ('added', 'removed')),
    user_id   UUID REFERENCES users(id) ON DELETE SET NULL,
    at        TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_item_attachment_history_item ON item_attachment_history (item_id);
GRANT SELECT, INSERT ON item_attachment_history TO pdm_user;

-- Historia blokady/zwolnienia właściciela — kto i kiedy zablokował (przejął na własność)
-- albo zwolnił element.
CREATE TABLE item_owner_history (
    id      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    item_id UUID NOT NULL REFERENCES items(id) ON DELETE CASCADE,
    action  TEXT NOT NULL CHECK (action IN ('locked', 'released')),
    user_id UUID REFERENCES users(id) ON DELETE SET NULL,
    at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_item_owner_history_item ON item_owner_history (item_id);
GRANT SELECT, INSERT ON item_owner_history TO pdm_user;

-- Harmonogram automatycznej kopii zapasowej (Ustawienia -> Magazyn plików). Jeden
-- wiersz-singleton wymuszony przez "id BOOLEAN PRIMARY KEY DEFAULT true CHECK (id)".
CREATE TABLE backup_schedule (
    id            BOOLEAN PRIMARY KEY DEFAULT true CHECK (id),
    enabled       BOOLEAN NOT NULL DEFAULT false,
    frequency     TEXT NOT NULL DEFAULT 'daily' CHECK (frequency IN ('daily', 'weekly', 'monthly')),
    -- 0 = niedziela .. 6 = sobota (System.DayOfWeek), używane tylko gdy frequency = 'weekly'.
    day_of_week   INT CHECK (day_of_week BETWEEN 0 AND 6),
    -- 1..31, używane tylko gdy frequency = 'monthly'; w krótszych miesiącach przycinane do
    -- ostatniego dnia miesiąca.
    day_of_month  INT CHECK (day_of_month BETWEEN 1 AND 31),
    hour          INT NOT NULL DEFAULT 2 CHECK (hour BETWEEN 0 AND 23),
    minute        INT NOT NULL DEFAULT 0 CHECK (minute BETWEEN 0 AND 59),
    last_run_at   TIMESTAMPTZ,
    -- Ile ostatnich automatycznych kopii trzymać w katalogu backups/ — starsze są kasowane.
    retention_count INT NOT NULL DEFAULT 14 CHECK (retention_count BETWEEN 1 AND 365)
);

INSERT INTO backup_schedule (id, day_of_week, day_of_month) VALUES (true, 0, 1);

GRANT SELECT, INSERT, UPDATE ON backup_schedule TO pdm_user;

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

-- ============================================================
-- Śledzenie zastosowanych migracji — od tej wersji PdmSystem.Api sam sprawdza tę tabelę
-- przy starcie i automatycznie stosuje nowe pliki z db/migrations/ (zob. MigrationRunner.cs).
-- Świeża instalacja (ten plik) jest już na bieżąco ze wszystkimi migracjami poniżej, więc
-- z góry oznaczamy je jako zastosowane — inaczej program przy pierwszym starcie próbowałby
-- wykonać je jeszcze raz.
-- ============================================================
CREATE TABLE schema_migrations (
    filename   TEXT PRIMARY KEY,
    applied_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

GRANT SELECT, INSERT ON schema_migrations TO pdm_user;

INSERT INTO schema_migrations (filename) VALUES
    ('002_add_projects.sql'), ('003_item_types.sql'), ('004_show_in_tree.sql'),
    ('005_assembly_type.sql'), ('006_item_status.sql'), ('007_materials.sql'),
    ('008_material_groups.sql'), ('009_item_attachments.sql'),
    ('010_item_relations_position.sql'), ('011_item_revision_comments.sql'),
    ('012_users_auth.sql'), ('013_project_properties.sql'),
    ('014_items_project_cascade.sql'), ('015_material_subgroup.sql'),
    ('016_root_position.sql'), ('017_manufacturers.sql'), ('018_saved_filters.sql'),
    ('019_project_users.sql'), ('020_item_owner.sql'),
    ('021_drop_dead_revision_schema.sql'), ('022_item_history.sql'),
    ('023_attachment_history.sql'), ('024_owner_lock_history.sql'),
    ('025_backup_schedule.sql'), ('026_backup_retention.sql'),
    ('027_schema_migrations_tracking.sql');
