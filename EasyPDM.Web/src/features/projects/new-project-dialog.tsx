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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { useClients } from "@/features/clients/use-clients"
import { useProjectLanguages } from "@/features/project-languages/use-project-languages"
import { useLanguage } from "@/i18n/use-language"

// Etykieta w wyszukiwarce klienta to zawsze sama nazwa główna -- Nazwa 2 (gdy klient ją ma
// i użytkownik chce wskazać konkretną) wybierana jest osobnym Comboboxem niżej.
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
  const [clientName2Id, setClientName2Id] = useState<number | null>(null)
  const [startDate, setStartDate] = useState("")
  const [endDate, setEndDate] = useState("")
  const [error, setError] = useState("")
  const [submitting, setSubmitting] = useState(false)
  // Główny język projektu — od początku, a nie dopiero we właściwościach po utworzeniu, bo w nim
  // wpisuje się nazwy pierwszych elementów. null = domyślny język dokumentacji z ustawień.
  const { languages } = useProjectLanguages()
  const [mainLanguageId, setMainLanguageId] = useState<number | null>(null)
  const defaultLanguage = languages.find((l) => l.isDefault)
  const chosenLanguageId = mainLanguageId ?? defaultLanguage?.id ?? null

  const name2s = clients.find((c) => c.id === clientId)?.name2s ?? []

  function reset() {
    setName("")
    setDescription("")
    setClientId(null)
    setClientName2Id(null)
    setStartDate("")
    setEndDate("")
    setMainLanguageId(null)
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
        clientName2Id,
        leadContactId: null,
        closed: false,
        startDate: startDate || null,
        endDate: endDate || null,
      })
      // Projekt powstaje z domyślnym językiem (wartość domyślna kolumny); inny wybór ustawia się
      // tym samym zapisem co we właściwościach projektu.
      let created = project
      if (chosenLanguageId !== null && chosenLanguageId !== project.mainLanguageId) {
        await api.setProjectLanguages(project.id, { mainLanguageId: chosenLanguageId, multilingual: false, languageIds: [] })
        created = { ...project, mainLanguageId: chosenLanguageId }
      }
      setOpen(false)
      reset()
      onCreated(created)
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
              onValueChange={(v) => {
                setClientId((v as number | null) ?? null)
                setClientName2Id(null)
              }}
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
          <Label htmlFor="new-project-client-name2">{t("project.clientName2")}</Label>
          <Combobox
            items={name2s.map((n) => n.id)}
            value={clientName2Id}
            onValueChange={(v) => setClientName2Id((v as number | null) ?? null)}
            itemToStringLabel={(id: number) => name2s.find((n) => n.id === id)?.name2 ?? ""}
            disabled={!clientId}
          >
            <ComboboxInput
              id="new-project-client-name2"
              placeholder={t("part.searchPlaceholder")}
              showClear
              disabled={!clientId}
            />
            <ComboboxContent>
              <ComboboxEmpty>{t("project.noMatchingClientName2")}</ComboboxEmpty>
              <ComboboxList>
                {(id: number) => (
                  <ComboboxItem key={id} value={id}>
                    {name2s.find((n) => n.id === id)?.name2 ?? ""}
                  </ComboboxItem>
                )}
              </ComboboxList>
            </ComboboxContent>
          </Combobox>
          {languages.length > 0 && (
            <>
              <Label htmlFor="new-project-language">{t("project.mainLanguage")}</Label>
              <Select
                value={chosenLanguageId !== null ? String(chosenLanguageId) : ""}
                onValueChange={(v) => v && setMainLanguageId(Number(v))}
              >
                <SelectTrigger id="new-project-language" className="w-full">
                  <SelectValue>
                    {(v: string) => {
                      const lang = languages.find((l) => String(l.id) === v)
                      return lang ? `${lang.name} (${lang.code})` : ""
                    }}
                  </SelectValue>
                </SelectTrigger>
                <SelectContent>
                  {languages.map((lang) => (
                    <SelectItem key={lang.id} value={String(lang.id)}>
                      {lang.name} ({lang.code})
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </>
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
