import { useCallback, useEffect, useMemo, useState } from "react"

import { api } from "@/api/client"
import type { Item, ItemRelation } from "@/api/types"

export function useProjectTree(projectId: string) {
  const [items, setItems] = useState<Item[]>([])
  const [relations, setRelations] = useState<ItemRelation[]>([])
  const [loading, setLoading] = useState(true)

  const refetch = useCallback(async () => {
    if (!projectId) return
    setLoading(true)
    try {
      const [nextItems, nextRelations] = await Promise.all([
        api.getItems({ projectId }),
        api.getProjectRelations(projectId),
      ])
      setItems(nextItems)
      setRelations(nextRelations)
    } finally {
      setLoading(false)
    }
  }, [projectId])

  useEffect(() => {
    refetch()
  }, [refetch])

  const itemsById = useMemo(() => new Map(items.map((i) => [i.id, i])), [items])

  const childrenOf = useCallback(
    (parentId: string) =>
      relations
        .filter((r) => r.parentId === parentId)
        .map((r) => ({ item: itemsById.get(r.childId), quantity: r.quantity }))
        .filter((c): c is { item: Item; quantity: number } => c.item !== undefined),
    [relations, itemsById]
  )

  const childIds = useMemo(() => new Set(relations.map((r) => r.childId)), [relations])
  const roots = useMemo(
    () => items.filter((i) => i.showInTree && !childIds.has(i.id)),
    [items, childIds]
  )

  return { items, itemsById, roots, childrenOf, loading, refetch }
}
