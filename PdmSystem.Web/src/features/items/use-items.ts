import { useCallback, useEffect, useState } from "react"

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

  const refetch = useCallback(async () => {
    setLoading(true)
    setError(false)
    try {
      setItems(await api.getItems(filters))
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
