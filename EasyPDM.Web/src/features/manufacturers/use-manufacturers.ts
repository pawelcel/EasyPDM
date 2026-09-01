import { useCallback, useEffect, useRef, useState } from "react"

import { api } from "@/api/client"
import type { Manufacturer } from "@/api/types"

export function useManufacturers(search: string) {
  const [manufacturers, setManufacturers] = useState<Manufacturer[]>([])
  const [loading, setLoading] = useState(true)
  // Licznik żądań — bez tego szybkie pisanie w wyszukiwarce mogłoby pokazać wyniki dla
  // STARSZEGO zapytania, gdyby jego odpowiedź wróciła później niż dla już wpisanego,
  // dłuższego tekstu.
  const requestIdRef = useRef(0)

  const refetch = useCallback(async () => {
    const requestId = ++requestIdRef.current
    setLoading(true)
    try {
      const data = await api.getManufacturers(search || undefined)
      if (requestIdRef.current !== requestId) return
      setManufacturers(data)
    } finally {
      if (requestIdRef.current === requestId) setLoading(false)
    }
  }, [search])

  useEffect(() => {
    refetch()
  }, [refetch])

  return { manufacturers, loading, refetch }
}
