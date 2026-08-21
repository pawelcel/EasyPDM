import { type ReactElement, useEffect, useState } from "react"

import { api, ApiError } from "@/api/client"
import { itemDisplayLabel, type Item, type ItemType, type Project } from "@/api/types"
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
import { ManufacturerField, MaterialField, PropField } from "@/features/items/property-fields"
import type { TranslationKey } from "@/i18n/translations"
import { useLanguage } from "@/i18n/use-language"

type Mode = ItemType | "existing"

const MODE_LABEL_KEYS: Record<Mode, TranslationKey> = {
  folder: "itemType.folder",
  part: "itemType.part",
  assembly: "itemType.assembly",
  file: "addNode.modeFile",
  existing: "addNode.modeExisting",
}

// Elementy o tej samej nazwie mogą pochodzić z różnych projektów — numer odróżnia je.
function candidateLabel(item: Item | undefined): string | undefined {
  return item ? itemDisplayLabel(item) : undefined
}

// Co wolno dodać jako dziecko pod czym w strukturze:
//   projekt (brak rodzica) / folder -> wszystko
//   złożenie                        -> tylko część i złożenie (BOM)
//   część / plik                    -> nic (są liśćmi struktury)
function modesForParent(parentType: ItemType | null): Mode[] {
  if (parentType === null || parentType === "folder")
    return ["folder", "part", "assembly", "file", "existing"]
  if (parentType === "assembly") return ["part", "assembly", "existing"]
  return []
}

function AddNodeDialog({
  trigger,
  projectId: fixedProjectId,
  parentId: fixedParentId,
  parentType: fixedParentType,
  lockMode,
  initialOpen,
  initialName,
  ticket,
  onOpenChange,
  onCreated,
}: {
  trigger: ReactElement
  // Gdy podane (zwykłe użycie — pasek narzędzi projektu, węzeł drzewa, panel szczegółów):
  // dialog tworzy od razu w TYM projekcie/pod TYM rodzicem, bez pytania o nic dodatkowego.
  // Gdy POMINIĘTE (zob. PendingTicketBanner "Nowy element"): dialog SAM pyta o projekt i
  // (opcjonalnie) element nadrzędny jako pierwszy krok — nie wymaga wcześniejszej nawigacji
  // po panelu projektów, wszystko dzieje się w tym jednym okienku.
  projectId?: string
  parentId?: string | null
  parentType?: ItemType | null
  lockMode?: ItemType
  // Wymusza otwarcie od razu po zamontowaniu — używane przez PendingTicketBanner, które
  // montuje tę instancję dopiero po kliknięciu "Nowy element", z ukrytym triggerem.
  initialOpen?: boolean
  initialName?: string
  // Bilet z makra CAD (zob. EasyPDM.FreeCad/EasyPDMUpload.FCMacro) — doklejany do POST
  // /nodes, żeby makro mogło się dowiedzieć (GET /create-tickets/{ticket}), że element
  // powstał. Podawany JAWNIE przez wywołującego (PendingTicketBanner), nie czytany z
  // żadnego globalnego stanu — zwykłe, ręczne użycia tego dialogu (bez tego propa) nigdy
  // nie mają z biletem nic wspólnego, niezależnie od tego, czy jakiś akurat czeka.
  ticket?: string
  // Wołane przy KAŻDEJ zmianie otwarcia/zamknięcia — PendingTicketBanner używa tego, żeby
  // wrócić do wyboru "Nowy/Istniejący", gdy użytkownik zamknie to okno bez tworzenia.
  onOpenChange?: (open: boolean) => void
  onCreated: () => void | Promise<void>
}) {
  const { t } = useLanguage()
  const needsProjectPicker = fixedProjectId === undefined
  const [open, setOpenState] = useState(initialOpen ?? false)
  function setOpen(next: boolean) {
    setOpenState(next)
    onOpenChange?.(next)
  }

  // Projekt/rodzic — albo z góry ustalone (zwykłe użycie), albo wybierane W TYM OKNIE
  // (zob. needsProjectPicker wyżej).
  const [projects, setProjects] = useState<Project[]>([])
  const [selectedProjectId, setSelectedProjectId] = useState("")
  const projectId = fixedProjectId ?? selectedProjectId
  const [parentCandidates, setParentCandidates] = useState<Item[]>([])
  const [selectedParentId, setSelectedParentId] = useState<string | null>(null)
  const parentId = fixedParentId !== undefined ? fixedParentId : selectedParentId
  const parentType =
    fixedParentType !== undefined
      ? fixedParentType
      : (parentCandidates.find((i) => i.id === selectedParentId)?.itemType ?? null)

  useEffect(() => {
    if (needsProjectPicker && open) api.getProjects().then(setProjects)
  }, [needsProjectPicker, open])

  useEffect(() => {
    if (!needsProjectPicker || !projectId) {
      setParentCandidates([])
      return
    }
    api.getItems({ projectId }).then((items) =>
      setParentCandidates(items.filter((i) => i.itemType === "folder" || i.itemType === "assembly"))
    )
  }, [needsProjectPicker, projectId])

  // Bilet doklejony do POST /nodes wymaga sensownego typu (Część/Złożenie) — makro nigdy
  // nie prosi o Folder/Plik/Istniejący, więc ten dialog, gdy ma podpięty bilet, w ogóle nie
  // pokazuje tych opcji (dokładnie tak, jak dawny natywny dialog makra ograniczał wybór do
  // Część/Złożenie).
  const rawAvailableModes = modesForParent(parentType)
  const availableModes = ticket
    ? rawAvailableModes.filter((m): m is "part" | "assembly" => m === "part" || m === "assembly")
    : rawAvailableModes
  const [mode, setMode] = useState<Mode>(lockMode ?? availableModes[0] ?? "folder")
  const [allItems, setAllItems] = useState<Item[]>([])

  // Folder / Część
  const [name, setName] = useState(initialName ?? "")
  const [mass, setMass] = useState("")
  const [rodzaj, setRodzaj] = useState("")
  // Materiał/Producent/Numery zamówieniowe/Norma — pola zależne od rodzaju, te same co w
  // panelu szczegółów już istniejącego elementu (PartPropertyForm), tylko zbierane lokalnie
  // przed pierwszym zapisem zamiast od razu wysyłane przez API (element jeszcze nie istnieje).
  const [extraProps, setExtraProps] = useState<Record<string, string>>({})
  function setExtraField(key: string, value: string) {
    setExtraProps((prev) => ({ ...prev, [key]: value }))
  }
  // Widoczny tylko, gdy tworzenie ma przypięty bilet z makra (zob. props "ticket" wyżej) —
  // w normalnym, ręcznym dodawaniu przez aplikację webową nie ma żadnego lokalnego pliku
  // CAD, więc nie ma czego eksportować. Domyślnie zaznaczony (tak jak dawniej w natywnym
  // dialogu makra).
  const [exportStep, setExportStep] = useState(true)

  // Inny plik
  const [file, setFile] = useState<File | null>(null)
  const [propsText, setPropsText] = useState("")

  // Istniejący element
  const [childId, setChildId] = useState("")
  const [quantity, setQuantity] = useState("1")

  const [error, setError] = useState("")
  const [submitting, setSubmitting] = useState(false)

  // Kandydaci do "Istniejący element" — pobierane leniwie z całej bazy, nie tylko z tego
  // projektu, bo Część/Złożenie mogą być współdzielone jako komponent w wielu projektach.
  useEffect(() => {
    if (!open) return
    api.getItems({}).then(setAllItems)
  }, [open])

  const partsAndAssemblies = allItems.filter(
    (i) => i.itemType === "part" || i.itemType === "assembly"
  )

  // Dowolna Część/Złożenie z całej bazy (poza samym sobą, gdy dodajemy pod konkretnym
  // rodzicem) — ten sam komponent można wykorzystać w wielu złożeniach/projektach.
  // Na poziomie głównym (bez rodzica): wybór elementu z innego projektu przenosi go
  // (project_id) i pokazuje jako korzeń tego projektu; wybór elementu już należącego
  // do tego projektu po prostu przywraca go, jeśli był odpięty („Usuń ze struktury”).
  const candidates = parentId
    ? partsAndAssemblies.filter((i) => i.id !== parentId)
    : partsAndAssemblies

  function reset() {
    setMode(lockMode ?? availableModes[0] ?? "folder")
    setName(initialName ?? "")
    setMass("")
    setRodzaj("")
    setExtraProps({})
    setExportStep(true)
    setFile(null)
    setPropsText("")
    setChildId("")
    setQuantity("1")
    setError("")
    if (needsProjectPicker) {
      setSelectedProjectId("")
      setSelectedParentId(null)
    }
  }

  async function handleCreateContainer(itemType: "folder" | "part" | "assembly") {
    if (needsProjectPicker && !projectId) {
      setError(t("addNode.projectRequired"))
      return
    }
    if (itemType === "part" && !rodzaj) {
      setError(
        t("addNode.selectKindError", {
          manufactured: t("part.kindManufactured"),
          purchased: t("part.kindPurchased"),
          standard: t("part.kindStandard"),
          client: t("part.kindClient"),
        })
      )
      return
    }

    const trimmed = name.trim()
    if (!trimmed) {
      setError(t("addNode.nameRequired"))
      return
    }

    const properties: Record<string, unknown> = {}
    if (itemType === "part") {
      properties.rodzaj = rodzaj
      if (rodzaj === "Zakupowa" && mass.trim()) properties.mass = mass.trim()
      for (const [key, value] of Object.entries(extraProps)) {
        if (value.trim()) properties[key] = value.trim()
      }
    }
    if (itemType === "assembly") {
      if (mass.trim()) properties.mass = mass.trim()
      if (rodzaj) properties.rodzaj = rodzaj
    }

    setSubmitting(true)
    setError("")
    try {
      await api.createNode(projectId, {
        name: trimmed,
        itemType,
        properties,
        parentId,
        ticket,
        exportStep: ticket ? exportStep : undefined,
      })
      setOpen(false)
      reset()
      await onCreated()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t("addNode.createFailed"))
    } finally {
      setSubmitting(false)
    }
  }

  async function handleUploadFile() {
    const trimmedName = name.trim()
    if (!file && !trimmedName) {
      setError(t("addNode.fileOrNameRequired"))
      return
    }

    const trimmedProps = propsText.trim()
    let parsedProps: Record<string, unknown> = {}
    if (trimmedProps) {
      try {
        parsedProps = JSON.parse(trimmedProps)
      } catch {
        setError(t("addNode.invalidJson"))
        return
      }
    }

    setSubmitting(true)
    setError("")
    try {
      if (file) {
        const formData = new FormData()
        formData.append("file", file)
        if (trimmedProps) formData.append("properties", trimmedProps)
        if (parentId) formData.append("parentId", parentId)
        await api.uploadItem(projectId, formData)
      } else {
        await api.createNode(projectId, {
          name: trimmedName,
          itemType: "file",
          properties: parsedProps,
          parentId,
        })
      }
      setOpen(false)
      reset()
      await onCreated()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t("addNode.addFailed"))
    } finally {
      setSubmitting(false)
    }
  }

  async function handleLinkExisting() {
    if (!childId) {
      setError(t("addNode.selectItemRequired"))
      return
    }

    setSubmitting(true)
    setError("")
    try {
      if (parentId) {
        const qty = Number(quantity)
        if (!Number.isFinite(qty) || qty <= 0) {
          setError(t("addNode.quantityInvalid"))
          setSubmitting(false)
          return
        }
        await api.addChild(parentId, childId, qty)
      } else {
        // Bez rodzica: element z innego projektu trzeba przenieść (project_id) i pokazać jako
        // korzeń; element już należący do tego projektu — po prostu przywracamy jego widoczność.
        const target = candidates.find((c) => c.id === childId)
        if (target && target.projectId !== projectId) {
          await api.moveItemToProject(childId, projectId)
        } else {
          await api.setShowInTree(childId, true)
        }
      }
      setOpen(false)
      reset()
      await onCreated()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t("addNode.addFailed"))
    } finally {
      setSubmitting(false)
    }
  }

  function handleSubmit() {
    if (mode === "folder" || mode === "part" || mode === "assembly")
      return handleCreateContainer(mode)
    if (mode === "file") return handleUploadFile()
    return handleLinkExisting()
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
          <DialogTitle>{lockMode === "file" ? t("item.uploadFile") : t("addNode.title")}</DialogTitle>
        </DialogHeader>

        {needsProjectPicker && (
          <div className="flex flex-col gap-2">
            <Label>{t("addNode.projectLabel")}</Label>
            <Select
              value={selectedProjectId || "none"}
              onValueChange={(v) => {
                setSelectedProjectId(v === "none" ? "" : (v as string))
                setSelectedParentId(null)
              }}
            >
              <SelectTrigger className="w-full">
                <SelectValue>
                  {(v: string) =>
                    v === "none" || !v
                      ? t("addNode.selectProjectPlaceholder")
                      : (projects.find((p) => p.id === v)?.name ?? v)
                  }
                </SelectValue>
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="none">{t("addNode.selectProjectPlaceholder")}</SelectItem>
                {projects.map((p) => (
                  <SelectItem key={p.id} value={p.id}>
                    {p.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>

            {projectId && (
              <>
                <Label>{t("addNode.parentLabel")}</Label>
                <Select
                  value={selectedParentId ?? "root"}
                  onValueChange={(v) => setSelectedParentId(v === "root" ? null : (v as string))}
                >
                  <SelectTrigger className="w-full">
                    <SelectValue>
                      {(v: string) =>
                        v === "root" || !v
                          ? t("addNode.noParent")
                          : (candidateLabel(parentCandidates.find((i) => i.id === v)) ?? v)
                      }
                    </SelectValue>
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="root">{t("addNode.noParent")}</SelectItem>
                    {parentCandidates.map((i) => (
                      <SelectItem key={i.id} value={i.id}>
                        {candidateLabel(i)}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </>
            )}
          </div>
        )}

        {(!needsProjectPicker || projectId) && !lockMode && (
          <div className="flex flex-wrap gap-1.5">
            {availableModes.map((m) => (
              <Button
                key={m}
                type="button"
                size="sm"
                variant={mode === m ? "default" : "outline"}
                onClick={() => {
                  setMode(m)
                  setError("")
                }}
              >
                {t(MODE_LABEL_KEYS[m])}
              </Button>
            ))}
          </div>
        )}

        {(!needsProjectPicker || projectId) && mode === "part" && (
          <div className="flex flex-col gap-2">
            <Label>{t("part.kind")}</Label>
            <div className="flex gap-1.5">
              <Button
                type="button"
                size="sm"
                variant={rodzaj === "Wykonywana" ? "default" : "outline"}
                onClick={() => setRodzaj("Wykonywana")}
              >
                {t("part.kindManufactured")}
              </Button>
              <Button
                type="button"
                size="sm"
                variant={rodzaj === "Zakupowa" ? "default" : "outline"}
                onClick={() => setRodzaj("Zakupowa")}
              >
                {t("part.kindPurchased")}
              </Button>
              <Button
                type="button"
                size="sm"
                variant={rodzaj === "Normalia" ? "default" : "outline"}
                onClick={() => setRodzaj("Normalia")}
              >
                {t("part.kindStandard")}
              </Button>
              <Button
                type="button"
                size="sm"
                variant={rodzaj === "Klienta" ? "default" : "outline"}
                onClick={() => setRodzaj("Klienta")}
              >
                {t("part.kindClient")}
              </Button>
            </div>
            <Label htmlFor="node-name">{t("common.name")}</Label>
            <Input id="node-name" value={name} onChange={(e) => setName(e.target.value)} />

            {(rodzaj === "Wykonywana" || rodzaj === "Normalia") && (
              <MaterialField value={extraProps.material ?? ""} onSave={setExtraField} disabled={false} />
            )}
            {rodzaj === "Zakupowa" && (
              <>
                <ManufacturerField value={extraProps.manufacturer ?? ""} onSave={setExtraField} disabled={false} />
                <PropField
                  label={t("part.orderNumber")}
                  propKey="orderNumber"
                  value={extraProps.orderNumber ?? ""}
                  onSave={setExtraField}
                  disabled={false}
                />
                <PropField
                  label={t("part.orderNumber2")}
                  propKey="orderNumber2"
                  value={extraProps.orderNumber2 ?? ""}
                  onSave={setExtraField}
                  disabled={false}
                />
                <Label htmlFor="node-part-mass">{t("part.mass")}</Label>
                <Input
                  id="node-part-mass"
                  type="number"
                  step="any"
                  value={mass}
                  onChange={(e) => setMass(e.target.value)}
                  className="[appearance:textfield] [&::-webkit-inner-spin-button]:appearance-none [&::-webkit-outer-spin-button]:appearance-none"
                />
              </>
            )}
            {rodzaj === "Normalia" && (
              <PropField
                label={t("part.norm")}
                propKey="norm"
                value={extraProps.norm ?? ""}
                onSave={setExtraField}
                disabled={false}
              />
            )}

            {ticket && (
              <label className="flex cursor-pointer items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={exportStep}
                  onChange={(e) => setExportStep(e.target.checked)}
                  className="size-3.5 shrink-0 accent-primary"
                />
                {t("addNode.exportStepOptional")}
              </label>
            )}

            <FormError>{error}</FormError>
          </div>
        )}

        {(!needsProjectPicker || projectId) && (mode === "folder" || mode === "assembly") && (
          <div className="flex flex-col gap-2">
            <Label htmlFor="node-name">{t("common.name")}</Label>
            <Input id="node-name" value={name} onChange={(e) => setName(e.target.value)} />

            {mode === "assembly" && (
              <>
                <Label htmlFor="node-mass">{t("addNode.massOptional")}</Label>
                <Input
                  id="node-mass"
                  type="number"
                  step="any"
                  value={mass}
                  onChange={(e) => setMass(e.target.value)}
                  className="[appearance:textfield] [&::-webkit-inner-spin-button]:appearance-none [&::-webkit-outer-spin-button]:appearance-none"
                />
                <Label>{t("addNode.kindOptional")}</Label>
                <Select value={rodzaj || "none"} onValueChange={(v) => setRodzaj(v === "none" ? "" : (v as string))}>
                  <SelectTrigger className="w-full">
                    <SelectValue>
                      {(v: string) => (v === "none" || !v ? t("addNode.noneSelected") : v)}
                    </SelectValue>
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="none">{t("addNode.noneSelected")}</SelectItem>
                    <SelectItem value="Zakupowa">{t("part.kindPurchased")}</SelectItem>
                    <SelectItem value="Wykonywana">{t("part.kindManufactured")}</SelectItem>
                    <SelectItem value="Normalia">{t("part.kindStandard")}</SelectItem>
                  </SelectContent>
                </Select>
              </>
            )}
            {mode === "assembly" && ticket && (
              <label className="flex cursor-pointer items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={exportStep}
                  onChange={(e) => setExportStep(e.target.checked)}
                  className="size-3.5 shrink-0 accent-primary"
                />
                {t("addNode.exportStepOptional")}
              </label>
            )}
            <FormError>{error}</FormError>
          </div>
        )}

        {(!needsProjectPicker || projectId) && mode === "file" && (
          <div className="flex flex-col gap-2">
            <Label htmlFor="node-file">{t("addNode.fileOptional")}</Label>
            <Input
              id="node-file"
              type="file"
              onChange={(e) => setFile(e.target.files?.[0] ?? null)}
            />
            {!file && (
              <>
                <Label htmlFor="node-file-name">{t("addNode.nameIfNoFile")}</Label>
                <Input
                  id="node-file-name"
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  placeholder={t("addNode.fileNamePlaceholder")}
                />
              </>
            )}
            <Label htmlFor="node-props">{t("addNode.propertiesJsonOptional")}</Label>
            <Input
              id="node-props"
              value={propsText}
              onChange={(e) => setPropsText(e.target.value)}
              placeholder={t("addNode.propertiesJsonPlaceholder")}
            />
            <FormError>{error}</FormError>
          </div>
        )}

        {(!needsProjectPicker || projectId) && mode === "existing" && (
          <div className="flex flex-col gap-2">
            {parentId ? (
              <Hint>{t("addNode.existingHintWithParent")}</Hint>
            ) : (
              <Hint>{t("addNode.existingHintNoParent")}</Hint>
            )}
            {candidates.length === 0 ? (
              <Hint>{t("addNode.noCandidates")}</Hint>
            ) : (
              <>
                <Label>{t("addNode.elementLabel")}</Label>
                <Combobox
                  items={candidates.map((c) => c.id)}
                  value={childId || null}
                  onValueChange={(v) => setChildId((v as string | null) ?? "")}
                  itemToStringLabel={(id: string) =>
                    candidateLabel(candidates.find((c) => c.id === id)) ?? ""
                  }
                >
                  <ComboboxInput placeholder={t("part.searchPlaceholder")} />
                  <ComboboxContent>
                    <ComboboxEmpty>{t("addNode.noMatchingItems")}</ComboboxEmpty>
                    <ComboboxList>
                      {(id: string) => (
                        <ComboboxItem key={id} value={id}>
                          {candidateLabel(candidates.find((c) => c.id === id))}
                        </ComboboxItem>
                      )}
                    </ComboboxList>
                  </ComboboxContent>
                </Combobox>
                {parentId && (
                  <>
                    <Label htmlFor="node-quantity">{t("common.quantity")}</Label>
                    <Input
                      id="node-quantity"
                      type="number"
                      min="0"
                      step="any"
                      value={quantity}
                      onChange={(e) => setQuantity(e.target.value)}
                    />
                  </>
                )}
              </>
            )}
            <FormError>{error}</FormError>
          </div>
        )}

        <DialogFooter>
          <Button variant="outline" onClick={() => setOpen(false)}>
            {t("common.cancel")}
          </Button>
          <Button onClick={handleSubmit} disabled={submitting || (needsProjectPicker && !projectId)}>
            {submitting ? t("common.saving") : t("common.add")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export { AddNodeDialog }
