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
      // Pobieramy WSZYSTKIE elementy (nie tylko z tego projektu) — Część/Złożenie może być
      // podpięte jako współdzielony komponent pod złożeniem w innym projekcie, więc do
      // poprawnego wyrenderowania takiego dziecka potrzebujemy go w puli niezależnie od tego,
      // do którego projektu formalnie należy. Korzenie i tak zostają ograniczone do tego projektu.
      const [allItems, nextRelations] = await Promise.all([
        api.getItems({}),
        api.getProjectRelations(projectId),
      ])
      setItems(allItems)
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
        .sort((a, b) => a.position - b.position)
        .map((r) => ({ item: itemsById.get(r.childId), quantity: r.quantity, position: r.position }))
        .filter(
          (c): c is { item: Item; quantity: number; position: number } => c.item !== undefined
        ),
    [relations, itemsById]
  )

  const childIds = useMemo(() => new Set(relations.map((r) => r.childId)), [relations])
  const roots = useMemo(
    () => items.filter((i) => i.projectId === projectId && i.showInTree && !childIds.has(i.id)),
    [items, childIds, projectId]
  )

  return { items, itemsById, roots, childrenOf, loading, refetch }
}
