import { useMemo } from "react"

import type { Project } from "@/api/types"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { useLanguage } from "@/i18n/use-language"

// Lista klientów wynika wprost z przekazanych projektów (Project już niesie
// clientId/clientName) -- w odróżnieniu od ManufacturerFilterSelect nie ma
// tu osobnego zapytania do całego katalogu Klientów, bo w "Cała baza" i tak liczy się
// tylko to, którzy klienci faktycznie mają tu jakiś projekt/element. Sam klient (nie jego
// ewentualne nazwy 2 -- może ich być kilka, projekt nie wskazuje żadnej konkretnej) jest
// tym, do czego projekt jest powiązany (projects.client_id).
function ClientFilterSelect({
  projects,
  value,
  onChange,
  disabled,
}: {
  projects: Project[]
  value: string
  onChange: (value: string) => void
  disabled?: boolean
}) {
  const { t } = useLanguage()

  const clients = useMemo(() => {
    const byId = new Map<number, string>()
    for (const p of projects) {
      if (p.clientId == null || byId.has(p.clientId)) continue
      byId.set(p.clientId, p.clientName ?? "")
    }
    return [...byId.entries()].sort((a, b) => a[1].localeCompare(b[1]))
  }, [projects])

  function labelFor(v: string) {
    if (v === "all" || !v) return t("filter.allClients")
    return clients.find(([id]) => String(id) === v)?.[1] ?? v
  }

  return (
    <Select value={value || "all"} onValueChange={(v) => onChange(v === "all" ? "" : (v as string))} disabled={disabled}>
      <SelectTrigger className="min-w-40">
        <SelectValue>{(v: string) => labelFor(v)}</SelectValue>
      </SelectTrigger>
      <SelectContent>
        <SelectItem value="all">{t("filter.allClients")}</SelectItem>
        {clients.map(([id, label]) => (
          <SelectItem key={id} value={String(id)}>
            {label}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

export { ClientFilterSelect }
