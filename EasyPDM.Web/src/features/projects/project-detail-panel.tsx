import { useEffect, useState } from "react"
import { Info } from "lucide-react"

import { api } from "@/api/client"
import type { Client, ClientContact, Project } from "@/api/types"
import { Button } from "@/components/ui/button"
import {
  Combobox,
  ComboboxContent,
  ComboboxEmpty,
  ComboboxInput,
  ComboboxItem,
  ComboboxList,
} from "@/components/ui/combobox"
import { ConfirmDialog } from "@/components/ui/confirm-dialog"
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { FormError } from "@/components/ui/form-error"
import { Hint } from "@/components/ui/hint"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { SectionLabel } from "@/components/ui/section-label"
import { useClients } from "@/features/clients/use-clients"
import { DocumentationDialog } from "@/features/items/documentation-dialog"
import { useLanguage } from "@/i18n/use-language"

type ProjectForm = {
  name: string
  description: string
  clientId: number | null
  clientName2Id: number | null
  leadContactId: number | null
  closed: boolean
  startDate: string
  endDate: string
}

function formFromProject(project: Project): ProjectForm {
  return {
    name: project.name,
    description: project.description ?? "",
    clientId: project.clientId,
    clientName2Id: project.clientName2Id,
    leadContactId: project.leadContactId,
    closed: project.closed,
    startDate: project.startDate ?? "",
    endDate: project.endDate ?? "",
  }
}

function contactLabel(contact: ClientContact | undefined): string {
  if (!contact) return ""
  return [contact.firstName, contact.lastName].filter(Boolean).join(" ") || ""
}

function formsEqual(a: ProjectForm, b: ProjectForm): boolean {
  return (
    a.name === b.name &&
    a.description === b.description &&
    a.clientId === b.clientId &&
    a.clientName2Id === b.clientName2Id &&
    a.leadContactId === b.leadContactId &&
    a.closed === b.closed &&
    a.startDate === b.startDate &&
    a.endDate === b.endDate
  )
}

// Etykieta w wyszukiwarce klienta to zawsze sama nazwa główna -- Nazwa 2 (gdy klient ją ma
// i użytkownik chce wskazać konkretną) wybierana jest osobnym Comboboxem niżej.
function clientLabel(client: Client | undefined): string {
  return client?.name ?? ""
}

function ProjectDetailPanel({
  project,
  isAdmin,
  onUpdated,
  onDeleted,
  onNavigateToProject,
  hideActions = false,
}: {
  project: Project
  isAdmin: boolean
  onUpdated: () => void | Promise<void>
  onDeleted: () => void | Promise<void>
  onNavigateToProject?: () => void
  // Widok projektu (ProjectTreeView) pokazuje te same akcje w belce nad drzewem
  // (razem z akcjami zaznaczonego elementu) zamiast w tym panelu — tu renderowane są
  // tylko przy wywołaniu z "Cała baza" (item-list.tsx), gdzie osobnej belki nie ma.
  hideActions?: boolean
}) {
  const { t } = useLanguage()
  const { clients } = useClients("")
  const [form, setForm] = useState(() => formFromProject(project))
  const name2s = clients.find((c) => c.id === form.clientId)?.name2s ?? []
  const [error, setError] = useState("")
  const [confirmingDelete, setConfirmingDelete] = useState(false)
  const [deletingPending, setDeletingPending] = useState(false)
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [leadContacts, setLeadContacts] = useState<ClientContact[]>([])
  const [showingLeadContact, setShowingLeadContact] = useState(false)
  const selectedLeadContact = leadContacts.find((c) => c.id === form.leadContactId)

  // Odśwież formularz, gdy z zewnątrz przyjdą nowe dane projektu (np. po zapisie albo
  // przełączeniu na inny projekt) — nie ma osobnego trybu "edycji", pola są edytowalne
  // od razu (dla administratora), więc trzymamy je zsynchronizowane z danymi z serwera.
  useEffect(() => {
    setForm(formFromProject(project))
  }, [project])

  // Prowadzącego wybiera się z kontaktów TEGO klienta -- zarówno jego własnych (name2_id
  // IS NULL), jak i przypisanych do wybranej Nazwy 2 -- ten sam zestaw dwóch zapytań i to
  // samo łączenie po stronie frontu co w ClientName2DetailPanel (własne + odziedziczone).
  useEffect(() => {
    if (!form.clientId) {
      setLeadContacts([])
      return
    }
    let cancelled = false
    async function loadContacts(clientId: number, clientName2Id: number | null) {
      const [client, name2] = await Promise.all([
        api.getClient(clientId),
        clientName2Id ? api.getClientName2(clientId, clientName2Id) : Promise.resolve(null),
      ])
      if (cancelled) return
      setLeadContacts(name2 ? [...client.contacts, ...name2.contacts] : client.contacts)
    }
    loadContacts(form.clientId, form.clientName2Id)
    return () => {
      cancelled = true
    }
  }, [form.clientId, form.clientName2Id])

  // Nazwa/Opis/daty wywołują save() na KAŻDYM blur, niezależnie czy coś w nich faktycznie
  // się zmieniło (np. samo kliknięcie w pole i wyjście bez edycji) — bez tej strażniczej
  // funkcji taki "pusty" zapis wysyła CAŁY, wcześniejszy stan formularza (zob. `next`
  // budowane przez spread `form` w innych onValueChange), który może dogonić i nadpisać
  // W TLE świeższą zmianę zrobioną tuż po (np. wybór Prowadzącego zaraz po kliknięciu z
  // pola Nazwa) — dokładnie ten sam wyścig dwóch równoległych PATCH-ów, co przy przycisku
  // Zamknij/Otwórz projekt niżej, tylko odpalany przez zwykłe przejście fokusu między
  // polami zamiast klikiem w przycisk. Pomijając zapis, gdy formularz nie różni się od
  // ostatniego stanu z serwera, ten "widmowy" PATCH w ogóle nie powstaje.
  function saveIfChanged() {
    if (!formsEqual(form, formFromProject(project))) save(form)
  }

  async function save(next: ProjectForm) {
    if (!next.name.trim()) return
    setError("")
    try {
      await api.updateProject(project.id, {
        name: next.name.trim(),
        description: next.description.trim() || null,
        clientId: next.clientId,
        clientName2Id: next.clientName2Id,
        leadContactId: next.leadContactId,
        closed: next.closed,
        startDate: next.startDate || null,
        endDate: next.endDate || null,
      })
      await onUpdated()
    } catch (err) {
      setError(err instanceof Error ? err.message : t("project.saveFailed"))
    }
  }

  async function confirmDelete() {
    setDeletingPending(true)
    setDeleteError(null)
    try {
      await api.deleteProject(project.id)
      setConfirmingDelete(false)
      await onDeleted()
    } catch (err) {
      setDeleteError(err instanceof Error ? err.message : t("project.deleteFailed"))
    } finally {
      setDeletingPending(false)
    }
  }

  const created = new Date(project.createdAt).toLocaleString("pl-PL")

  return (
    <div>
      {!hideActions && (
        <div className="mb-3 flex flex-col gap-1.5 border-b pb-3">
          <div className="flex gap-1.5">
            {onNavigateToProject && (
              <Button size="sm" variant="outline" onClick={onNavigateToProject}>
                {t("project.goToProject")}
              </Button>
            )}
            <DocumentationDialog
              trigger={
                <Button size="sm" variant="outline">
                  {t("documentation.button")}
                </Button>
              }
              fetchExtensions={() => api.getProjectDocumentationExtensions(project.id)}
              buildDownloadUrl={(extensions) => api.projectDocumentationUrl(project.id, extensions)}
            />
            {isAdmin && (
              <Button
                size="sm"
                variant="destructive"
                onClick={() => {
                  setDeleteError(null)
                  setConfirmingDelete(true)
                }}
              >
                {t("project.deleteButton")}
              </Button>
            )}
          </div>
        </div>
      )}

      <div className="text-[15px] font-semibold">{project.name}</div>
      <div className="text-[12.5px] text-muted-foreground">
        {t("project.subtitle", {
          date: created,
          count: project.itemCount,
          itemWord: t(project.itemCount === 1 ? "project.itemSingular" : "project.itemPlural"),
        })}
      </div>
      {form.closed && <Hint>{t("project.closedHint")}</Hint>}
      {isAdmin && (
        // Poza blokiem !hideActions celowo -- w odróżnieniu od Usuń/Dokumentacja (które
        // ProjectTreeView pokazuje we własnej belce nad drzewem zamiast tutaj), ten przycisk
        // ma być widoczny zawsze, niezależnie skąd panel jest wywołany.
        <Button
          size="sm"
          variant="secondary"
          className="mt-1.5"
          // onMouseDown + preventDefault -- bez tego, klikanie tego przycisku podczas gdy
          // inne pole (np. Nazwa) ma fokus najpierw odpala JEGO onBlur (save ze STARĄ,
          // sprzed przełączenia wartością closed), a dopiero potem ten onClick (save z NOWĄ
          // wartością) -- dwa równoległe zapytania PATCH, których kolejność zakończenia nie
          // jest gwarantowana, więc czasem "wygrywa" to starsze i projekt natychmiast wraca
          // do poprzedniego stanu (potwierdzone w praktyce). preventDefault na mousedown nie
          // pozwala przeglądarce w ogóle przenieść fokusu (więc blur się nie odpala), bez
          // wpływu na sam onClick.
          onMouseDown={(e) => e.preventDefault()}
          onClick={() => {
            const next = { ...form, closed: !form.closed }
            setForm(next)
            save(next)
          }}
        >
          {form.closed ? t("project.reopenButton") : t("project.closeButton")}
        </Button>
      )}

      <SectionLabel>{t("item.properties")}</SectionLabel>
      <div className="flex flex-col gap-2">
        <Label htmlFor="project-name">{t("common.name")}</Label>
        <Input
          id="project-name"
          value={form.name}
          disabled={!isAdmin}
          onChange={(e) => setForm((f) => ({ ...f, name: e.target.value }))}
          onBlur={saveIfChanged}
        />

        <Label htmlFor="project-description">{t("project.description")}</Label>
        {isAdmin ? (
          <Input
            id="project-description"
            value={form.description}
            onChange={(e) => setForm((f) => ({ ...f, description: e.target.value }))}
            onBlur={saveIfChanged}
            placeholder={t("project.noDescription")}
          />
        ) : project.description ? (
          <p className="text-sm whitespace-pre-wrap">{project.description}</p>
        ) : (
          <Hint>{t("project.noDescription")}</Hint>
        )}

        <Label htmlFor="project-client">{t("project.client")}</Label>
        {clients.length > 0 ? (
          <Combobox
            items={clients.map((c) => c.id)}
            value={form.clientId}
            onValueChange={(v) => {
              const next = { ...form, clientId: (v as number | null) ?? null, clientName2Id: null, leadContactId: null }
              setForm(next)
              save(next)
            }}
            itemToStringLabel={(id: number) => clientLabel(clients.find((c) => c.id === id))}
            disabled={!isAdmin}
          >
            <ComboboxInput id="project-client" placeholder={t("part.searchPlaceholder")} showClear />
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
        {!form.clientId && project.client && (
          <Hint>{t("project.legacyClientValue", { value: project.client })}</Hint>
        )}

        <Label htmlFor="project-client-name2">{t("project.clientName2")}</Label>
        <Combobox
          items={name2s.map((n) => n.id)}
          value={form.clientName2Id}
          onValueChange={(v) => {
            const next = { ...form, clientName2Id: (v as number | null) ?? null, leadContactId: null }
            setForm(next)
            save(next)
          }}
          itemToStringLabel={(id: number) => name2s.find((n) => n.id === id)?.name2 ?? ""}
          disabled={!isAdmin || !form.clientId}
        >
          <ComboboxInput
            id="project-client-name2"
            placeholder={t("part.searchPlaceholder")}
            showClear
            disabled={!isAdmin || !form.clientId}
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

        <Label htmlFor="project-lead-contact">{t("project.leadContact")}</Label>
        {form.clientId && leadContacts.length > 0 ? (
          <div className="flex items-center gap-1.5">
            <div className="flex-1">
              <Combobox
                items={leadContacts.map((c) => c.id)}
                value={form.leadContactId}
                onValueChange={(v) => {
                  const next = { ...form, leadContactId: (v as number | null) ?? null }
                  setForm(next)
                  save(next)
                }}
                itemToStringLabel={(id: number) => contactLabel(leadContacts.find((c) => c.id === id))}
                disabled={!isAdmin}
              >
                <ComboboxInput id="project-lead-contact" placeholder={t("part.searchPlaceholder")} showClear />
                <ComboboxContent>
                  <ComboboxEmpty>{t("project.noMatchingLeadContact")}</ComboboxEmpty>
                  <ComboboxList>
                    {(id: number) => (
                      <ComboboxItem key={id} value={id}>
                        {contactLabel(leadContacts.find((c) => c.id === id))}
                      </ComboboxItem>
                    )}
                  </ComboboxList>
                </ComboboxContent>
              </Combobox>
            </div>
            {selectedLeadContact && (
              <Button
                type="button"
                size="icon"
                variant="outline"
                aria-label={t("project.leadContactDetailsAria")}
                onClick={() => setShowingLeadContact(true)}
              >
                <Info className="size-4" />
              </Button>
            )}
          </div>
        ) : (
          <Hint>{form.clientId ? t("project.noLeadContactsHint") : t("project.leadContactNeedsClientHint")}</Hint>
        )}

        {showingLeadContact && selectedLeadContact && (
          <Dialog open onOpenChange={setShowingLeadContact}>
            <DialogContent>
              <DialogHeader>
                <DialogTitle>{contactLabel(selectedLeadContact)}</DialogTitle>
              </DialogHeader>
              <div className="flex flex-col gap-2 text-sm">
                <div className="flex justify-between gap-4">
                  <span className="text-muted-foreground">{t("common.position")}</span>
                  <span>{selectedLeadContact.position || "-"}</span>
                </div>
                <div className="flex justify-between gap-4">
                  <span className="text-muted-foreground">{t("common.phone")}</span>
                  <span>{selectedLeadContact.phone || "-"}</span>
                </div>
                <div className="flex justify-between gap-4">
                  <span className="text-muted-foreground">{t("common.email")}</span>
                  <span>{selectedLeadContact.email || "-"}</span>
                </div>
                <div className="flex justify-between gap-4">
                  <span className="text-muted-foreground">{t("common.address")}</span>
                  <span>{selectedLeadContact.address || "-"}</span>
                </div>
              </div>
            </DialogContent>
          </Dialog>
        )}

        <div className="flex gap-2">
          <div className="flex flex-1 flex-col gap-2">
            <Label htmlFor="project-start-date">{t("project.startDate")}</Label>
            <Input
              id="project-start-date"
              type="date"
              value={form.startDate}
              disabled={!isAdmin}
              onChange={(e) => setForm((f) => ({ ...f, startDate: e.target.value }))}
              onBlur={saveIfChanged}
            />
          </div>
          <div className="flex flex-1 flex-col gap-2">
            <Label htmlFor="project-end-date">{t("project.endDate")}</Label>
            <Input
              id="project-end-date"
              type="date"
              value={form.endDate}
              disabled={!isAdmin}
              onChange={(e) => setForm((f) => ({ ...f, endDate: e.target.value }))}
              onBlur={saveIfChanged}
            />
          </div>
        </div>

        <FormError>{error}</FormError>
      </div>

      {!hideActions && confirmingDelete && (
        <ConfirmDialog
          open
          title={t("project.deleteButton")}
          description={t("project.deleteConfirmDescription", {
            name: project.name,
            count: project.itemCount,
          })}
          confirmLabel={t("project.deleteButton")}
          variant="destructive"
          onConfirm={confirmDelete}
          onCancel={() => setConfirmingDelete(false)}
          pending={deletingPending}
          error={deleteError}
        />
      )}
    </div>
  )
}

export { ProjectDetailPanel }
