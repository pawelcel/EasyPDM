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
GRANT SELECT, INSERT, UPDATE, DELETE ON sessions TO pdm_user;

-- ============================================================
-- Projekty (kontener grupujący elementy)
-- ============================================================
CREATE TABLE projects (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name        TEXT NOT NULL UNIQUE,
    description TEXT,
    client      TEXT,  -- wolny tekst, historyczny -- nowe projekty łączy się z klientem przez client_id (patrz sekcja Klienci, kolumna dołożona niżej przez ALTER TABLE, bo clients jest zdefiniowane dalej w pliku)
    start_date  DATE,
    end_date    DATE,
    created_at  TIMESTAMPTZ DEFAULT now(),
    is_sample   BOOLEAN NOT NULL DEFAULT false  -- projekt startowy zasiany przez EnsureSampleProjectAsync (Program.cs)
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
    project_id          UUID REFERENCES projects(id) ON DELETE CASCADE, -- NULL = element bez projektu, widoczny tylko przez "Cała baza"
    item_type           TEXT NOT NULL DEFAULT 'file'
                            CHECK (item_type IN ('folder', 'part', 'file', 'assembly')),
    item_number         INTEGER,                -- numer nadawany automatycznie Częściom i Złożeniom (item_number_seq)
    item_number_prefix  TEXT,                    -- opcjonalny prefiks-litera, zamrożony przy tworzeniu wg rodzaju + item_number_prefixes
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

-- ============================================================
-- Mapowanie rodzaj -> prefiks numeru (Ustawienia -> Nazewnictwo). Brak wiersza dla
-- danego rodzaju = brak prefiksu. Odczytywane WYŁĄCZNIE w momencie tworzenia elementu
-- (zob. item_number_prefix w items) — zmiana tu nie wpływa na już istniejące elementy.
-- ============================================================
CREATE TABLE item_number_prefixes (
    rodzaj TEXT PRIMARY KEY,
    prefix TEXT NOT NULL CHECK (char_length(prefix) BETWEEN 1 AND 4)
);
GRANT SELECT, INSERT, UPDATE, DELETE ON item_number_prefixes TO pdm_user;

CREATE TABLE manufacturer_contacts (
    id              SERIAL PRIMARY KEY,
    manufacturer_id INTEGER NOT NULL REFERENCES manufacturers(id) ON DELETE CASCADE,
    first_name      TEXT,
    last_name       TEXT,
    phone           TEXT,
    position        TEXT,
    email           TEXT,
    address         TEXT
);

CREATE INDEX idx_manufacturer_contacts_manufacturer ON manufacturer_contacts (manufacturer_id);

-- ============================================================
-- Klienci (katalog z osobami kontaktowymi i własną strukturą plików, zarządzany z panelu
-- bocznego) -- celowo izolowane od items/item_relations, patrz komentarz przy client_nodes.
-- ============================================================
CREATE TABLE clients (
    id       SERIAL PRIMARY KEY,
    name     TEXT NOT NULL UNIQUE,
    name2    TEXT,
    location TEXT
);

CREATE TABLE client_contacts (
    id         SERIAL PRIMARY KEY,
    client_id  INTEGER NOT NULL REFERENCES clients(id) ON DELETE CASCADE,
    first_name TEXT,
    last_name  TEXT,
    phone      TEXT,
    position   TEXT,
    email      TEXT,
    address    TEXT
);
CREATE INDEX idx_client_contacts_client ON client_contacts (client_id);

-- Jedna tabela z node_type CHECK ('folder'/'file'), analogicznie do items.item_type, ale
-- bez part/assembly/status/rewizji/właściciela/BOM -- tylko to, co faktycznie potrzebne
-- dla prostego drzewka dokumentów klienta (np. norm).
CREATE TABLE client_nodes (
    id         UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    client_id  INTEGER NOT NULL REFERENCES clients(id) ON DELETE CASCADE,
    parent_id  UUID REFERENCES client_nodes(id) ON DELETE CASCADE,
    node_type  TEXT NOT NULL CHECK (node_type IN ('folder', 'file')),
    name       TEXT NOT NULL,
    file_path  TEXT UNIQUE,   -- NULL dla folderów
    file_size  BIGINT,
    file_hash  TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CHECK (node_type = 'folder' OR file_path IS NOT NULL)
);
CREATE INDEX idx_client_nodes_client ON client_nodes (client_id);
CREATE INDEX idx_client_nodes_parent ON client_nodes (parent_id);

GRANT SELECT, INSERT, UPDATE, DELETE ON clients, client_contacts, client_nodes TO pdm_user;

-- projects.client_id dokłada się tutaj (ALTER, nie inline w CREATE TABLE projects wyżej),
-- bo clients musi już istnieć, żeby FK zadziałało -- projects jest zdefiniowane wcześniej
-- w tym pliku.
ALTER TABLE projects ADD COLUMN client_id INTEGER REFERENCES clients(id) ON DELETE SET NULL;
CREATE INDEX idx_projects_client ON projects (client_id);

-- ============================================================
-- Powiadomienia -- zdarzenia dotyczące elementów/projektów/konta, adresowane do
-- konkretnego użytkownika, plus per-użytkownik wyłączenia poszczególnych typów.
-- ============================================================
CREATE TABLE notifications (
    id         UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id    UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    type       TEXT NOT NULL CHECK (type IN (
        'status_review', 'status_released', 'status_regressed', 'new_revision',
        'project_assigned', 'project_unassigned', 'project_deleted',
        'password_changed', 'low_disk_space', 'sample_project'
    )),
    -- Dane do wyrenderowania treści PO STRONIE FRONTU (i18n, 3 języki) -- ten sam
    -- wzorzec co HistoryEntry/ItemHistoryPanel: zapisujemy surowe dane (nazwy/numery
    -- w momencie zdarzenia, żeby przetrwały ewentualne późniejsze zmiany/usunięcia),
    -- front dobiera odpowiedni klucz tłumaczenia wg "type".
    data       JSONB NOT NULL DEFAULT '{}',
    -- Cele nawigacji "przejdź do" -- ON DELETE SET NULL (nie CASCADE), żeby sama
    -- wiadomość i jej treść (data) przetrwały nawet po skasowaniu elementu/projektu;
    -- front po prostu nie pokazuje przycisku "przejdź", gdy pole jest NULL.
    item_id    UUID REFERENCES items(id) ON DELETE SET NULL,
    project_id UUID REFERENCES projects(id) ON DELETE SET NULL,
    read_at    TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_notifications_user ON notifications (user_id, created_at DESC);
GRANT SELECT, INSERT, UPDATE, DELETE ON notifications TO pdm_user;

-- Per-użytkownik wyłączenia (opt-out): brak wiersza = włączone (domyślnie wszystko
-- włączone bez potrzeby zasiewania wiersza dla każdego usera x każdy typ).
CREATE TABLE notification_preferences (
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    type    TEXT NOT NULL,
    enabled BOOLEAN NOT NULL DEFAULT true,
    PRIMARY KEY (user_id, type)
);
GRANT SELECT, INSERT, UPDATE, DELETE ON notification_preferences TO pdm_user;

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
    PRIMARY KEY (parent_id, child_id),
    -- DEFERRABLE INITIALLY DEFERRED (sprawdzane dopiero przy COMMIT) — .../children/reorder
    -- przenumerowuje całe zestawienie 1..N w jednej transakcji, więc bez odroczenia
    -- tymczasowe, przejściowe kolizje numerów w trakcie tej pętli przerywałyby transakcję.
    CONSTRAINT item_relations_parent_position_unique UNIQUE (parent_id, position) DEFERRABLE INITIALLY DEFERRED
);

-- ============================================================
-- Załączniki (pliki dograne "z zewnątrz" do Części/Złożenia/Pliku) — w odróżnieniu
-- od item_relations NIE są osobnym elementem w drzewku, zarządzane tylko z panelu
-- właściwości po prawej stronie.
-- ============================================================
CREATE TABLE item_attachments (
    id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    item_id      UUID NOT NULL REFERENCES items(id) ON DELETE CASCADE,
    file_name    TEXT NOT NULL,
    file_path    TEXT NOT NULL UNIQUE,
    file_hash    TEXT,
    file_size    BIGINT,
    uploaded_at  TIMESTAMPTZ DEFAULT now(),
    -- Opcjonalna "rola" załącznika:
    --   pdf/step — wskazuje, KTÓRY załącznik zasila stały podgląd (2D/3D) w panelu
    --     właściwości elementu (jeden na rolę, nowy zastępuje poprzedni).
    --   cad — oryginalny plik CAD (SLDPRT/SLDASM/FCStd) wgrany przez makro
    --     SolidWorks/FreeCAD, odróżniony od zwykłych, ręcznie dodanych załączników.
    --     WIELE na element dozwolone (jeden na rewizję, nazwy zawierają literę rewizji,
    --     więc kolejne przesłania się nie nadpisują — historia rewizji zostaje widoczna).
    preview_role TEXT CHECK (preview_role IN ('pdf', 'step', 'cad'))
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

-- Trwały znacznik stanu serwera, którego celowo NIE dotyka "Wyczyść bazę" (czyści tylko
-- projects/materials/manufacturers/clients) -- bez tego, po ręcznym wyczyszczeniu Projektów
-- na już zasiedlonej instancji, kolejny restart procesu widziałby pustą tabelę projects i
-- ponownie zasiewał przykładowy projekt startowy (EnsureSampleProjectAsync w Program.cs).
CREATE TABLE system_state (
    id                     BOOLEAN PRIMARY KEY DEFAULT true CHECK (id),
    sample_project_seeded  BOOLEAN NOT NULL DEFAULT false
);
GRANT SELECT, INSERT, UPDATE ON system_state TO pdm_user;

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
-- Śledzenie zastosowanych migracji — od tej wersji EasyPDM.Api sam sprawdza tę tabelę
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
    ('027_schema_migrations_tracking.sql'), ('028_attachment_preview_role.sql'),
    ('029_item_number_prefix.sql'), ('030_attachment_cad_role.sql'),
    ('031_clients.sql'), ('032_nullable_item_project.sql'),
    ('033_manufacturer_contact_address.sql'), ('034_client_contact_address.sql'),
    ('035_item_relations_position_default.sql'), ('036_item_relations_position_unique.sql'),
    ('037_notifications.sql'), ('038_project_is_sample.sql'), ('039_system_state.sql');
