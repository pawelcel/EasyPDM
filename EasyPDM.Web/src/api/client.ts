import type {
  Attachment,
  BackupSchedule,
  BomEntry,
  CurrentUser,
  HistoryEntry,
  Item,
  ItemRelation,
  ItemStatus,
  ItemType,
  LogContent,
  LogFile,
  ManagedUser,
  Manufacturer,
  ManufacturerDetail,
  Material,
  Project,
  ProjectUserAssignment,
  RevisionComment,
  SavedFilter,
  StorageInfo,
  Tag,
  UserRole,
} from "./types"

// Zdarzenie globalne: dowolne wywołanie API, które dostanie 401, oznacza że sesja wygasła
// (albo nigdy nie było zalogowania) — AuthProvider nasłuchuje na to zamiast każdy komponent
// musiałby osobno sprawdzać ApiError.status.
export const UNAUTHORIZED_EVENT = "pdm:unauthorized"

const BASE = "/api"

type ProjectWriteBody = {
  name: string
  description: string | null
  client: string | null
  startDate: string | null
  endDate: string | null
}

type MaterialWriteBody = {
  name: string
  group: string | null
  subgroup: string | null
}

type ContactWriteBody = {
  firstName: string | null
  lastName: string | null
  phone: string | null
  position: string | null
  email: string | null
}

export class ApiError extends Error {
  status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

// Endpointy zwracają błędy jako zwykły tekst (Results.BadRequest("..."),
// Results.Conflict("...")), nie jako JSON — dlatego czytamy body przez text(),
// nie json(), zanim rzucimy błąd.
async function handleResponse<T>(res: Response): Promise<T> {
  if (!res.ok) {
    const text = await res.text().catch(() => "")
    if (res.status === 401) window.dispatchEvent(new Event(UNAUTHORIZED_EVENT))
    throw new ApiError(res.status, text || `Żądanie nie powiodło się (${res.status}).`)
  }
  if (res.status === 204) return undefined as T
  const text = await res.text()
  return text ? (JSON.parse(text) as T) : (undefined as T)
}

function json(body: unknown): RequestInit {
  return {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  }
}

export const api = {
  getProjects: () => fetch(`${BASE}/projects`).then((r) => handleResponse<Project[]>(r)),

  createProject: (body: ProjectWriteBody) =>
    fetch(`${BASE}/projects`, json(body)).then((r) => handleResponse<Project>(r)),

  updateProject: (id: string, body: ProjectWriteBody) =>
    fetch(`${BASE}/projects/${id}`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    }).then((r) => handleResponse<Project>(r)),

  deleteProject: (id: string) =>
    fetch(`${BASE}/projects/${id}`, { method: "DELETE" }).then((r) => handleResponse<void>(r)),

  getProjectUsers: () =>
    fetch(`${BASE}/project-users`).then((r) => handleResponse<ProjectUserAssignment[]>(r)),

  grantProjectAccess: (projectId: string, userId: string) =>
    fetch(`${BASE}/projects/${projectId}/users/${userId}`, { method: "POST" }).then((r) =>
      handleResponse<void>(r)
    ),

  revokeProjectAccess: (projectId: string, userId: string) =>
    fetch(`${BASE}/projects/${projectId}/users/${userId}`, { method: "DELETE" }).then((r) =>
      handleResponse<void>(r)
    ),

  getItems: (params: { search?: string; tag?: string; projectId?: string }) => {
    const query = new URLSearchParams()
    if (params.search) query.set("search", params.search)
    if (params.tag) query.set("tag", params.tag)
    if (params.projectId) query.set("projectId", params.projectId)
    const qs = query.toString()
    return fetch(`${BASE}/items${qs ? `?${qs}` : ""}`).then((r) => handleResponse<Item[]>(r))
  },

  getItem: (id: string) => fetch(`${BASE}/items/${id}`).then((r) => handleResponse<Item>(r)),

  uploadItem: (projectId: string, formData: FormData) =>
    fetch(`${BASE}/projects/${projectId}/items`, { method: "POST", body: formData }).then((r) =>
      handleResponse<{ id: string }>(r)
    ),

  createNode: (
    projectId: string,
    body: {
      name: string
      itemType: ItemType
      properties?: Record<string, unknown>
      parentId?: string | null
    }
  ) =>
    fetch(`${BASE}/projects/${projectId}/nodes`, json(body)).then((r) =>
      handleResponse<{ id: string; itemNumber: number | null }>(r)
    ),

  deleteItem: (itemId: string) =>
    fetch(`${BASE}/items/${itemId}`, { method: "DELETE" }).then((r) =>
      handleResponse<{ deletedCount: number }>(r)
    ),

  duplicateItem: (
    itemId: string,
    structure?: { parentId: string | null; insertAfterOriginal: true }
  ) =>
    fetch(
      `${BASE}/items/${itemId}/duplicate`,
      json(structure ?? { parentId: null, insertAfterOriginal: false })
    ).then((r) => handleResponse<{ id: string; itemNumber: number | null }>(r)),

  setShowInTree: (itemId: string, showInTree: boolean) =>
    fetch(`${BASE}/items/${itemId}/visibility`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ showInTree }),
    }).then((r) => handleResponse<void>(r)),

  moveItemToProject: (itemId: string, projectId: string) =>
    fetch(`${BASE}/items/${itemId}/project`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ projectId }),
    }).then((r) => handleResponse<void>(r)),

  renameItem: (itemId: string, name: string) =>
    fetch(`${BASE}/items/${itemId}/name`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ name }),
    }).then((r) => handleResponse<void>(r)),

  setStatus: (itemId: string, status: ItemStatus, comment?: string) =>
    fetch(`${BASE}/items/${itemId}/status`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ status, comment: comment || null }),
    }).then((r) => handleResponse<{ status: ItemStatus; revisionNumber: number | null }>(r)),

  getRevisionComments: (itemId: string) =>
    fetch(`${BASE}/items/${itemId}/revisions`).then((r) => handleResponse<RevisionComment[]>(r)),

  getItemHistory: (itemId: string) =>
    fetch(`${BASE}/items/${itemId}/history`).then((r) => handleResponse<HistoryEntry[]>(r)),

  lockItem: (itemId: string) =>
    fetch(`${BASE}/items/${itemId}/lock`, { method: "POST" }).then((r) =>
      handleResponse<{ ownerId: string; ownerLocked: boolean }>(r)
    ),

  releaseItem: (itemId: string) =>
    fetch(`${BASE}/items/${itemId}/release`, { method: "POST" }).then((r) =>
      handleResponse<{ ownerId: string | null; ownerLocked: boolean }>(r)
    ),

  fileDownloadUrl: (itemId: string) => `${BASE}/items/${itemId}/file`,

  getTags: () => fetch(`${BASE}/tags`).then((r) => handleResponse<Tag[]>(r)),

  addTag: (itemId: string, name: string) =>
    fetch(`${BASE}/items/${itemId}/tags`, json({ name })).then((r) => handleResponse<void>(r)),

  removeTag: (itemId: string, tagName: string) =>
    fetch(`${BASE}/items/${itemId}/tags/${encodeURIComponent(tagName)}`, {
      method: "DELETE",
    }).then((r) => handleResponse<void>(r)),

  updateProperties: (itemId: string, props: Record<string, unknown>) =>
    fetch(`${BASE}/items/${itemId}/properties`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(props),
    }).then((r) => handleResponse<void>(r)),

  deleteProperty: (itemId: string, key: string) =>
    fetch(`${BASE}/items/${itemId}/properties/${encodeURIComponent(key)}`, {
      method: "DELETE",
    }).then((r) => handleResponse<void>(r)),

  getProjectRelations: (projectId: string) =>
    fetch(`${BASE}/projects/${projectId}/relations`).then((r) =>
      handleResponse<ItemRelation[]>(r)
    ),

  // W odróżnieniu od getProjectRelations (całe drzewko jednego projektu naraz) — bezpośrednie
  // dzieci JEDNEGO elementu jako pełne obiekty Item, do widoków bez wcześniej załadowanego
  // drzewka relacji (np. "Cała baza", gdzie zaznaczony element może być z dowolnego projektu).
  getItemChildren: (itemId: string) =>
    fetch(`${BASE}/items/${itemId}/children`).then((r) =>
      handleResponse<{ item: Item; quantity: number; position: number }[]>(r)
    ),

  addChild: (parentId: string, childId: string, quantity: number) =>
    fetch(`${BASE}/items/${parentId}/children`, json({ childId, quantity })).then((r) =>
      handleResponse<void>(r)
    ),

  removeChild: (parentId: string, childId: string) =>
    fetch(`${BASE}/items/${parentId}/children/${childId}`, { method: "DELETE" }).then((r) =>
      handleResponse<void>(r)
    ),

  setChildPosition: (parentId: string, childId: string, position: number) =>
    fetch(`${BASE}/items/${parentId}/children/${childId}/position`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ position }),
    }).then((r) => handleResponse<void>(r)),

  reorderChildren: (parentId: string, childIds: string[]) =>
    fetch(`${BASE}/items/${parentId}/children/reorder`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ childIds }),
    }).then((r) => handleResponse<void>(r)),

  reorderRoots: (projectId: string, itemIds: string[]) =>
    fetch(`${BASE}/projects/${projectId}/roots/reorder`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ itemIds }),
    }).then((r) => handleResponse<void>(r)),

  getBom: (itemId: string) =>
    fetch(`${BASE}/items/${itemId}/bom`).then((r) => handleResponse<BomEntry[]>(r)),

  bomCsvUrl: (itemId: string) => `${BASE}/items/${itemId}/bom/csv`,

  bomAggregatedCsvUrl: (itemId: string) => `${BASE}/items/${itemId}/bom/aggregated-csv`,

  getItemDocumentationExtensions: (itemId: string) =>
    fetch(`${BASE}/items/${itemId}/documentation/extensions`).then((r) => handleResponse<string[]>(r)),

  itemDocumentationUrl: (itemId: string, extensions: string[]) =>
    `${BASE}/items/${itemId}/documentation?${extensions.map((e) => `ext=${encodeURIComponent(e)}`).join("&")}`,

  getProjectDocumentationExtensions: (projectId: string) =>
    fetch(`${BASE}/projects/${projectId}/documentation/extensions`).then((r) => handleResponse<string[]>(r)),

  projectDocumentationUrl: (projectId: string, extensions: string[]) =>
    `${BASE}/projects/${projectId}/documentation?${extensions.map((e) => `ext=${encodeURIComponent(e)}`).join("&")}`,

  getMaterials: () => fetch(`${BASE}/materials`).then((r) => handleResponse<Material[]>(r)),

  addMaterial: (body: MaterialWriteBody) =>
    fetch(`${BASE}/materials`, json(body)).then((r) => handleResponse<{ id: number }>(r)),

  updateMaterial: (id: number, body: MaterialWriteBody) =>
    fetch(`${BASE}/materials/${id}`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    }).then((r) => handleResponse<void>(r)),

  removeMaterial: (id: number) =>
    fetch(`${BASE}/materials/${id}`, { method: "DELETE" }).then((r) => handleResponse<void>(r)),

  getManufacturers: (search?: string) =>
    fetch(`${BASE}/manufacturers${search ? `?search=${encodeURIComponent(search)}` : ""}`).then(
      (r) => handleResponse<Manufacturer[]>(r)
    ),

  getManufacturer: (id: number) =>
    fetch(`${BASE}/manufacturers/${id}`).then((r) => handleResponse<ManufacturerDetail>(r)),

  createManufacturer: (name: string) =>
    fetch(`${BASE}/manufacturers`, json({ name })).then((r) => handleResponse<{ id: number }>(r)),

  updateManufacturer: (id: number, name: string) =>
    fetch(`${BASE}/manufacturers/${id}`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ name }),
    }).then((r) => handleResponse<void>(r)),

  removeManufacturer: (id: number) =>
    fetch(`${BASE}/manufacturers/${id}`, { method: "DELETE" }).then((r) => handleResponse<void>(r)),

  addManufacturerContact: (manufacturerId: number, body: ContactWriteBody) =>
    fetch(`${BASE}/manufacturers/${manufacturerId}/contacts`, json(body)).then((r) =>
      handleResponse<{ id: number }>(r)
    ),

  updateManufacturerContact: (manufacturerId: number, contactId: number, body: ContactWriteBody) =>
    fetch(`${BASE}/manufacturers/${manufacturerId}/contacts/${contactId}`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    }).then((r) => handleResponse<void>(r)),

  removeManufacturerContact: (manufacturerId: number, contactId: number) =>
    fetch(`${BASE}/manufacturers/${manufacturerId}/contacts/${contactId}`, {
      method: "DELETE",
    }).then((r) => handleResponse<void>(r)),

  getSavedFilters: () =>
    fetch(`${BASE}/saved-filters`).then((r) => handleResponse<SavedFilter[]>(r)),

  saveFilter: (name: string, filters: Record<string, unknown>) =>
    fetch(`${BASE}/saved-filters`, json({ name, filters })).then((r) =>
      handleResponse<SavedFilter>(r)
    ),

  deleteSavedFilter: (id: string) =>
    fetch(`${BASE}/saved-filters/${id}`, { method: "DELETE" }).then((r) => handleResponse<void>(r)),

  getStorageInfo: () => fetch(`${BASE}/settings/storage`).then((r) => handleResponse<StorageInfo>(r)),

  moveStorage: (newPath: string, migrateExisting: boolean) =>
    fetch(`${BASE}/settings/storage/move`, json({ newPath, migrateExisting })).then((r) =>
      handleResponse<{ path: string; migratedFiles: number }>(r)
    ),

  backupUrl: () => `${BASE}/settings/backup`,

  getBackupSchedule: () =>
    fetch(`${BASE}/settings/backup-schedule`).then((r) => handleResponse<BackupSchedule>(r)),

  updateBackupSchedule: (schedule: Omit<BackupSchedule, "lastRunAt">) =>
    fetch(`${BASE}/settings/backup-schedule`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(schedule),
    }).then((r) => handleResponse<BackupSchedule>(r)),

  restoreBackup: (file: File) => {
    const formData = new FormData()
    formData.append("file", file)
    return fetch(`${BASE}/settings/restore`, { method: "POST", body: formData }).then((r) =>
      handleResponse<{ success: boolean; warnings: string; filesRestored: number }>(r)
    )
  },

  getAttachments: (itemId: string) =>
    fetch(`${BASE}/items/${itemId}/attachments`).then((r) => handleResponse<Attachment[]>(r)),

  uploadAttachment: (itemId: string, formData: FormData) =>
    fetch(`${BASE}/items/${itemId}/attachments`, { method: "POST", body: formData }).then((r) =>
      handleResponse<{ id: string; fileName: string }>(r)
    ),

  deleteAttachment: (attachmentId: string) =>
    fetch(`${BASE}/attachments/${attachmentId}`, { method: "DELETE" }).then((r) =>
      handleResponse<void>(r)
    ),

  attachmentDownloadUrl: (attachmentId: string) => `${BASE}/attachments/${attachmentId}/file`,

  login: (username: string, password: string) =>
    fetch(`${BASE}/auth/login`, json({ username, password })).then((r) =>
      handleResponse<CurrentUser>(r)
    ),

  logout: () =>
    fetch(`${BASE}/auth/logout`, { method: "POST" }).then((r) => handleResponse<void>(r)),

  getMe: () => fetch(`${BASE}/auth/me`).then((r) => handleResponse<CurrentUser>(r)),

  changePassword: (currentPassword: string, newPassword: string) =>
    fetch(`${BASE}/auth/password`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ currentPassword, newPassword }),
    }).then((r) => handleResponse<void>(r)),

  getUsers: () => fetch(`${BASE}/users`).then((r) => handleResponse<ManagedUser[]>(r)),

  createUser: (body: {
    username: string
    password: string
    displayName: string
    email: string | null
    role: UserRole
  }) => fetch(`${BASE}/users`, json(body)).then((r) => handleResponse<{ id: string }>(r)),

  updateUser: (
    id: string,
    body: { displayName?: string; email?: string; role?: UserRole; password?: string }
  ) =>
    fetch(`${BASE}/users/${id}`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    }).then((r) => handleResponse<void>(r)),

  deleteUser: (id: string) =>
    fetch(`${BASE}/users/${id}`, { method: "DELETE" }).then((r) => handleResponse<void>(r)),

  getLogFiles: () => fetch(`${BASE}/settings/logs`).then((r) => handleResponse<LogFile[]>(r)),

  getLogContent: (date: string, lines?: number) =>
    fetch(`${BASE}/settings/logs/${date}${lines ? `?lines=${lines}` : ""}`).then((r) =>
      handleResponse<LogContent>(r)
    ),

  logDownloadUrl: (date: string) => `${BASE}/settings/logs/${date}/download`,
}
