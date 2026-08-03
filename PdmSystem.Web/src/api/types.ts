import type { TranslationKey } from "@/i18n/translations"

export type UserRole = "admin" | "user"

export const ROLE_LABEL_KEYS: Record<UserRole, TranslationKey> = {
  admin: "role.admin",
  user: "role.user",
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

// Przypisanie użytkownika do projektu (project_users) — administrator zawsze widzi
// wszystkie projekty niezależnie od tych przypisań; dotyczą tylko roli "user".
export interface ProjectUserAssignment {
  projectId: string
  userId: string
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

export const STATUS_LABEL_KEYS: Record<ItemStatus, TranslationKey> = {
  w_pracy: "status.w_pracy",
  sprawdzany: "status.sprawdzany",
  wydany: "status.wydany",
}

export function isLocked(item: Pick<Item, "itemType" | "status">): boolean {
  return (item.itemType === "part" || item.itemType === "assembly") && item.status !== "w_pracy"
}

// Folder/Część/Złożenie mają stałe, tłumaczone nazwy (klucz do t()) — Plik pokazuje zamiast
// tego swoje rozszerzenie (np. "PDF"), które nie jest tekstem do tłumaczenia.
export function itemTypeLabelKey(
  item: Pick<Item, "itemType" | "fileType">
): TranslationKey | undefined {
  switch (item.itemType) {
    case "folder":
      return "itemType.folder"
    case "part":
      return "itemType.part"
    case "assembly":
      return "itemType.assembly"
    case "file":
      return undefined
  }
}

export function fileTypeLabel(item: Pick<Item, "itemType" | "fileType">): string | undefined {
  return item.itemType === "file" ? item.fileType?.toUpperCase() : undefined
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
  rootPosition: number
  ownerId: string | null
  ownerLocked: boolean
  ownerDisplayName: string | null
  tags: string[]
}

// Właściciel (part/assembly) — dopóki ownerLocked=true, tylko ownerId może edytować
// (nawet admin nie omija tego, patrz backend ItemEndpoints.CanEditOwnerLocked). Element
// bez właściciela (ownerId=null, elementy sprzed tej funkcji) traktujemy jak zwolniony.
export function isOwnerLocked(item: Pick<Item, "ownerId" | "ownerLocked">): boolean {
  return item.ownerLocked && item.ownerId !== null
}

export function canEditOwnerLocked(
  item: Pick<Item, "ownerId" | "ownerLocked">,
  currentUserId: string
): boolean {
  return !isOwnerLocked(item) || item.ownerId === currentUserId
}

export type Tag = string

// Opcjonalny komentarz do rewizji (tylko rewizje, którym faktycznie nadano komentarz —
// nie każda rewizja go ma).
export interface RevisionComment {
  revisionNumber: number
  comment: string
  createdAt: string
}

// Historia Części/Złożenia (do panelu "Historia") — kilka rodzajów wpisów połączonych
// przez backend w jedną chronologiczną listę: utworzenie elementu, zmiana statusu,
// rewizja z komentarzem, dodanie/usunięcie załącznika, zablokowanie/zwolnienie właściciela.
// Pola nieistotne dla danego "type" przychodzą jako null.
export type HistoryEventType =
  | "created"
  | "status"
  | "revision"
  | "attachment_added"
  | "attachment_removed"
  | "owner_locked"
  | "owner_released"

export interface HistoryEntry {
  type: HistoryEventType
  at: string
  userDisplayName: string | null
  fromStatus: ItemStatus | null
  toStatus: ItemStatus | null
  revisionNumber: number | null
  comment: string | null
  fileName: string | null
}

export interface ItemRelation {
  parentId: string
  childId: string
  quantity: number
  position: number
}

// Zagłębiony wpis BOM-u (głębiej niż bezpośrednie dziecko) — zwracany przez
// GET /api/items/{id}/bom. "path" to pełna ścieżka L.p. od bieżącego złożenia w dół,
// np. [2, 1] dla drugiego bezpośredniego dziecka, pierwszego elementu w jego BOM-ie.
export interface BomEntry {
  itemId: string
  quantity: number
  depth: number
  path: number[]
  itemNumber: number | null
  fileName: string
  properties: Record<string, unknown>
}

// L.p. zagłębionego wpisu BOM-u jako tekst, np. [2, 1] -> "2.1".
export function bomPositionLabel(path: number[]): string {
  return path.join(".")
}

// "group" jest wyłącznie polem porządkowym/filtrującym katalogu materiałów —
// nigdy nie trafia do właściwości Części (Część zapisuje tylko "name").
export interface Material {
  id: number
  name: string
  group: string | null
  subgroup: string | null
}

// Lekki wpis do listy/wyszukiwarki producentów — bez osób kontaktowych (te dociągane są
// osobno, dopiero po zaznaczeniu konkretnego producenta).
export interface Manufacturer {
  id: number
  name: string
  contactCount: number
}

export interface ManufacturerContact {
  id: number
  firstName: string | null
  lastName: string | null
  phone: string | null
  position: string | null
  email: string | null
}

export interface ManufacturerDetail {
  id: number
  name: string
  contacts: ManufacturerContact[]
}

export interface StorageInfo {
  path: string
  fileCount: number
  totalSizeBytes: number
}

// Zapisany zestaw filtrów widoku "Cała baza" — prywatny dla każdego użytkownika (serwer
// zawsze bierze user_id z sesji, nigdy z ciała żądania). "filters" trzymane luźno — dokładny
// kształt narzuca DatabaseFilters we features/items/database-filters.ts.
export interface SavedFilter {
  id: string
  name: string
  filters: Record<string, unknown>
  createdAt: string
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
