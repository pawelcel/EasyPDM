import { useCallback, useEffect, useState } from "react"

import { api } from "@/api/client"
import type { ClientVerificationSummary } from "@/api/types"

// Ostatni wynik weryfikacji klienta dla KAŻDEGO elementu w projekcie, pobierany jednym
// żądaniem — znacznik ma się pokazać przy dowolnym elemencie w drzewku, więc odpytywanie
// serwera osobno dla każdego byłoby setkami żądań na jedno otwarcie projektu.
export function useClientVerifications(projectId: string) {
  const [byItemId, setByItemId] = useState<Map<string, ClientVerificationSummary>>(new Map())

  const refetch = useCallback(async () => {
    if (!projectId) {
      setByItemId(new Map())
      return
    }
    try {
      const rows = await api.getProjectClientVerificationSummary(projectId)
      setByItemId(new Map(rows.map((row) => [row.itemId, row])))
    } catch {
      // Brak dostępu/błąd sieci — znaczniki po prostu się nie pojawią, reszta widoku działa.
      setByItemId(new Map())
    }
  }, [projectId])

  useEffect(() => {
    refetch()
  }, [refetch])

  return { clientVerifications: byItemId, refetchClientVerifications: refetch }
}
