import { Box, Boxes, File, Folder, ShoppingCart, type LucideIcon } from "lucide-react"

import type { Item } from "@/api/types"

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

// Część zakupowa dostaje inną ikonkę niż wykonywana (koszyk zamiast zwykłego pudełka) —
// Złożenie ma też opcjonalne "rodzaj", ale to rozróżnienie dotyczy wyłącznie Części.
export function itemIcon(item: Item): LucideIcon {
  if (item.itemType === "part" && item.properties.rodzaj === "Zakupowa") return ShoppingCart
  return TYPE_ICON[item.itemType]
}

export function iconColorClass(item: Item): string {
  if (item.itemType !== "part" && item.itemType !== "assembly") return "text-muted-foreground"
  return STATUS_ICON_COLOR[item.status ?? "w_pracy"]
}
