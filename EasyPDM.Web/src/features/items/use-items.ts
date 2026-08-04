import { useCallback, useEffect, useRef, useState } from "react"

import { api } from "@/api/client"
import type { Item } from "@/api/types"

interface ItemFilters {
  search: string
  tag: string
}

export function useItems(filters: ItemFilters) {
  const [items, setItems] = useState<Item[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(false)
  // Tylko pierwsze wczytanie ma pokazywać "loading" — odświeżenia wywołane np. zapisaniem
  // właściwości elementu mają zostawić starą listę widoczną, aż przyjdą nowe dane, inaczej
  // lista migałaby na pusto przy każdej edycji.
  const hasLoadedOnceRef = useRef(false)

  const refetch = useCallback(async () => {
    if (!hasLoadedOnceRef.current) setLoading(true)
    setError(false)
    try {
      setItems(await api.getItems(filters))
      hasLoadedOnceRef.current = true
    } catch {
      setError(true)
    } finally {
      setLoading(false)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filters.search, filters.tag])

  useEffect(() => {
    refetch()
  }, [refetch])

  return { items, loading, error, refetch }
}
