import { useCallback, useEffect, useState } from "react"

import { api } from "@/api/client"
import type { Material } from "@/api/types"

export function useMaterials() {
  const [materials, setMaterials] = useState<Material[]>([])

  const refetch = useCallback(async () => {
    setMaterials(await api.getMaterials())
  }, [])

  useEffect(() => {
    refetch()
  }, [refetch])

  return { materials, refetch }
}
