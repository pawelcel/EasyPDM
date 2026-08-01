export type UserRole = "admin" | "user"

export const ROLE_LABELS: Record<UserRole, string> = {
  admin: "Administrator",
  user: "Użytkownik",
}

// Zwracane przez GET/POST /api/auth/... — kim jest AKTUALNIE zalogowany.
export interface CurrentUser {
  id: string
  username: string
  displayName: string
  role: UserRole
}

// Zwracane przez GET /api/users — konta zarządzane przez administratora.
export interface ManagedUser {
  id: string
  username: string
  displayName: string
  email: string | null
  role: UserRole
}

export interface Project {
  id: string
  name: string
  description: string | null
  client: string | null
  startDate: string | null
  endDate: string | null
  createdAt: string
  itemCount: number
}

export type ItemType = "folder" | "part" | "file" | "assembly"

export type ItemStatus = "w_pracy" | "sprawdzany" | "wydany"

export const STATUS_LABELS: Record<ItemStatus, string> = {
  w_pracy: "W pracy",
  sprawdzany: "Sprawdzany",
  wydany: "Wydany",
}

export function isLocked(item: Pick<Item, "itemType" | "status">): boolean {
  return (item.itemType === "part" || item.itemType === "assembly") && item.status !== "w_pracy"
}

export function itemTypeLabel(item: Pick<Item, "itemType" | "fileType">): string | undefined {
  switch (item.itemType) {
    case "folder":
      return "Folder"
    case "part":
      return "Część"
    case "assembly":
      return "Złożenie"
    case "file":
      return item.fileType?.toUpperCase()
  }
}

// Część/Złożenie mają numer z bazy (item_number) — wyświetlamy je zawsze jako "numer (nazwa)".
// Folder/Plik nie mają numeru, więc pokazują samą nazwę.
export function itemDisplayLabel(item: Pick<Item, "fileName" | "itemNumber">): string {
  return item.itemNumber !== null ? `${item.itemNumber} (${item.fileName})` : item.fileName
}

// Rewizje wyświetlamy jako wielkie litery zamiast cyfr: 1->A, 2->B, ..., 26->Z, 27->AA...
// (jak numeracja kolumn arkusza) — sama liczba w bazie (revision_number) się nie zmienia.
export function revisionLabel(n: number): string {
  let value = n
  let label = ""
  while (value > 0) {
    const remainder = (value - 1) % 26
    label = String.fromCharCode(65 + remainder) + label
    value = Math.floor((value - 1) / 26)
  }
  return label || "A"
}

export interface Item {
  id: string
  projectId: string
  fileName: string
  fileType: string | null
  filePath: string | null
  properties: Record<string, unknown>
  modifiedAt: string | null
  itemType: ItemType
  itemNumber: number | null
  showInTree: boolean
  status: ItemStatus | null
  revisionNumber: number | null
  tags: string[]
}

export type Tag = string

// Opcjonalny komentarz do rewizji (tylko rewizje, którym faktycznie nadano komentarz —
// nie każda rewizja go ma).
export interface RevisionComment {
  revisionNumber: number
  comment: string
  createdAt: string
}

export interface ItemRelation {
  parentId: string
  childId: string
  quantity: number
  position: number
}

// "group" jest wyłącznie polem porządkowym/filtrującym katalogu materiałów —
// nigdy nie trafia do właściwości Części (Część zapisuje tylko "name").
export interface Material {
  name: string
  group: string | null
}

// Załącznik (plik dograny "z zewnątrz", np. plik CAD) — w odróżnieniu od struktury
// (item_relations) NIE jest osobnym elementem w drzewku, zarządzany tylko z panelu
// właściwości po prawej stronie.
export interface Attachment {
  id: string
  fileName: string
  fileSize: number | null
  uploadedAt: string | null
}
