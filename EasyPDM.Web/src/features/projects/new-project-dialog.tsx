import { useState } from "react"

import { api, ApiError } from "@/api/client"
import type { Client, Project } from "@/api/types"
import { Button } from "@/components/ui/button"
import {
  Combobox,
  ComboboxContent,
  ComboboxEmpty,
  ComboboxInput,
  ComboboxItem,
  ComboboxList,
} from "@/components/ui/combobox"
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
import { useClients } from "@/features/clients/use-clients"
import { useLanguage } from "@/i18n/use-language"

// Projekt łączy się z Klientem jako całością (nie z konkretną Nazwą 2 -- może ich mieć
// kilka, zob. ClientName2), więc etykieta w wyszukiwarce to zawsze sama nazwa główna.
function clientLabel(client: Client | undefined): string {
  return client?.name ?? ""
}

function NewProjectDialog({ onCreated }: { onCreated: (project: Project) => void }) {
  const { t } = useLanguage()
  const { clients } = useClients("")
  const [open, setOpen] = useState(false)
  const [name, setName] = useState("")
  const [description, setDescription] = useState("")
  const [clientId, setClientId] = useState<number | null>(null)
  const [startDate, setStartDate] = useState("")
  const [endDate, setEndDate] = useState("")
  const [error, setError] = useState("")
  const [submitting, setSubmitting] = useState(false)

  function reset() {
    setName("")
    setDescription("")
    setClientId(null)
    setStartDate("")
    setEndDate("")
    setError("")
  }

  async function handleCreate() {
    const trimmed = name.trim()
    if (!trimmed) {
      setError(t("project.newNameRequired"))
      return
    }

    setSubmitting(true)
    setError("")
    try {
      const project = await api.createProject({
        name: trimmed,
        description: description.trim() || null,
        clientId,
        startDate: startDate || null,
        endDate: endDate || null,
      })
      setOpen(false)
      reset()
      onCreated(project)
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        setError(t("project.nameConflict"))
      } else {
        setError(t("project.createFailed"))
      }
    } finally {
      setSubmitting(false)
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
      <DialogTrigger render={<Button>{t("project.newButton")}</Button>} />
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("project.newTitle")}</DialogTitle>
        </DialogHeader>

        <div className="flex flex-col gap-2">
          <Label htmlFor="new-project-name">{t("project.nameLabel")}</Label>
          <Input
            id="new-project-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder={t("project.nameLabel")}
          />
          <Label htmlFor="new-project-desc">{t("project.descriptionOptional")}</Label>
          <Input
            id="new-project-desc"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder={t("project.descriptionOptional")}
          />
          <Label htmlFor="new-project-client">{t("project.clientOptional")}</Label>
          {clients.length > 0 ? (
            <Combobox
              items={clients.map((c) => c.id)}
              value={clientId}
              onValueChange={(v) => setClientId((v as number | null) ?? null)}
              itemToStringLabel={(id: number) => clientLabel(clients.find((c) => c.id === id))}
            >
              <ComboboxInput id="new-project-client" placeholder={t("part.searchPlaceholder")} showClear />
              <ComboboxContent>
                <ComboboxEmpty>{t("client.noMatchingClients")}</ComboboxEmpty>
                <ComboboxList>
                  {(id: number) => (
                    <ComboboxItem key={id} value={id}>
                      {clientLabel(clients.find((c) => c.id === id))}
                    </ComboboxItem>
                  )}
                </ComboboxList>
              </ComboboxContent>
            </Combobox>
          ) : (
            <Hint>{t("project.noClientsHint")}</Hint>
          )}
          <div className="flex gap-2">
            <div className="flex flex-1 flex-col gap-2">
              <Label htmlFor="new-project-start">{t("project.startDate")}</Label>
              <Input
                id="new-project-start"
                type="date"
                value={startDate}
                onChange={(e) => setStartDate(e.target.value)}
              />
            </div>
            <div className="flex flex-1 flex-col gap-2">
              <Label htmlFor="new-project-end">{t("project.endDate")}</Label>
              <Input
                id="new-project-end"
                type="date"
                value={endDate}
                onChange={(e) => setEndDate(e.target.value)}
              />
            </div>
          </div>
          <FormError>{error}</FormError>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => setOpen(false)}>
            {t("common.cancel")}
          </Button>
          <Button onClick={handleCreate} disabled={submitting}>
            {t("project.createButton")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export { NewProjectDialog }
