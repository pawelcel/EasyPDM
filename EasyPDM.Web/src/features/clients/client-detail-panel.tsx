import { useEffect, useState } from "react"
import { ArrowUpRight, Pencil, Trash2 } from "lucide-react"

import { api, ApiError } from "@/api/client"
import type { ClientDetail } from "@/api/types"
import { Button } from "@/components/ui/button"
import { ConfirmDialog } from "@/components/ui/confirm-dialog"
import { FormError } from "@/components/ui/form-error"
import { Hint } from "@/components/ui/hint"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { SectionLabel } from "@/components/ui/section-label"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { ContactDialog } from "@/features/clients/client-contact-dialog"
import { ClientFileSearch } from "@/features/clients/client-file-search"
import { ClientFileTree } from "@/features/clients/client-file-tree"
import { useLanguage } from "@/i18n/use-language"

function ClientDetailPanel({
  id,
  onClientsRefetch,
  onDeleted,
  onNavigateToProject,
  onAddName2,
}: {
  id: number
  onClientsRefetch: () => void | Promise<void>
  onDeleted: () => void
  onNavigateToProject?: (id: string) => void
  onAddName2: (client: Pick<ClientDetail, "id" | "name">) => void
}) {
  const { t } = useLanguage()
  const [client, setClient] = useState<ClientDetail | null>(null)
  const [name, setName] = useState("")
  const [location, setLocation] = useState("")
  const [nameError, setNameError] = useState("")
  const [confirmingDelete, setConfirmingDelete] = useState(false)
  const [deletingPending, setDeletingPending] = useState(false)
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [confirmingDeleteContactId, setConfirmingDeleteContactId] = useState<number | null>(null)
  const [contactDeletePending, setContactDeletePending] = useState(false)
  const [contactDeleteError, setContactDeleteError] = useState<string | null>(null)

  async function refetch() {
    const data = await api.getClient(id)
    setClient(data)
    setName(data.name)
    setLocation(data.location ?? "")
  }

  useEffect(() => {
    refetch()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id])

  async function save() {
    if (!client) return
    const trimmedName = name.trim()
    if (!trimmedName) {
      setName(client.name)
      return
    }
    const trimmedLocation = location.trim() || null
    if (trimmedName === client.name && trimmedLocation === client.location) {
      return
    }
    setNameError("")
    try {
      await api.updateClient(id, { name: trimmedName, location: trimmedLocation })
      await refetch()
      await onClientsRefetch()
    } catch (err) {
      setName(client.name)
      setLocation(client.location ?? "")
      if (err instanceof ApiError && err.status === 409) {
        setNameError(t("client.nameConflict"))
      } else {
        setNameError(t("client.saveNameFailed"))
      }
    }
  }

  async function confirmDelete() {
    setDeletingPending(true)
    setDeleteError(null)
    try {
      await api.removeClient(id)
      setConfirmingDelete(false)
      await onClientsRefetch()
      onDeleted()
    } catch (err) {
      setDeleteError(err instanceof ApiError ? err.message : t("client.deleteFailed"))
    } finally {
      setDeletingPending(false)
    }
  }

  async function confirmRemoveContact() {
    if (confirmingDeleteContactId === null) return
    setContactDeletePending(true)
    setContactDeleteError(null)
    try {
      await api.removeClientContact(id, confirmingDeleteContactId)
      setConfirmingDeleteContactId(null)
      await refetch()
      await onClientsRefetch()
    } catch (err) {
      setContactDeleteError(err instanceof ApiError ? err.message : t("client.deleteContactFailed"))
    } finally {
      setContactDeletePending(false)
    }
  }

  if (!client) return null

  const confirmingDeleteContact = client.contacts.find((c) => c.id === confirmingDeleteContactId) ?? null

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-start justify-between gap-2">
        <div className="flex-1 flex flex-col gap-2">
          <Input
            value={name}
            onChange={(e) => setName(e.target.value)}
            onBlur={save}
            className="text-[15px] font-semibold"
            placeholder={t("common.name")}
          />
          <div className="flex-1 flex flex-col gap-1">
            <Label htmlFor="client-location">{t("client.locationLabel")}</Label>
            <Input
              id="client-location"
              value={location}
              onChange={(e) => setLocation(e.target.value)}
              onBlur={save}
              placeholder={t("client.locationPlaceholder")}
            />
          </div>
          <FormError>{nameError}</FormError>
        </div>
        <div className="flex items-center gap-1.5">
          <Button size="sm" variant="secondary" onClick={() => onAddName2(client)}>
            {t("client.addName2Button")}
          </Button>
          <Button
            size="sm"
            variant="destructive"
            onClick={() => {
              setDeleteError(null)
              setConfirmingDelete(true)
            }}
          >
            {t("client.deleteButton")}
          </Button>
        </div>
      </div>

      <div>
        <SectionLabel>{t("client.projectsLabel")}</SectionLabel>
        {client.projects.length > 0 ? (
          <ul className="mt-1 flex max-h-[7.5rem] flex-col gap-1 overflow-y-auto">
            {client.projects.map((p) => (
              <li key={p.id} className="flex items-center gap-2 text-[12.5px]">
                <span className="min-w-0 flex-1 truncate">{p.name}</span>
                {onNavigateToProject && (
                  <Button
                    type="button"
                    size="icon-xs"
                    onClick={() => onNavigateToProject(p.id)}
                    aria-label={t("client.goToProjectAria")}
                    title={t("client.goToProjectAria")}
                  >
                    <ArrowUpRight />
                  </Button>
                )}
              </li>
            ))}
          </ul>
        ) : (
          <Hint>{t("client.noProjects")}</Hint>
        )}
      </div>

      <div>
        <div className="mb-1 flex items-center justify-between">
          <SectionLabel>{t("client.contactsLabel")}</SectionLabel>
          <ContactDialog
            trigger={
              <Button size="sm" variant="secondary">
                {t("client.addContactButton")}
              </Button>
            }
            title={t("client.addContactTitle")}
            confirmLabel={t("common.add")}
            onSubmit={async (body) => {
              await api.addClientContact(id, body)
              await refetch()
              await onClientsRefetch()
            }}
          />
        </div>

        {client.contacts.length > 0 ? (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>{t("common.fullName")}</TableHead>
                <TableHead>{t("common.position")}</TableHead>
                <TableHead>{t("common.phone")}</TableHead>
                <TableHead>{t("common.email")}</TableHead>
                <TableHead className="w-16" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {client.contacts.map((c) => (
                <TableRow key={c.id}>
                  <TableCell>{[c.firstName, c.lastName].filter(Boolean).join(" ") || "-"}</TableCell>
                  <TableCell>{c.position || "-"}</TableCell>
                  <TableCell>{c.phone || "-"}</TableCell>
                  <TableCell>{c.email || "-"}</TableCell>
                  <TableCell>
                    <div className="flex items-center gap-0.5">
                      <ContactDialog
                        trigger={
                          <Button size="icon-xs" variant="ghost" aria-label={t("client.editContactAria")}>
                            <Pencil className="size-3.5 text-muted-foreground" />
                          </Button>
                        }
                        title={t("client.editContactTitle")}
                        confirmLabel={t("common.save")}
                        initial={c}
                        onSubmit={async (body) => {
                          await api.updateClientContact(id, c.id, body)
                          await refetch()
                        }}
                      />
                      <Button
                        size="icon-xs"
                        variant="ghost"
                        aria-label={t("client.deleteContactAria")}
                        onClick={() => setConfirmingDeleteContactId(c.id)}
                      >
                        <Trash2 className="size-3.5 text-muted-foreground" />
                      </Button>
                    </div>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        ) : (
          <Hint>{t("common.noContacts")}</Hint>
        )}
      </div>

      {/* Dodanie drugiej nazwy stąd (przycisk w nagłówku wyżej) otwiera to samo okno co
          "+" po najechaniu na wiersz klienta w liście po lewej (QuickAddName2Dialog w
          clients-view.tsx) -- ten panel tylko przekazuje żądanie w górę, bo okno jest
          wspólne dla obu miejsc. Usunięcie zostaje we własnym panelu szczegółów danej
          Nazwy 2 (client-name2-detail-panel.tsx), nie tutaj. */}

      <div>
        <SectionLabel>{t("client.filesLabel")}</SectionLabel>
        <div className="mt-1 flex flex-col gap-3">
          <ClientFileSearch clientId={id} />
          <ClientFileTree clientId={id} />
        </div>
      </div>

      {confirmingDelete && (
        <ConfirmDialog
          open
          title={t("client.deleteButton")}
          description={t("client.deleteConfirmDescription", {
            name: client.name,
            count: client.contacts.length,
          })}
          confirmLabel={t("client.deleteButton")}
          variant="destructive"
          onConfirm={confirmDelete}
          onCancel={() => setConfirmingDelete(false)}
          pending={deletingPending}
          error={deleteError}
        />
      )}

      {confirmingDeleteContact && (
        <ConfirmDialog
          open
          title={t("client.deleteContactAria")}
          description={t("client.deleteContactConfirmDescription", {
            name: [confirmingDeleteContact.firstName, confirmingDeleteContact.lastName].filter(Boolean).join(" ") || "-",
          })}
          confirmLabel={t("common.delete")}
          variant="destructive"
          onConfirm={confirmRemoveContact}
          onCancel={() => setConfirmingDeleteContactId(null)}
          pending={contactDeletePending}
          error={contactDeleteError}
        />
      )}
    </div>
  )
}

export { ClientDetailPanel }
