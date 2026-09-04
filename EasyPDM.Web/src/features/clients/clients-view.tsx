import { useState } from "react"
import { Plus } from "lucide-react"

import { api, ApiError } from "@/api/client"
import type { Client } from "@/api/types"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog"
import { FormError } from "@/components/ui/form-error"
import { Hint } from "@/components/ui/hint"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { useDebouncedValue } from "@/hooks/use-debounced-value"
import { ClientDetailPanel } from "@/features/clients/client-detail-panel"
import { useClients } from "@/features/clients/use-clients"
import { useLanguage } from "@/i18n/use-language"

function ClientsView({ onNavigateToProject }: { onNavigateToProject?: (id: string) => void }) {
  const { t } = useLanguage()
  const [search, setSearch] = useState("")
  const debouncedSearch = useDebouncedValue(search, 300)
  const { clients, refetch } = useClients(debouncedSearch)
  const [selectedId, setSelectedId] = useState<number | null>(null)

  return (
    <div className="grid grid-cols-3 gap-4">
      <div className="col-span-1 flex flex-col gap-2 rounded-xl bg-card p-2 ring-1 ring-foreground/10">
        <div className="flex items-center gap-2 p-0.5">
          <Input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder={t("client.searchPlaceholder")}
            className="flex-1"
          />
          <NewClientDialog
            clients={clients}
            onCreated={async (id) => {
              await refetch()
              setSelectedId(id)
            }}
          />
        </div>

        {clients.length > 0 ? (
          <ul className="flex flex-col gap-0.5">
            {clients.map((c) => (
              <li key={c.id}>
                <button
                  type="button"
                  onClick={() => setSelectedId(c.id)}
                  className={`flex w-full items-center justify-between gap-2 rounded-md px-2 py-1.5 text-left text-sm hover:bg-accent ${
                    selectedId === c.id ? "bg-accent" : ""
                  }`}
                >
                  <span className="flex-1 truncate">{c.name}</span>
                  <span className="shrink-0 text-[12px] text-muted-foreground">
                    {c.contactCount} {t(c.contactCount === 1 ? "client.contactSingular" : "client.contactPlural")}
                  </span>
                </button>
              </li>
            ))}
          </ul>
        ) : (
          <Hint>{search ? t("client.noMatches") : t("client.emptyAll")}</Hint>
        )}
      </div>

      <div className="col-span-2 rounded-xl bg-card p-4 ring-1 ring-foreground/10">
        {selectedId ? (
          <ClientDetailPanel
            key={selectedId}
            id={selectedId}
            onClientsRefetch={refetch}
            onDeleted={() => setSelectedId(null)}
            onNavigateToProject={onNavigateToProject}
          />
        ) : (
          <Hint>{t("client.selectHint")}</Hint>
        )}
      </div>
    </div>
  )
}

// Okno "dynamiczne": wpisana/wybrana nazwa jest na bieżąco porównywana z katalogiem. Jeśli
// pasuje do JUŻ ISTNIEJĄCEGO klienta, okno przełącza się w tryb "dodaj temu klientowi nazwę
// 2" zamiast próbować założyć drugiego klienta o tej samej nazwie (co i tak odbiłoby się od
// UNIQUE na clients.name) — dokładnie to, co pozwala jednemu "Bosch" mieć wiele nazw 2
// zamiast wielu osobnych, niepowiązanych ze sobą wpisów "Bosch" w katalogu.
function NewClientDialog({
  clients,
  onCreated,
}: {
  clients: Client[]
  onCreated: (id: number) => void | Promise<void>
}) {
  const { t } = useLanguage()
  const [open, setOpen] = useState(false)
  const [name, setName] = useState("")
  const [name2, setName2] = useState("")
  const [error, setError] = useState("")
  const [pending, setPending] = useState(false)

  const trimmedName = name.trim()
  const matchedClient = trimmedName
    ? (clients.find((c) => c.name.toLowerCase() === trimmedName.toLowerCase()) ?? null)
    : null

  function reset() {
    setName("")
    setName2("")
    setError("")
  }

  async function submit() {
    if (!trimmedName) {
      setError(t("client.nameRequired"))
      return
    }
    setError("")
    setPending(true)
    try {
      if (matchedClient) {
        const trimmedName2 = name2.trim()
        if (!trimmedName2) {
          setError(t("client.name2Required"))
          return
        }
        await api.addClientName2(matchedClient.id, trimmedName2)
        setOpen(false)
        reset()
        await onCreated(matchedClient.id)
      } else {
        const { id } = await api.createClient({ name: trimmedName, location: null })
        setOpen(false)
        reset()
        await onCreated(id)
      }
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        setError(matchedClient ? t("client.name2Conflict") : t("client.nameConflict"))
      } else {
        setError(matchedClient ? t("client.addName2Failed") : t("client.addFailed"))
      }
    } finally {
      setPending(false)
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next)
        if (!next) reset()
      }}
    >
      <DialogTrigger render={<Button size="icon-sm" aria-label={t("client.addAria")}><Plus className="size-4" /></Button>} />
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{matchedClient ? t("client.addName2Title") : t("client.addTitle")}</DialogTitle>
        </DialogHeader>
        <div className="flex flex-col gap-2">
          <Label htmlFor="client-name">{t("common.name")}</Label>
          <Input
            id="client-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder={t("client.namePlaceholder")}
          />
          {matchedClient && (
            <>
              <Hint>{t("client.existingClientHint")}</Hint>
              <Label htmlFor="client-new-name2">{t("client.name2Label")}</Label>
              <Input
                id="client-new-name2"
                value={name2}
                onChange={(e) => setName2(e.target.value)}
                placeholder={t("client.name2Placeholder")}
              />
            </>
          )}
          <FormError>{error}</FormError>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={() => setOpen(false)}>
            {t("common.cancel")}
          </Button>
          <Button onClick={submit} disabled={pending}>
            {matchedClient ? t("client.addName2Button") : t("common.add")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export { ClientsView }
