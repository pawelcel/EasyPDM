import { useCallback, useEffect, useState } from "react"

import { api } from "@/api/client"
import type { ProjectLanguage } from "@/api/types"

// Lista języków dokumentacji (Ustawienia -> Język). Odczyt dla każdego zalogowanego.
export function useProjectLanguages() {
  const [languages, setLanguages] = useState<ProjectLanguage[]>([])

  const refetch = useCallback(async () => {
    setLanguages(await api.getProjectLanguages())
  }, [])

  useEffect(() => {
    refetch()
  }, [refetch])

  return { languages, refetch }
}
