import type { Item, ItemRelation, ItemType, Project, Tag } from "./types"

const BASE = "/api"

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

  createProject: (body: { name: string; description: string | null }) =>
    fetch(`${BASE}/projects`, json(body)).then((r) => handleResponse<Project>(r)),

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

  setShowInTree: (itemId: string, showInTree: boolean) =>
    fetch(`${BASE}/items/${itemId}/visibility`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ showInTree }),
    }).then((r) => handleResponse<void>(r)),

  renameItem: (itemId: string, name: string) =>
    fetch(`${BASE}/items/${itemId}/name`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ name }),
    }).then((r) => handleResponse<void>(r)),

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

  addChild: (parentId: string, childId: string, quantity: number) =>
    fetch(`${BASE}/items/${parentId}/children`, json({ childId, quantity })).then((r) =>
      handleResponse<void>(r)
    ),

  removeChild: (parentId: string, childId: string) =>
    fetch(`${BASE}/items/${parentId}/children/${childId}`, { method: "DELETE" }).then((r) =>
      handleResponse<void>(r)
    ),
}
