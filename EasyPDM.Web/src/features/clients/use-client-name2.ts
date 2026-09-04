import { useClients } from "@/features/clients/use-clients"

// Nazwy 2 klienta wskazanego NAZWĄ — bo tak właśnie element trzyma klienta
// (properties.client to wolny tekst, nie klucz obcy), więc dopasowanie odbywa się po
// nazwie, tak jak przy producencie/serii-typie. GET /api/clients niesie już name2s dla
// każdego klienta (potrzebne też lewej liście w zakładce Klienci), więc bez osobnego
// zapytania -- tylko odszukanie klienta po nazwie w liście, którą useClients i tak już ma.
// Pusta nazwa (albo nazwa spoza katalogu) daje pustą listę.
export function useClientName2s(clientName: string) {
  const { clients } = useClients("")
  const matched = clients.find((c) => c.name === clientName) ?? null
  return { name2s: matched?.name2s ?? [], clientId: matched?.id ?? null }
}
