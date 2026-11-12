import { useCallback, useEffect, useState } from "react"

import { api } from "@/api/client"
import type { ProjectLanguage } from "@/api/types"

// Lista języków dokumentacji (Ustawienia -> Język). Odczyt dla każdego zalogowanego.
export function useProjectLanguages() {
  const [languages, setLanguages] = useState<ProjectLanguage[]>([])

  const refetch = useCallback(async () => {
    try {
      setLanguages(await api.getProjectLanguages())
    } catch {
      // Bez listy języków pola wyboru zostają puste, a teksty pokazują się w wersji domyślnej —
      // nic nie przestaje działać, więc nie ma czym przerywać.
    }
  }, [])

  useEffect(() => {
    refetch()
  }, [refetch])

  return { languages, refetch }
}
