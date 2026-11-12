import { useEffect, useState } from "react"

import { api } from "@/api/client"

// Czy katalogi (Materiały, Obróbki, Producenci, Klienci) są dla tego użytkownika tylko do
// odczytu: włączona opcja "katalogi zmienia tylko administrator", a to nie administrator.
// Regułę egzekwuje serwer; tu chodzi o to, żeby interfejs nie proponował czegoś, co odmówi.
export function useCatalogReadOnly(isAdmin: boolean) {
  const [adminOnly, setAdminOnly] = useState(false)

  useEffect(() => {
    if (isAdmin) return
    api.getCatalogPolicy().then((p) => setAdminOnly(p.adminOnly)).catch(() => {})
  }, [isAdmin])

  return !isAdmin && adminOnly
}
