import { useEffect, useState, type ReactElement } from "react"
import { Pencil, Plus, Trash2 } from "lucide-react"

import { api, ApiError } from "@/api/client"
import type { ManufacturerContact, ManufacturerDetail } from "@/api/types"
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
import { useLanguage } from "@/i18n/use-language"

function ManufacturersView() {
  const { t } = useLanguage()
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
            placeholder={t("manufacturer.searchPlaceholder")}
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
                    {m.contactCount}{" "}
                    {t(m.contactCount === 1 ? "manufacturer.contactSingular" : "manufacturer.contactPlural")}
                  </span>
                </button>
              </li>
            ))}
          </ul>
        ) : (
          <Hint>{search ? t("manufacturer.noMatches") : t("manufacturer.emptyAll")}</Hint>
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
          <Hint>{t("manufacturer.selectHint")}</Hint>
        )}
      </div>
    </div>
  )
}

function NewManufacturerDialog({ onCreated }: { onCreated: (id: number) => void | Promise<void> }) {
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
      setError(t("manufacturer.nameRequired"))
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
        setError(t("manufacturer.nameConflict"))
      } else {
        setError(t("manufacturer.addFailed"))
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
      <DialogTrigger render={<Button size="icon-sm" aria-label={t("manufacturer.addAria")}><Plus className="size-4" /></Button>} />
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("manufacturer.addTitle")}</DialogTitle>
        </DialogHeader>
        <div className="flex flex-col gap-2">
          <Label htmlFor="mfg-name">{t("common.name")}</Label>
          <Input
            id="mfg-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder={t("manufacturer.namePlaceholder")}
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

function ManufacturerDetailPanel({
  id,
  onManufacturersRefetch,
  onDeleted,
}: {
  id: number
  onManufacturersRefetch: () => void | Promise<void>
  onDeleted: () => void
}) {
  const { t } = useLanguage()
  const [manufacturer, setManufacturer] = useState<ManufacturerDetail | null>(null)
  const [name, setName] = useState("")
  const [nameError, setNameError] = useState("")
  const [confirmingDelete, setConfirmingDelete] = useState(false)
  const [deletingPending, setDeletingPending] = useState(false)
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [confirmingDeleteContactId, setConfirmingDeleteContactId] = useState<number | null>(null)
  const [contactDeletePending, setContactDeletePending] = useState(false)
  const [contactDeleteError, setContactDeleteError] = useState<string | null>(null)
  const [confirmingDeleteTypeId, setConfirmingDeleteTypeId] = useState<number | null>(null)
  const [typeDeletePending, setTypeDeletePending] = useState(false)
  const [typeDeleteError, setTypeDeleteError] = useState<string | null>(null)
  // Wspólne pole dodawania serii/typu i podtypu (podtyp opcjonalny) + filtr tabeli poniżej.
  const [typeInput, setTypeInput] = useState("")
  const [subtypeInput, setSubtypeInput] = useState("")
  const [addPending, setAddPending] = useState(false)
  const [addError, setAddError] = useState("")
  const [catalogFilter, setCatalogFilter] = useState("")
  // Podtyp identyfikowany parą (seria, podtyp) — samo id podtypu nie wystarcza, bo do
  // usunięcia potrzebny jest też id serii (zob. trasa DELETE .../product-types/{t}/subtypes/{s}).
  const [confirmingDeleteSubtype, setConfirmingDeleteSubtype] = useState<
    { typeId: number; subtypeId: number } | null
  >(null)
  const [subtypeDeletePending, setSubtypeDeletePending] = useState(false)
  const [subtypeDeleteError, setSubtypeDeleteError] = useState<string | null>(null)

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
        setNameError(t("manufacturer.nameConflict"))
      } else {
        setNameError(t("manufacturer.saveNameFailed"))
      }
    }
  }

  async function confirmDelete() {
    setDeletingPending(true)
    setDeleteError(null)
    try {
      await api.removeManufacturer(id)
      setConfirmingDelete(false)
      await onManufacturersRefetch()
      onDeleted()
    } catch (err) {
      setDeleteError(err instanceof ApiError ? err.message : t("manufacturer.deleteFailed"))
    } finally {
      setDeletingPending(false)
    }
  }

  async function confirmRemoveContact() {
    if (confirmingDeleteContactId === null) return
    setContactDeletePending(true)
    setContactDeleteError(null)
    try {
      await api.removeManufacturerContact(id, confirmingDeleteContactId)
      setConfirmingDeleteContactId(null)
      await refetch()
      await onManufacturersRefetch()
    } catch (err) {
      setContactDeleteError(err instanceof ApiError ? err.message : t("manufacturer.deleteContactFailed"))
    } finally {
      setContactDeletePending(false)
    }
  }

  // Jedno "Dodaj" obsługuje oba poziomy: sam typ zakłada serię, typ + podtyp dokłada
  // podtyp (zakładając serię po drodze, jeśli jeszcze nie istnieje). Pola czyszczą się
  // dopiero po udanym zapisie — nieudany nie gubi tego, co użytkownik wpisał.
  async function addTypeOrSubtype() {
    if (!manufacturer || addPending) return
    const typeName = typeInput.trim()
    const subtypeName = subtypeInput.trim()
    if (!typeName) {
      setAddError(t("manufacturer.productTypeRequired"))
      return
    }

    setAddPending(true)
    setAddError("")
    try {
      let type = manufacturer.productTypes.find((p) => p.name === typeName) ?? null
      if (!type) {
        const created = await api.addManufacturerProductType(id, typeName)
        type = { id: created.id, name: typeName, subtypes: [] }
      } else if (!subtypeName) {
        setAddError(t("manufacturer.productTypeConflict"))
        return
      }
      if (subtypeName) {
        await api.addManufacturerProductSubtype(id, type.id, subtypeName)
      }
      setTypeInput("")
      setSubtypeInput("")
      await refetch()
      await onManufacturersRefetch()
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        setAddError(
          subtypeName ? t("manufacturer.productSubtypeConflict") : t("manufacturer.productTypeConflict")
        )
      } else {
        setAddError(
          subtypeName ? t("manufacturer.addProductSubtypeFailed") : t("manufacturer.addProductTypeFailed")
        )
      }
    } finally {
      setAddPending(false)
    }
  }

  async function confirmRemoveProductType() {
    if (confirmingDeleteTypeId === null) return
    setTypeDeletePending(true)
    setTypeDeleteError(null)
    try {
      await api.removeManufacturerProductType(id, confirmingDeleteTypeId)
      setConfirmingDeleteTypeId(null)
      await refetch()
    } catch (err) {
      setTypeDeleteError(err instanceof ApiError ? err.message : t("manufacturer.deleteProductTypeFailed"))
    } finally {
      setTypeDeletePending(false)
    }
  }

  async function confirmRemoveProductSubtype() {
    if (confirmingDeleteSubtype === null) return
    setSubtypeDeletePending(true)
    setSubtypeDeleteError(null)
    try {
      await api.removeManufacturerProductSubtype(
        id,
        confirmingDeleteSubtype.typeId,
        confirmingDeleteSubtype.subtypeId
      )
      setConfirmingDeleteSubtype(null)
      await refetch()
    } catch (err) {
      setSubtypeDeleteError(
        err instanceof ApiError ? err.message : t("manufacturer.deleteProductSubtypeFailed")
      )
    } finally {
      setSubtypeDeletePending(false)
    }
  }

  if (!manufacturer) return null

  const confirmingDeleteContact = manufacturer.contacts.find((c) => c.id === confirmingDeleteContactId) ?? null
  const confirmingDeleteType = manufacturer.productTypes.find((p) => p.id === confirmingDeleteTypeId) ?? null
  const typeNames = manufacturer.productTypes.map((p) => p.name)

  // Płaska tabela: seria bez podtypów daje jeden wiersz (podtyp pusty), seria z podtypami —
  // po wierszu na podtyp. Filtr dopasowuje po obu kolumnach naraz, więc wpisanie nazwy serii
  // pokazuje całą jej zawartość, a wpisanie podtypu — tylko pasujące wiersze.
  const needle = catalogFilter.trim().toLowerCase()
  const catalogRows: {
    typeId: number
    typeName: string
    subtypeId: number | null
    subtypeName: string | null
    firstOfType: boolean
  }[] = []
  for (const type of manufacturer.productTypes) {
    const matchingSubtypes = type.subtypes.filter(
      (sub) =>
        !needle ||
        type.name.toLowerCase().includes(needle) ||
        sub.name.toLowerCase().includes(needle)
    )
    const typeMatches = !needle || type.name.toLowerCase().includes(needle)
    if (matchingSubtypes.length === 0) {
      // Seria bez (pasujących) podtypów pokazuje się sama tylko wtedy, gdy sama pasuje —
      // inaczej filtr po nazwie podtypu wyrzucałby puste wiersze niepasujących serii.
      if (typeMatches) {
        catalogRows.push({
          typeId: type.id,
          typeName: type.name,
          subtypeId: null,
          subtypeName: null,
          firstOfType: true,
        })
      }
      continue
    }
    matchingSubtypes.forEach((sub, index) => {
      catalogRows.push({
        typeId: type.id,
        typeName: type.name,
        subtypeId: sub.id,
        subtypeName: sub.name,
        firstOfType: index === 0,
      })
    })
  }

  const confirmingDeleteSubtypeRow = confirmingDeleteSubtype
    ? (manufacturer.productTypes
        .find((p) => p.id === confirmingDeleteSubtype.typeId)
        ?.subtypes.find((sub) => sub.id === confirmingDeleteSubtype.subtypeId) ?? null)
    : null

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
        <Button
          size="sm"
          variant="destructive"
          onClick={() => {
            setDeleteError(null)
            setConfirmingDelete(true)
          }}
        >
          {t("manufacturer.deleteButton")}
        </Button>
      </div>

      <div className="mb-1 flex items-center justify-between">
        <SectionLabel>{t("manufacturer.contactsLabel")}</SectionLabel>
        <ContactDialog
          trigger={
            <Button size="sm" variant="secondary">
              {t("manufacturer.addContactButton")}
            </Button>
          }
          title={t("manufacturer.addContactTitle")}
          confirmLabel={t("common.add")}
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
              <TableHead>{t("common.fullName")}</TableHead>
              <TableHead>{t("common.position")}</TableHead>
              <TableHead>{t("common.phone")}</TableHead>
              <TableHead>{t("common.email")}</TableHead>
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
                        <Button size="icon-xs" variant="ghost" aria-label={t("manufacturer.editContactAria")}>
                          <Pencil className="size-3.5 text-muted-foreground" />
                        </Button>
                      }
                      title={t("manufacturer.editContactTitle")}
                      confirmLabel={t("common.save")}
                      initial={c}
                      onSubmit={async (body) => {
                        await api.updateManufacturerContact(id, c.id, body)
                        await refetch()
                      }}
                    />
                    <Button
                      size="icon-xs"
                      variant="ghost"
                      aria-label={t("manufacturer.deleteContactAria")}
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

      {/* Serie/typy i podtypy — podpowiedzi do pól przy elemencie zakupowym (zob.
          ProductTypeField/ProductSubtypeField) i do filtrów w widoku "Cała baza". Element
          trzyma same nazwy, więc usunięcie pozycji tutaj nie zmienia niczego w opisanych
          już elementach — znika tylko z listy do wyboru.

          Jedno pole dodawania na oba poziomy: seria/typ jest rozwijalna (można wybrać
          istniejącą albo wpisać nową), podtyp jest opcjonalny. Sam typ zakłada nową serię,
          typ + podtyp dokłada podtyp do wskazanej serii (zakładając ją po drodze, jeśli
          jeszcze nie istnieje). */}
      <div className="mt-4">
        <SectionLabel>{t("manufacturer.productTypesLabel")}</SectionLabel>

        <div className="mt-1 flex items-start gap-1.5">
          <div className="min-w-0 flex-1">
            <Combobox
              items={typeNames}
              value={typeInput || null}
              inputValue={typeInput}
              onInputValueChange={(v) => setTypeInput(v)}
              onValueChange={(v) => setTypeInput((v as string | null) ?? "")}
            >
              <ComboboxInput
                placeholder={t("manufacturer.productTypePlaceholder")}
                showClear
                onKeyDown={(e) => {
                  if (e.key === "Enter") addTypeOrSubtype()
                }}
              />
              <ComboboxContent>
                <ComboboxEmpty>{t("manufacturer.newProductTypeHint")}</ComboboxEmpty>
                <ComboboxList>
                  {(name: string) => (
                    <ComboboxItem key={name} value={name}>
                      {name}
                    </ComboboxItem>
                  )}
                </ComboboxList>
              </ComboboxContent>
            </Combobox>
          </div>

          <Input
            value={subtypeInput}
            onChange={(e) => setSubtypeInput(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") addTypeOrSubtype()
            }}
            placeholder={t("manufacturer.productSubtypePlaceholder")}
            className="min-w-0 flex-1"
          />

          <Button variant="secondary" onClick={addTypeOrSubtype} disabled={addPending}>
            {t("common.add")}
          </Button>
        </div>
        <FormError>{addError}</FormError>

        {manufacturer.productTypes.length > 0 ? (
          <>
            <Input
              value={catalogFilter}
              onChange={(e) => setCatalogFilter(e.target.value)}
              placeholder={t("manufacturer.filterCatalogPlaceholder")}
              className="mt-2"
            />
            {catalogRows.length > 0 ? (
              <Table className="mt-2">
                <TableHeader>
                  <TableRow>
                    <TableHead>{t("manufacturer.colProductType")}</TableHead>
                    <TableHead>{t("manufacturer.colProductSubtype")}</TableHead>
                    <TableHead className="w-10" />
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {catalogRows.map((row) => (
                    <TableRow key={`${row.typeId}-${row.subtypeId ?? "none"}`}>
                      {/* Nazwa serii tylko w pierwszym jej wierszu — kolejne wiersze tej
                          samej serii to jej następne podtypy, powtarzanie nazwy zaśmiecałoby
                          tabelę. */}
                      <TableCell className={row.firstOfType ? "" : "text-muted-foreground/40"}>
                        {row.firstOfType ? row.typeName : "↳"}
                      </TableCell>
                      <TableCell>{row.subtypeName ?? "-"}</TableCell>
                      <TableCell>
                        <Button
                          size="icon-xs"
                          variant="ghost"
                          aria-label={
                            row.subtypeId === null
                              ? t("manufacturer.deleteProductTypeTitle")
                              : t("manufacturer.deleteProductSubtypeTitle")
                          }
                          onClick={() =>
                            row.subtypeId === null
                              ? setConfirmingDeleteTypeId(row.typeId)
                              : setConfirmingDeleteSubtype({ typeId: row.typeId, subtypeId: row.subtypeId })
                          }
                        >
                          <Trash2 className="size-3.5 text-muted-foreground" />
                        </Button>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            ) : (
              <Hint>{t("manufacturer.noMatchingCatalogRows")}</Hint>
            )}
          </>
        ) : (
          <Hint>{t("manufacturer.noProductTypes")}</Hint>
        )}
      </div>

      {confirmingDelete && (
        <ConfirmDialog
          open
          title={t("manufacturer.deleteButton")}
          description={t("manufacturer.deleteConfirmDescription", {
            name: manufacturer.name,
            count: manufacturer.contacts.length,
          })}
          confirmLabel={t("manufacturer.deleteButton")}
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
          title={t("manufacturer.deleteContactAria")}
          description={t("manufacturer.deleteContactConfirmDescription", {
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

      {confirmingDeleteType && (
        <ConfirmDialog
          open
          title={t("manufacturer.deleteProductTypeTitle")}
          description={t("manufacturer.deleteProductTypeConfirmDescription", {
            name: confirmingDeleteType.name,
          })}
          confirmLabel={t("common.delete")}
          variant="destructive"
          onConfirm={confirmRemoveProductType}
          onCancel={() => setConfirmingDeleteTypeId(null)}
          pending={typeDeletePending}
          error={typeDeleteError}
        />
      )}

      {confirmingDeleteSubtypeRow && (
        <ConfirmDialog
          open
          title={t("manufacturer.deleteProductSubtypeTitle")}
          description={t("manufacturer.deleteProductSubtypeConfirmDescription", {
            name: confirmingDeleteSubtypeRow.name,
          })}
          confirmLabel={t("common.delete")}
          variant="destructive"
          onConfirm={confirmRemoveProductSubtype}
          onCancel={() => setConfirmingDeleteSubtype(null)}
          pending={subtypeDeletePending}
          error={subtypeDeleteError}
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
  initial?: ManufacturerContact
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
      setError(t("manufacturer.saveContactFailed"))
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
              <Label htmlFor="contact-first-name">{t("common.firstName")}</Label>
              <Input id="contact-first-name" value={firstName} onChange={(e) => setFirstName(e.target.value)} />
            </div>
            <div className="flex flex-1 flex-col gap-2">
              <Label htmlFor="contact-last-name">{t("common.lastName")}</Label>
              <Input id="contact-last-name" value={lastName} onChange={(e) => setLastName(e.target.value)} />
            </div>
          </div>

          <Label htmlFor="contact-position">{t("common.position")}</Label>
          <Input id="contact-position" value={position} onChange={(e) => setPosition(e.target.value)} />

          <Label htmlFor="contact-phone">{t("common.phoneNumber")}</Label>
          <Input id="contact-phone" value={phone} onChange={(e) => setPhone(e.target.value)} />

          <Label htmlFor="contact-email">{t("common.email")}</Label>
          <Input id="contact-email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} />

          <Label htmlFor="contact-address">{t("common.address")}</Label>
          <Input id="contact-address" value={address} onChange={(e) => setAddress(e.target.value)} />

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

export { ManufacturersView }
