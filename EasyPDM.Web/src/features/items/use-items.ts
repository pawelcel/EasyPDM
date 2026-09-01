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
  // Licznik żądań — bez tego szybkie pisanie w wyszukiwarce (każdy znak odpala nowy
  // refetch) mogłoby pokazać wyniki dla STARSZEGO zapytania, gdyby jego odpowiedź
  // wróciła później niż odpowiedź na już wpisany, dłuższy tekst.
  const requestIdRef = useRef(0)

  const refetch = useCallback(async () => {
    const requestId = ++requestIdRef.current
    if (!hasLoadedOnceRef.current) setLoading(true)
    setError(false)
    try {
      const data = await api.getItems(filters)
      if (requestIdRef.current !== requestId) return
      setItems(data)
      hasLoadedOnceRef.current = true
    } catch {
      if (requestIdRef.current === requestId) setError(true)
    } finally {
      if (requestIdRef.current === requestId) setLoading(false)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filters.search, filters.tag])

  useEffect(() => {
    refetch()
  }, [refetch])

  return { items, loading, error, refetch }
}
