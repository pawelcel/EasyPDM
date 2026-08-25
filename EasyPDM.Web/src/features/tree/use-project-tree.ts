import { useCallback, useEffect, useMemo, useRef, useState } from "react"

import { api } from "@/api/client"
import type { Item, ItemRelation } from "@/api/types"

export function useProjectTree(projectId: string) {
  const [items, setItems] = useState<Item[]>([])
  const [relations, setRelations] = useState<ItemRelation[]>([])
  const [loading, setLoading] = useState(true)
  // Tylko PIERWSZE wczytanie tego projektu ma pokazywać "loading" (drzewko puste/nieznane).
  // Odświeżenia wywołane np. zapisaniem właściwości elementu mają zostawić stare drzewko
  // widoczne, aż przyjdą nowe dane — inaczej drzewko migałoby na pusto przy każdej edycji.
  const loadedProjectIdRef = useRef<string | null>(null)

  const refetch = useCallback(async () => {
    if (!projectId) return
    if (loadedProjectIdRef.current !== projectId) setLoading(true)
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
      loadedProjectIdRef.current = projectId
    } finally {
      setLoading(false)
    }
  }, [projectId])

  useEffect(() => {
    refetch()
  }, [refetch])

  const itemsById = useMemo(() => new Map(items.map((i) => [i.id, i])), [items])

  // Zgrupowane raz per zmianę relations/itemsById (nie przy każdym wywołaniu childrenOf) —
  // dzięki temu childrenOf(id) zwraca TĘ SAMĄ referencję tablicy między renderami, dopóki
  // struktura faktycznie się nie zmieniła. Bez tego każde wywołanie (np. w JSX) budowało nową
  // tablicę, co w ItemDetailPanel odpalało zbędny refetch zagłębionego BOM-u przy KAŻDYM,
  // niepowiązanym re-renderze (np. włączeniu trybu zaznaczania w drzewku).
  const childrenByParentId = useMemo(() => {
    const map = new Map<string, { item: Item; quantity: number; position: number }[]>()
    const sorted = [...relations].sort((a, b) => a.position - b.position)
    for (const r of sorted) {
      const item = itemsById.get(r.childId)
      if (!item) continue
      const entry = { item, quantity: r.quantity, position: r.position }
      const list = map.get(r.parentId)
      if (list) list.push(entry)
      else map.set(r.parentId, [entry])
    }
    return map
  }, [relations, itemsById])

  const emptyChildren = useMemo(() => [], [])

  const childrenOf = useCallback(
    (parentId: string) => childrenByParentId.get(parentId) ?? emptyChildren,
    [childrenByParentId, emptyChildren]
  )

  // Korzeń = showInTree, NIEZALEŻNIE od tego, czy element ma też rodzica gdzie indziej — element
  // może być jednocześnie widoczny jako korzeń projektu I zagnieżdżony pod złożeniem (np. część
  // dodana do projektu, a potem dołączona jako podelement już istniejącego złożenia — oba miejsca
  // mają zostać widoczne). Zgodne z backendowym wyliczaniem korzeni w StructureEndpoints.cs
  // (roots/reorder).
  const roots = useMemo(
    () =>
      items
        .filter((i) => i.projectId === projectId && i.showInTree)
        .sort((a, b) => a.rootPosition - b.rootPosition),
    [items, projectId]
  )

  return { items, itemsById, roots, childrenOf, loading, refetch }
}
