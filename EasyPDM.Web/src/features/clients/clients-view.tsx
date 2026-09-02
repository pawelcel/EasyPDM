import { useState } from "react"
import { Plus } from "lucide-react"

import { api, ApiError } from "@/api/client"
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
                  <span className="flex-1 truncate">
                    {c.name}
                    {c.name2 && <span className="text-muted-foreground"> — {c.name2}</span>}
                  </span>
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

function NewClientDialog({ onCreated }: { onCreated: (id: number) => void | Promise<void> }) {
  const { t } = useLanguage()
  const [open, setOpen] = useState(false)
  const [name, setName] = useState("")
  const [error, setError] = useState("")

  function reset() {
    setName("")
    setError("")
  }

  async function submit() {
    const trimmed = name.trim()
    if (!trimmed) {
      setError(t("client.nameRequired"))
      return
    }
    setError("")
    try {
      const { id } = await api.createClient({ name: trimmed, name2: null, location: null })
      setOpen(false)
      reset()
      await onCreated(id)
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        setError(t("client.nameConflict"))
      } else {
        setError(t("client.addFailed"))
      }
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
          <DialogTitle>{t("client.addTitle")}</DialogTitle>
        </DialogHeader>
        <div className="flex flex-col gap-2">
          <Label htmlFor="client-name">{t("common.name")}</Label>
          <Input
            id="client-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder={t("client.namePlaceholder")}
          />
          <FormError>{error}</FormError>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={() => setOpen(false)}>
            {t("common.cancel")}
          </Button>
          <Button onClick={submit}>{t("common.add")}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export { ClientsView }
