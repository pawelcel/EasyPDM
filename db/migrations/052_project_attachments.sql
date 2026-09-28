BEGIN;

-- Załączniki PROJEKTU -- dokumenty dotyczące całego zlecenia, nie pojedynczej Części:
-- oferta, potwierdzenie przyjęcia zlecenia i wszystko inne, co przychodzi "do projektu"
-- (korespondencja, ustalenia, specyfikacje klienta).
--
-- role:
--   'oferta'   -- oferta wysłana klientowi,
--   'zlecenie' -- dokument potwierdzający otrzymanie zlecenia,
--   NULL       -- zwykły załącznik, bez wyróżnionego miejsca w panelu.
-- Wyróżnione role dopuszczają WIELE plików (jak 'cad' przy elementach, nie jak 'pdf'/'step'):
-- oferta bywa poprawiana i wysyłana ponownie, a nowa wersja nie powinna kasować śladu po
-- poprzedniej -- to dokumenty handlowe, ich historia bywa potrzebna.
CREATE TABLE IF NOT EXISTS project_attachments (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id  UUID NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    file_name   TEXT NOT NULL,
    file_path   TEXT NOT NULL UNIQUE,
    file_size   BIGINT,
    role        TEXT CHECK (role IN ('oferta', 'zlecenie')),
    uploaded_by UUID REFERENCES users(id) ON DELETE SET NULL,
    uploaded_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_project_attachments_project ON project_attachments (project_id);

GRANT SELECT, INSERT, UPDATE, DELETE ON project_attachments TO pdm_user;

COMMIT;
