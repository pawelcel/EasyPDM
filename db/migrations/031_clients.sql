-- Migracja 031: katalog klientów (Nazwa/Nazwa 2/Lokalizacja + osoby kontaktowe, wzorem
-- manufacturers/manufacturer_contacts z migracji 017) oraz własna, prosta struktura
-- folderów/plików na dokumenty klienta (client_nodes -- CELOWO osobna od items/
-- item_relations, które dźwigają część/złożenie/status/rewizję/właściciela/BOM,
-- zupełnie nieistotne dla "folderu z dokumentami klienta"). Dokłada też powiązanie
-- Projekt -> Klient (projects.client_id) do wyszukiwarki po Nazwie/Nazwie 2 we
-- właściwościach projektu -- istniejąca kolumna projects.client TEXT zostaje bez zmian,
-- jako wartość historyczna (brak sensownego auto-dopasowania do nowych rekordów clients).
-- Uruchom: psql -h localhost -U pdm_user -d pdm -f db/migrations/031_clients.sql

BEGIN;

CREATE TABLE IF NOT EXISTS clients (
    id       SERIAL PRIMARY KEY,
    name     TEXT NOT NULL UNIQUE,
    name2    TEXT,
    location TEXT
);

CREATE TABLE IF NOT EXISTS client_contacts (
    id         SERIAL PRIMARY KEY,
    client_id  INTEGER NOT NULL REFERENCES clients(id) ON DELETE CASCADE,
    first_name TEXT,
    last_name  TEXT,
    phone      TEXT,
    position   TEXT,
    email      TEXT
);
CREATE INDEX IF NOT EXISTS idx_client_contacts_client ON client_contacts (client_id);

-- Jedna tabela z node_type CHECK ('folder'/'file'), analogicznie do items.item_type, ale
-- bez part/assembly/status/rewizji/właściciela/BOM -- tylko to, co faktycznie potrzebne.
CREATE TABLE IF NOT EXISTS client_nodes (
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
CREATE INDEX IF NOT EXISTS idx_client_nodes_client ON client_nodes (client_id);
CREATE INDEX IF NOT EXISTS idx_client_nodes_parent ON client_nodes (parent_id);

GRANT SELECT, INSERT, UPDATE, DELETE ON clients, client_contacts, client_nodes TO pdm_user;

ALTER TABLE projects ADD COLUMN IF NOT EXISTS client_id INTEGER REFERENCES clients(id) ON DELETE SET NULL;
CREATE INDEX IF NOT EXISTS idx_projects_client ON projects (client_id);

COMMIT;
