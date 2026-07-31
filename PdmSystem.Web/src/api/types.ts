export interface Project {
  id: string
  name: string
  description: string | null
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
