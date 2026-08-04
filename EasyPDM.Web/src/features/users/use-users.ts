import { useCallback, useEffect, useState } from "react"

import { api } from "@/api/client"
import type { ManagedUser } from "@/api/types"

export function useUsers() {
  const [users, setUsers] = useState<ManagedUser[]>([])

  const refetch = useCallback(async () => {
    setUsers(await api.getUsers())
  }, [])

  useEffect(() => {
    refetch()
  }, [refetch])

  return { users, refetch }
}
