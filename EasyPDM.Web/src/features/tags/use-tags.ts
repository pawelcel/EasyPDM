import { useCallback, useEffect, useState } from "react"

import { api } from "@/api/client"
import type { Tag } from "@/api/types"

export function useTags() {
  const [tags, setTags] = useState<Tag[]>([])

  const refetch = useCallback(async () => {
    setTags(await api.getTags())
  }, [])

  useEffect(() => {
    refetch()
  }, [refetch])

  return { tags, refetch }
}
