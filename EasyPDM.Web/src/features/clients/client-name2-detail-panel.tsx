import { useEffect, useState } from "react"
import { Pencil, Trash2 } from "lucide-react"

import { api, ApiError } from "@/api/client"
import type { ClientContact, ClientName2Detail } from "@/api/types"
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

// Szczegóły JEDNEJ Nazwy 2 -- odpowiednik client-detail-panel.tsx, ale bez sekcji Projekty
// (poza zakresem: Nazwa 2 to wariant handlowy klienta, nie osobny byt spięty z projektami).
// Przycisk usunięcia jest tu, w tym samym miejscu co u klienta (nagłówek, po prawej) --
// zamiast przy wierszu w liście po lewej, żeby nie trzeba było wchodzić w szczegóły, żeby
// zobaczyć, co dokładnie się usuwa. Kluczowa różnica, powtórzona zarówno dla kontaktów jak
// i plików: DWIE osobne sekcje -- odziedziczone z klienta-rodzica (tylko do odczytu z tego
// poziomu) i własne tej Nazwy 2 (pełne CRUD). Np. różne normy w "Bosch Rexroth" niż w
// "Bosch Tabory" -- każda Nazwa 2 dostaje swoje własne pliki, niezależne od plików klienta
// i od siebie nawzajem.
function ClientName2DetailPanel({
  clientId,
  name2Id,
  onClientsRefetch,
  onDeleted,
}: {
  clientId: number
  name2Id: number
  onClientsRefetch: () => void | Promise<void>
  onDeleted: () => void
}) {
  const { t } = useLanguage()
  const [detail, setDetail] = useState<ClientName2Detail | null>(null)
  const [parentClientName, setParentClientName] = useState("")
  const [parentContacts, setParentContacts] = useState<ClientContact[]>([])
  const [name2, setName2] = useState("")
  const [location, setLocation] = useState("")
  const [nameError, setNameError] = useState("")
  const [confirmingDelete, setConfirmingDelete] = useState(false)
  const [deletingPending, setDeletingPending] = useState(false)
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [confirmingDeleteContactId, setConfirmingDeleteContactId] = useState<number | null>(null)
  const [contactDeletePending, setContactDeletePending] = useState(false)
  const [contactDeleteError, setContactDeleteError] = useState<string | null>(null)

  async function refetch() {
    const [detailData, parentData] = await Promise.all([
      api.getClientName2(clientId, name2Id),
      api.getClient(clientId),
    ])
    setDetail(detailData)
    setName2(detailData.name2)
    setLocation(detailData.location ?? "")
    setParentClientName(parentData.name)
    setParentContacts(parentData.contacts)
  }

  useEffect(() => {
    refetch()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [clientId, name2Id])

  async function save() {
    if (!detail) return
    const trimmedName2 = name2.trim()
    if (!trimmedName2) {
      setName2(detail.name2)
      return
    }
    const trimmedLocation = location.trim() || null
    if (trimmedName2 === detail.name2 && trimmedLocation === detail.location) {
      return
    }
    setNameError("")
    try {
      await api.updateClientName2(clientId, name2Id, { name2: trimmedName2, location: trimmedLocation })
      await refetch()
      await onClientsRefetch()
    } catch (err) {
      setName2(detail.name2)
      setLocation(detail.location ?? "")
      if (err instanceof ApiError && err.status === 409) {
        setNameError(t("client.name2Conflict"))
      } else {
        setNameError(t("client.saveName2Failed"))
      }
    }
  }

  async function confirmDelete() {
    setDeletingPending(true)
    setDeleteError(null)
    try {
      await api.removeClientName2(clientId, name2Id)
      setConfirmingDelete(false)
      await onClientsRefetch()
      onDeleted()
    } catch (err) {
      setDeleteError(err instanceof ApiError ? err.message : t("client.deleteName2Failed"))
    } finally {
      setDeletingPending(false)
    }
  }

  async function confirmRemoveContact() {
    if (confirmingDeleteContactId === null) return
    setContactDeletePending(true)
    setContactDeleteError(null)
    try {
      await api.removeClientName2Contact(clientId, name2Id, confirmingDeleteContactId)
      setConfirmingDeleteContactId(null)
      await refetch()
    } catch (err) {
      setContactDeleteError(err instanceof ApiError ? err.message : t("client.deleteContactFailed"))
    } finally {
      setContactDeletePending(false)
    }
  }

  if (!detail) return null

  const confirmingDeleteContact = detail.contacts.find((c) => c.id === confirmingDeleteContactId) ?? null

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-start justify-between gap-2">
        <div className="flex-1 flex flex-col gap-2">
          {/* Rodzic wypisany na samej górze -- w odróżnieniu od klienta ta Nazwa 2 nie
              istnieje samodzielnie, więc zanim cokolwiek innego, jasne jest, czyja to
              Nazwa 2. */}
          <div className="text-[12.5px] text-muted-foreground">
            {t("client.parentClientLabel")}: {parentClientName}
          </div>
          <Input
            value={name2}
            onChange={(e) => setName2(e.target.value)}
            onBlur={save}
            className="text-[15px] font-semibold"
            placeholder={t("client.name2Label")}
          />
          <div className="flex flex-col gap-1">
            <Label htmlFor="client-name2-location">{t("client.locationLabel")}</Label>
            <Input
              id="client-name2-location"
              value={location}
              onChange={(e) => setLocation(e.target.value)}
              onBlur={save}
              placeholder={t("client.locationPlaceholder")}
            />
          </div>
          <FormError>{nameError}</FormError>
        </div>
        <Button
          size="sm"
          variant="destructive"
          onClick={() => {
            setDeleteError(null)
            setConfirmingDelete(true)
          }}
        >
          {t("client.deleteName2Aria")}
        </Button>
      </div>

      {/* Odziedziczone z klienta-rodzica -- tylko do odczytu z tego poziomu (edycja i
          dodawanie nadal wyłącznie w panelu samego klienta). */}
      <div>
        <SectionLabel>{t("client.inheritedContactsLabel")}</SectionLabel>
        <Hint>{t("client.inheritedContactsHint")}</Hint>
        {parentContacts.length > 0 ? (
          <Table className="mt-1">
            <TableHeader>
              <TableRow>
                <TableHead>{t("common.fullName")}</TableHead>
                <TableHead>{t("common.position")}</TableHead>
                <TableHead>{t("common.phone")}</TableHead>
                <TableHead>{t("common.email")}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {parentContacts.map((c) => (
                <TableRow key={c.id}>
                  <TableCell>{[c.firstName, c.lastName].filter(Boolean).join(" ") || "-"}</TableCell>
                  <TableCell>{c.position || "-"}</TableCell>
                  <TableCell>{c.phone || "-"}</TableCell>
                  <TableCell>{c.email || "-"}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        ) : (
          <Hint>{t("common.noContacts")}</Hint>
        )}
      </div>

      {/* Własne kontakty TEJ Nazwy 2 -- pełne dodawanie/edycja/usuwanie, niewidoczne nigdzie
          indziej (ani u rodzica, ani u innych Nazw 2 tego samego klienta). */}
      <div>
        <div className="mb-1 flex items-center justify-between">
          <SectionLabel>{t("client.ownContactsLabel", { name2: detail.name2 })}</SectionLabel>
          <ContactDialog
            trigger={
              <Button size="sm" variant="secondary">
                {t("client.addContactButton")}
              </Button>
            }
            title={t("client.addContactTitle")}
            confirmLabel={t("common.add")}
            onSubmit={async (body) => {
              await api.addClientName2Contact(clientId, name2Id, body)
              await refetch()
            }}
          />
        </div>

        {detail.contacts.length > 0 ? (
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
              {detail.contacts.map((c) => (
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
                          await api.updateClientName2Contact(clientId, name2Id, c.id, body)
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

      {/* Pliki klienta -- odziedziczone, tylko do odczytu z tego poziomu (dodawanie i
          zarządzanie nadal wyłącznie w panelu samego klienta), tak samo jak kontakty
          klienta wyżej. */}
      <div>
        <SectionLabel>{t("client.inheritedFilesLabel")}</SectionLabel>
        <Hint>{t("client.inheritedFilesHint")}</Hint>
        <div className="mt-1">
          <ClientFileTree clientId={clientId} readOnly />
        </div>
      </div>

      {/* Własne pliki TEJ Nazwy 2 -- pełne dodawanie/organizowanie/usuwanie, niewidoczne
          nigdzie indziej (ani u rodzica, ani u innych Nazw 2 tego samego klienta). */}
      <div>
        <SectionLabel>{t("client.ownFilesLabel", { name2: detail.name2 })}</SectionLabel>
        <div className="mt-1 flex flex-col gap-3">
          <ClientFileSearch clientId={clientId} name2Id={name2Id} />
          <ClientFileTree clientId={clientId} name2Id={name2Id} />
        </div>
      </div>

      {confirmingDelete && (
        <ConfirmDialog
          open
          title={t("client.deleteName2Aria")}
          description={t("client.deleteName2ConfirmDescription", { name: detail.name2 })}
          confirmLabel={t("common.delete")}
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

export { ClientName2DetailPanel }
