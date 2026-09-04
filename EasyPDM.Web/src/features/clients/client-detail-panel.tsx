import { useEffect, useState, type ReactElement } from "react"
import { ArrowUpRight, Pencil, Trash2 } from "lucide-react"

import { api, ApiError } from "@/api/client"
import type { ClientContact, ClientDetail } from "@/api/types"
import { Button } from "@/components/ui/button"
import { ConfirmDialog } from "@/components/ui/confirm-dialog"
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
import { SectionLabel } from "@/components/ui/section-label"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { ClientFileSearch } from "@/features/clients/client-file-search"
import { ClientFileTree } from "@/features/clients/client-file-tree"
import { useLanguage } from "@/i18n/use-language"

function ClientDetailPanel({
  id,
  onClientsRefetch,
  onDeleted,
  onNavigateToProject,
}: {
  id: number
  onClientsRefetch: () => void | Promise<void>
  onDeleted: () => void
  onNavigateToProject?: (id: string) => void
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
  const [name2Input, setName2Input] = useState("")
  const [addName2Pending, setAddName2Pending] = useState(false)
  const [addName2Error, setAddName2Error] = useState("")
  const [confirmingDeleteName2Id, setConfirmingDeleteName2Id] = useState<number | null>(null)
  const [name2DeletePending, setName2DeletePending] = useState(false)
  const [name2DeleteError, setName2DeleteError] = useState<string | null>(null)

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

  async function addName2() {
    const trimmed = name2Input.trim()
    if (!trimmed) {
      setAddName2Error(t("client.name2Required"))
      return
    }
    setAddName2Pending(true)
    setAddName2Error("")
    try {
      await api.addClientName2(id, trimmed)
      setName2Input("")
      await refetch()
    } catch (err) {
      setAddName2Error(
        err instanceof ApiError && err.status === 409 ? t("client.name2Conflict") : t("client.addName2Failed")
      )
    } finally {
      setAddName2Pending(false)
    }
  }

  async function confirmRemoveName2() {
    if (confirmingDeleteName2Id === null) return
    setName2DeletePending(true)
    setName2DeleteError(null)
    try {
      await api.removeClientName2(id, confirmingDeleteName2Id)
      setConfirmingDeleteName2Id(null)
      await refetch()
    } catch (err) {
      setName2DeleteError(err instanceof ApiError ? err.message : t("client.deleteName2Failed"))
    } finally {
      setName2DeletePending(false)
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
  const confirmingDeleteName2 = client.name2s.find((n) => n.id === confirmingDeleteName2Id) ?? null

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

      {/* Nazwy 2 -- podpowiedzi do wyboru przy elemencie Klienta (zob. ClientAndName2Fields).
          Jeden klient może mieć kilka (np. różne spółki-córki) -- stąd lista z osobnym
          dodawaniem/usuwaniem zamiast pojedynczego pola, tak samo jak Serie/Typy producenta. */}
      <div>
        <SectionLabel>{t("client.name2sLabel")}</SectionLabel>

        <div className="mt-1 flex items-start gap-1.5">
          <Input
            value={name2Input}
            onChange={(e) => setName2Input(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") addName2()
            }}
            placeholder={t("client.name2Placeholder")}
            className="min-w-0 flex-1"
          />
          <Button variant="secondary" onClick={addName2} disabled={addName2Pending}>
            {t("common.add")}
          </Button>
        </div>
        <FormError>{addName2Error}</FormError>

        {client.name2s.length > 0 ? (
          <Table className="mt-2">
            <TableBody>
              {client.name2s.map((n) => (
                <TableRow key={n.id}>
                  <TableCell>{n.name2}</TableCell>
                  <TableCell className="w-10">
                    <Button
                      size="icon-xs"
                      variant="ghost"
                      aria-label={t("client.deleteName2Aria")}
                      onClick={() => setConfirmingDeleteName2Id(n.id)}
                    >
                      <Trash2 className="size-3.5 text-muted-foreground" />
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        ) : (
          <Hint>{t("client.noName2s")}</Hint>
        )}
      </div>

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

      {confirmingDeleteName2 && (
        <ConfirmDialog
          open
          title={t("client.deleteName2Aria")}
          description={t("client.deleteName2ConfirmDescription", { name: confirmingDeleteName2.name2 })}
          confirmLabel={t("common.delete")}
          variant="destructive"
          onConfirm={confirmRemoveName2}
          onCancel={() => setConfirmingDeleteName2Id(null)}
          pending={name2DeletePending}
          error={name2DeleteError}
        />
      )}
    </div>
  )
}

type ContactFormBody = {
  firstName: string | null
  lastName: string | null
  phone: string | null
  position: string | null
  email: string | null
  address: string | null
}

function ContactDialog({
  trigger,
  title,
  confirmLabel,
  initial,
  onSubmit,
}: {
  trigger: ReactElement
  title: string
  confirmLabel: string
  initial?: ClientContact
  onSubmit: (body: ContactFormBody) => Promise<void>
}) {
  const { t } = useLanguage()
  const [open, setOpen] = useState(false)
  const [firstName, setFirstName] = useState(initial?.firstName ?? "")
  const [lastName, setLastName] = useState(initial?.lastName ?? "")
  const [phone, setPhone] = useState(initial?.phone ?? "")
  const [position, setPosition] = useState(initial?.position ?? "")
  const [email, setEmail] = useState(initial?.email ?? "")
  const [address, setAddress] = useState(initial?.address ?? "")
  const [error, setError] = useState("")

  function reset() {
    setFirstName(initial?.firstName ?? "")
    setLastName(initial?.lastName ?? "")
    setPhone(initial?.phone ?? "")
    setPosition(initial?.position ?? "")
    setEmail(initial?.email ?? "")
    setAddress(initial?.address ?? "")
    setError("")
  }

  async function submit() {
    setError("")
    try {
      await onSubmit({
        firstName: firstName.trim() || null,
        lastName: lastName.trim() || null,
        phone: phone.trim() || null,
        position: position.trim() || null,
        email: email.trim() || null,
        address: address.trim() || null,
      })
      setOpen(false)
      reset()
    } catch {
      setError(t("client.saveContactFailed"))
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
      <DialogTrigger render={trigger} />
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
        </DialogHeader>

        <div className="flex flex-col gap-2">
          <div className="flex gap-2">
            <div className="flex flex-1 flex-col gap-2">
              <Label htmlFor="client-contact-first-name">{t("common.firstName")}</Label>
              <Input id="client-contact-first-name" value={firstName} onChange={(e) => setFirstName(e.target.value)} />
            </div>
            <div className="flex flex-1 flex-col gap-2">
              <Label htmlFor="client-contact-last-name">{t("common.lastName")}</Label>
              <Input id="client-contact-last-name" value={lastName} onChange={(e) => setLastName(e.target.value)} />
            </div>
          </div>

          <Label htmlFor="client-contact-position">{t("common.position")}</Label>
          <Input id="client-contact-position" value={position} onChange={(e) => setPosition(e.target.value)} />

          <Label htmlFor="client-contact-phone">{t("common.phoneNumber")}</Label>
          <Input id="client-contact-phone" value={phone} onChange={(e) => setPhone(e.target.value)} />

          <Label htmlFor="client-contact-email">{t("common.email")}</Label>
          <Input id="client-contact-email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} />

          <Label htmlFor="client-contact-address">{t("common.address")}</Label>
          <Input id="client-contact-address" value={address} onChange={(e) => setAddress(e.target.value)} />

          <FormError>{error}</FormError>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => setOpen(false)}>
            {t("common.cancel")}
          </Button>
          <Button onClick={submit}>{confirmLabel}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export { ClientDetailPanel }
