import { useEffect, useState, type ReactElement } from "react"
import { Pencil, Plus, Trash2 } from "lucide-react"

import { api, ApiError } from "@/api/client"
import type { ManufacturerContact, ManufacturerDetail } from "@/api/types"
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
import { useDebouncedValue } from "@/hooks/use-debounced-value"
import { useManufacturers } from "@/features/manufacturers/use-manufacturers"

function ManufacturersView() {
  const [search, setSearch] = useState("")
  const debouncedSearch = useDebouncedValue(search, 300)
  const { manufacturers, refetch } = useManufacturers(debouncedSearch)
  const [selectedId, setSelectedId] = useState<number | null>(null)

  return (
    <div className="grid grid-cols-3 gap-4">
      <div className="col-span-1 flex flex-col gap-2 rounded-xl bg-card p-2 ring-1 ring-foreground/10">
        <div className="flex items-center gap-2 p-0.5">
          <Input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Szukaj producenta…"
            className="flex-1"
          />
          <NewManufacturerDialog
            onCreated={async (id) => {
              await refetch()
              setSelectedId(id)
            }}
          />
        </div>

        {manufacturers.length > 0 ? (
          <ul className="flex flex-col gap-0.5">
            {manufacturers.map((m) => (
              <li key={m.id}>
                <button
                  type="button"
                  onClick={() => setSelectedId(m.id)}
                  className={`flex w-full items-center justify-between gap-2 rounded-md px-2 py-1.5 text-left text-sm hover:bg-accent ${
                    selectedId === m.id ? "bg-accent" : ""
                  }`}
                >
                  <span className="truncate">{m.name}</span>
                  <span className="shrink-0 text-[12px] text-muted-foreground">
                    {m.contactCount} {m.contactCount === 1 ? "kontakt" : "kontaktów"}
                  </span>
                </button>
              </li>
            ))}
          </ul>
        ) : (
          <Hint>{search ? "Brak pasujących producentów." : "Brak producentów — dodaj pierwszego."}</Hint>
        )}
      </div>

      <div className="col-span-2 rounded-xl bg-card p-4 ring-1 ring-foreground/10">
        {selectedId ? (
          <ManufacturerDetailPanel
            key={selectedId}
            id={selectedId}
            onManufacturersRefetch={refetch}
            onDeleted={() => setSelectedId(null)}
          />
        ) : (
          <Hint>Wybierz producenta z listy po lewej albo dodaj nowego.</Hint>
        )}
      </div>
    </div>
  )
}

function NewManufacturerDialog({ onCreated }: { onCreated: (id: number) => void | Promise<void> }) {
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
      setError("Nazwa producenta jest wymagana.")
      return
    }
    setError("")
    try {
      const { id } = await api.createManufacturer(trimmed)
      setOpen(false)
      reset()
      await onCreated(id)
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        setError("Producent o tej nazwie już istnieje.")
      } else {
        setError("Nie udało się dodać producenta.")
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
      <DialogTrigger render={<Button size="icon-sm" aria-label="Dodaj producenta"><Plus className="size-4" /></Button>} />
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Dodaj producenta</DialogTitle>
        </DialogHeader>
        <div className="flex flex-col gap-2">
          <Label htmlFor="mfg-name">Nazwa</Label>
          <Input
            id="mfg-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="np. Bosch"
          />
          <FormError>{error}</FormError>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={() => setOpen(false)}>
            Anuluj
          </Button>
          <Button onClick={submit}>Dodaj</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function ManufacturerDetailPanel({
  id,
  onManufacturersRefetch,
  onDeleted,
}: {
  id: number
  onManufacturersRefetch: () => void | Promise<void>
  onDeleted: () => void
}) {
  const [manufacturer, setManufacturer] = useState<ManufacturerDetail | null>(null)
  const [name, setName] = useState("")
  const [nameError, setNameError] = useState("")
  const [confirmingDelete, setConfirmingDelete] = useState(false)

  async function refetch() {
    const data = await api.getManufacturer(id)
    setManufacturer(data)
    setName(data.name)
  }

  useEffect(() => {
    refetch()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id])

  async function saveName() {
    const trimmed = name.trim()
    if (!manufacturer || !trimmed || trimmed === manufacturer.name) {
      setName(manufacturer?.name ?? "")
      return
    }
    setNameError("")
    try {
      await api.updateManufacturer(id, trimmed)
      await refetch()
      await onManufacturersRefetch()
    } catch (err) {
      setName(manufacturer.name)
      if (err instanceof ApiError && err.status === 409) {
        setNameError("Producent o tej nazwie już istnieje.")
      } else {
        setNameError("Nie udało się zapisać nazwy.")
      }
    }
  }

  async function confirmDelete() {
    await api.removeManufacturer(id)
    setConfirmingDelete(false)
    await onManufacturersRefetch()
    onDeleted()
  }

  async function removeContact(contactId: number) {
    await api.removeManufacturerContact(id, contactId)
    await refetch()
    await onManufacturersRefetch()
  }

  if (!manufacturer) return null

  return (
    <div>
      <div className="mb-1 flex items-start justify-between gap-2">
        <div className="flex-1">
          <Input
            value={name}
            onChange={(e) => setName(e.target.value)}
            onBlur={saveName}
            className="text-[15px] font-semibold"
          />
          <FormError>{nameError}</FormError>
        </div>
        <Button size="sm" variant="destructive" onClick={() => setConfirmingDelete(true)}>
          Usuń producenta
        </Button>
      </div>

      <div className="mb-1 flex items-center justify-between">
        <SectionLabel>Osoby kontaktowe</SectionLabel>
        <ContactDialog
          trigger={
            <Button size="sm" variant="secondary">
              + Dodaj kontakt
            </Button>
          }
          title="Dodaj osobę kontaktową"
          confirmLabel="Dodaj"
          onSubmit={async (body) => {
            await api.addManufacturerContact(id, body)
            await refetch()
            await onManufacturersRefetch()
          }}
        />
      </div>

      {manufacturer.contacts.length > 0 ? (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Imię i nazwisko</TableHead>
              <TableHead>Stanowisko</TableHead>
              <TableHead>Telefon</TableHead>
              <TableHead>Email</TableHead>
              <TableHead className="w-16" />
            </TableRow>
          </TableHeader>
          <TableBody>
            {manufacturer.contacts.map((c) => (
              <TableRow key={c.id}>
                <TableCell>
                  {[c.firstName, c.lastName].filter(Boolean).join(" ") || "-"}
                </TableCell>
                <TableCell>{c.position || "-"}</TableCell>
                <TableCell>{c.phone || "-"}</TableCell>
                <TableCell>{c.email || "-"}</TableCell>
                <TableCell>
                  <div className="flex items-center gap-0.5">
                    <ContactDialog
                      trigger={
                        <Button size="icon-xs" variant="ghost" aria-label="Edytuj kontakt">
                          <Pencil className="size-3.5 text-muted-foreground" />
                        </Button>
                      }
                      title="Edytuj osobę kontaktową"
                      confirmLabel="Zapisz"
                      initial={c}
                      onSubmit={async (body) => {
                        await api.updateManufacturerContact(id, c.id, body)
                        await refetch()
                      }}
                    />
                    <Button
                      size="icon-xs"
                      variant="ghost"
                      aria-label="Usuń kontakt"
                      onClick={() => removeContact(c.id)}
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
        <Hint>brak osób kontaktowych</Hint>
      )}

      {confirmingDelete && (
        <ConfirmDialog
          open
          title="Usuń producenta"
          description={`Na pewno usunąć producenta „${manufacturer.name}” wraz ze wszystkimi osobami kontaktowymi (${manufacturer.contacts.length})? Tej operacji nie można cofnąć.`}
          confirmLabel="Usuń producenta"
          variant="destructive"
          onConfirm={confirmDelete}
          onCancel={() => setConfirmingDelete(false)}
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
  initial?: ManufacturerContact
  onSubmit: (body: ContactFormBody) => Promise<void>
}) {
  const [open, setOpen] = useState(false)
  const [firstName, setFirstName] = useState(initial?.firstName ?? "")
  const [lastName, setLastName] = useState(initial?.lastName ?? "")
  const [phone, setPhone] = useState(initial?.phone ?? "")
  const [position, setPosition] = useState(initial?.position ?? "")
  const [email, setEmail] = useState(initial?.email ?? "")
  const [error, setError] = useState("")

  function reset() {
    setFirstName(initial?.firstName ?? "")
    setLastName(initial?.lastName ?? "")
    setPhone(initial?.phone ?? "")
    setPosition(initial?.position ?? "")
    setEmail(initial?.email ?? "")
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
      })
      setOpen(false)
      reset()
    } catch {
      setError("Nie udało się zapisać osoby kontaktowej.")
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
              <Label htmlFor="contact-first-name">Imię</Label>
              <Input id="contact-first-name" value={firstName} onChange={(e) => setFirstName(e.target.value)} />
            </div>
            <div className="flex flex-1 flex-col gap-2">
              <Label htmlFor="contact-last-name">Nazwisko</Label>
              <Input id="contact-last-name" value={lastName} onChange={(e) => setLastName(e.target.value)} />
            </div>
          </div>

          <Label htmlFor="contact-position">Stanowisko</Label>
          <Input id="contact-position" value={position} onChange={(e) => setPosition(e.target.value)} />

          <Label htmlFor="contact-phone">Numer telefonu</Label>
          <Input id="contact-phone" value={phone} onChange={(e) => setPhone(e.target.value)} />

          <Label htmlFor="contact-email">Email</Label>
          <Input id="contact-email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} />

          <FormError>{error}</FormError>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => setOpen(false)}>
            Anuluj
          </Button>
          <Button onClick={submit}>{confirmLabel}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export { ManufacturersView }
