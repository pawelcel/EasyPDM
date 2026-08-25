import { useCallback, useEffect, useState } from "react"

import { api } from "@/api/client"
import type { Client } from "@/api/types"

export function useClients(search: string) {
  const [clients, setClients] = useState<Client[]>([])
  const [loading, setLoading] = useState(true)

  const refetch = useCallback(async () => {
    setLoading(true)
    try {
      setClients(await api.getClients(search || undefined))
    } finally {
      setLoading(false)
    }
  }, [search])

  useEffect(() => {
    refetch()
  }, [refetch])

  return { clients, loading, refetch }
}
