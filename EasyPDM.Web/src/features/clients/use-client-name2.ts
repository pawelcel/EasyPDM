import { useEffect, useState } from "react"

import { api } from "@/api/client"
import type { ClientName2 } from "@/api/types"
import { useClients } from "@/features/clients/use-clients"

// Nazwy 2 klienta wskazanego NAZWĄ — bo tak właśnie element trzyma klienta
// (properties.client to wolny tekst, nie klucz obcy), więc id trzeba najpierw odszukać w
// katalogu; ten sam sposób dopasowania stosuje useManufacturerProductTypes dla
// producenta/serii-typu. Pusta nazwa (albo nazwa spoza katalogu) daje pustą listę.
export function useClientName2s(clientName: string) {
  const { clients } = useClients("")
  const clientId = clients.find((c) => c.name === clientName)?.id ?? null
  const [name2s, setName2s] = useState<ClientName2[]>([])

  useEffect(() => {
    if (clientId === null) {
      setName2s([])
      return
    }
    let cancelled = false
    api
      .getClient(clientId)
      .then((detail) => {
        if (!cancelled) setName2s(detail.name2s)
      })
      .catch(() => {
        if (!cancelled) setName2s([])
      })
    return () => {
      cancelled = true
    }
  }, [clientId])

  return { name2s, clientId }
}
