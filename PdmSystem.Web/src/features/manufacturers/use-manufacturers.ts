import { useCallback, useEffect, useState } from "react"

import { api } from "@/api/client"
import type { Manufacturer } from "@/api/types"

export function useManufacturers(search: string) {
  const [manufacturers, setManufacturers] = useState<Manufacturer[]>([])
  const [loading, setLoading] = useState(true)

  const refetch = useCallback(async () => {
    setLoading(true)
    try {
      setManufacturers(await api.getManufacturers(search || undefined))
    } finally {
      setLoading(false)
    }
  }, [search])

  useEffect(() => {
    refetch()
  }, [refetch])

  return { manufacturers, loading, refetch }
}
