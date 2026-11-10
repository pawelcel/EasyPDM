import { useCallback, useEffect, useState } from "react"

import { api } from "@/api/client"
import type { Process } from "@/api/types"

export function useProcesses() {
  const [processes, setProcesses] = useState<Process[]>([])

  const refetch = useCallback(async () => {
    setProcesses(await api.getProcesses())
  }, [])

  useEffect(() => {
    refetch()
  }, [refetch])

  return { processes, refetch }
}
