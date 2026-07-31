import { useState } from "react"

import type { Item, Project } from "@/api/types"
import { Hint } from "@/components/ui/hint"
import { ItemCard } from "@/features/items/item-card"

function ItemList({
  items,
  loading,
  error,
  projects,
  onItemsRefetch,
  onTagsRefetch,
}: {
  items: Item[]
  loading: boolean
  error: boolean
  projects: Project[]
  onItemsRefetch: () => void | Promise<void>
  onTagsRefetch: () => void | Promise<void>
}) {
  const [expandedId, setExpandedId] = useState<string | null>(null)

  if (loading) return <Hint>Ładowanie…</Hint>
  if (error) return <Hint>Błąd ładowania danych z API.</Hint>
  if (items.length === 0) {
    return <Hint>Brak elementów pasujących do filtra. Kliknij "+ Element", żeby dodać pierwszy.</Hint>
  }

  return (
    <div className="flex flex-col gap-3">
      {items.map((item) => (
        <ItemCard
          key={item.id}
          item={item}
          projectName={projects.find((p) => p.id === item.projectId)?.name}
          open={expandedId === item.id}
          onToggle={() => setExpandedId(expandedId === item.id ? null : item.id)}
          onItemsRefetch={onItemsRefetch}
          onTagsRefetch={onTagsRefetch}
        />
      ))}
    </div>
  )
}

export { ItemList }
