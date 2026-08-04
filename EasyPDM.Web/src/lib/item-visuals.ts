import { Bolt, Box, Boxes, Circle, File, Folder, Lock, LockOpen, ShoppingCart, type LucideIcon } from "lucide-react"

import { isOwnerLocked, type Item } from "@/api/types"

const TYPE_ICON: Record<Item["itemType"], LucideIcon> = {
  folder: Folder,
  part: Box,
  assembly: Boxes,
  file: File,
}

const STATUS_ICON_COLOR: Record<string, string> = {
  w_pracy: "text-muted-foreground",
  sprawdzany: "text-yellow-400",
  wydany: "text-green-400",
}

// "Rodzaj" Części dostaje inną ikonkę niż zwykłe pudełko — koszyk dla zakupowej, śrubka dla
// Normalii, okrąg dla Klienta. Złożenie ma też opcjonalne "rodzaj", ale to rozróżnienie
// dotyczy wyłącznie Części.
export function itemIcon(item: Item): LucideIcon {
  if (item.itemType === "part") {
    if (item.properties.rodzaj === "Zakupowa") return ShoppingCart
    if (item.properties.rodzaj === "Normalia") return Bolt
    if (item.properties.rodzaj === "Klienta") return Circle
  }
  return TYPE_ICON[item.itemType]
}

export function iconColorClass(item: Item): string {
  if (item.itemType !== "part" && item.itemType !== "assembly") return "text-muted-foreground"
  return STATUS_ICON_COLOR[item.status ?? "w_pracy"]
}

// Kłódka przy Części/Złożeniu w drzewku — tylko w statusach "w_pracy"/"sprawdzany" (w
// statusie "wydany" element zawsze jest zwolniony bez właściciela, więc kłódka nie ma tam
// sensu). Otwarta = brak właściciela (może edytować każdy), zielona = zablokowana przez
// bieżącego użytkownika, żółta = zablokowana przez kogoś innego.
export function ownerLockVisual(
  item: Item,
  currentUserId: string | undefined
): { Icon: LucideIcon; colorClass: string } | null {
  if (item.itemType !== "part" && item.itemType !== "assembly") return null
  if (item.status !== "w_pracy" && item.status !== "sprawdzany") return null

  if (!isOwnerLocked(item)) return { Icon: LockOpen, colorClass: "text-muted-foreground" }
  return item.ownerId === currentUserId
    ? { Icon: Lock, colorClass: "text-green-400" }
    : { Icon: Lock, colorClass: "text-yellow-400" }
}
