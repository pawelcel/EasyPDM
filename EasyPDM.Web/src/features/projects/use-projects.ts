import { useCallback, useEffect, useState } from "react"

import { api } from "@/api/client"
import type { Project } from "@/api/types"

export function useProjects() {
  const [projects, setProjects] = useState<Project[]>([])
  const [loading, setLoading] = useState(true)

  const refetch = useCallback(async () => {
    setLoading(true)
    try {
      setProjects(await api.getProjects())
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    refetch()
  }, [refetch])

  return { projects, loading, refetch }
}
