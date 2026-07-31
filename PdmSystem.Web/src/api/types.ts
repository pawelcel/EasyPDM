export interface Project {
  id: string
  name: string
  description: string | null
  createdAt: string
  itemCount: number
}

export type ItemType = "folder" | "part" | "file" | "assembly"

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
  tags: string[]
}

export type Tag = string

export interface ItemRelation {
  parentId: string
  childId: string
  quantity: number
}
