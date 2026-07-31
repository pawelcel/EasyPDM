import { type ReactElement, useEffect, useState } from "react"

import { api } from "@/api/client"
import { itemDisplayLabel, type Item, type ItemType } from "@/api/types"
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

type Mode = ItemType | "existing"

const MODE_LABELS: Record<Mode, string> = {
  folder: "Folder",
  part: "Część",
  assembly: "Złożenie",
  file: "Inny plik",
  existing: "Istniejący element",
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
  projectId,
  parentId,
  parentType,
  lockMode,
  onCreated,
}: {
  trigger: ReactElement
  projectId: string
  parentId: string | null
  parentType: ItemType | null
  lockMode?: ItemType
  onCreated: () => void | Promise<void>
}) {
  const availableModes = modesForParent(parentType)
  const [open, setOpen] = useState(false)
  const [mode, setMode] = useState<Mode>(lockMode ?? availableModes[0] ?? "folder")
  const [allItems, setAllItems] = useState<Item[]>([])

  // Folder / Część
  const [name, setName] = useState("")
  const [material, setMaterial] = useState("")
  const [mass, setMass] = useState("")
  const [rodzaj, setRodzaj] = useState("")

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
    setName("")
    setMaterial("")
    setMass("")
    setRodzaj("")
    setFile(null)
    setPropsText("")
    setChildId("")
    setQuantity("1")
    setError("")
  }

  async function handleCreateContainer(itemType: "folder" | "part" | "assembly") {
    if (itemType === "part" && !rodzaj) {
      setError("Wybierz rodzaj: Wykonywana albo Zakupowa.")
      return
    }

    const trimmed = name.trim()
    if (!trimmed) {
      setError("Nazwa jest wymagana.")
      return
    }

    const properties: Record<string, unknown> = {}
    if (itemType === "part") {
      properties.rodzaj = rodzaj
    }
    if (itemType === "assembly") {
      if (material.trim()) properties.material = material.trim()
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
      })
      setOpen(false)
      reset()
      await onCreated()
    } catch {
      setError("Nie udało się utworzyć elementu.")
    } finally {
      setSubmitting(false)
    }
  }

  async function handleUploadFile() {
    const trimmedName = name.trim()
    if (!file && !trimmedName) {
      setError("Podaj plik albo przynajmniej nazwę — plik można dograć później.")
      return
    }

    const trimmedProps = propsText.trim()
    let parsedProps: Record<string, unknown> = {}
    if (trimmedProps) {
      try {
        parsedProps = JSON.parse(trimmedProps)
      } catch {
        setError('Pole właściwości musi być poprawnym JSON-em, np. {"material":"Stal S235"}')
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
    } catch {
      setError("Nie udało się dodać elementu.")
    } finally {
      setSubmitting(false)
    }
  }

  async function handleLinkExisting() {
    if (!childId) {
      setError("Wybierz element.")
      return
    }

    setSubmitting(true)
    setError("")
    try {
      if (parentId) {
        const qty = Number(quantity)
        if (!Number.isFinite(qty) || qty <= 0) {
          setError("Ilość musi być liczbą większą od zera.")
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
    } catch {
      setError("Nie udało się dodać elementu.")
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
          <DialogTitle>{lockMode === "file" ? "Wgraj plik" : "Dodaj element"}</DialogTitle>
        </DialogHeader>

        {!lockMode && (
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
                {MODE_LABELS[m]}
              </Button>
            ))}
          </div>
        )}

        {mode === "part" && (
          <div className="flex flex-col gap-2">
            <Label>Rodzaj</Label>
            <div className="flex gap-1.5">
              <Button
                type="button"
                size="sm"
                variant={rodzaj === "Wykonywana" ? "default" : "outline"}
                onClick={() => setRodzaj("Wykonywana")}
              >
                Wykonywana
              </Button>
              <Button
                type="button"
                size="sm"
                variant={rodzaj === "Zakupowa" ? "default" : "outline"}
                onClick={() => setRodzaj("Zakupowa")}
              >
                Zakupowa
              </Button>
            </div>
            <Label htmlFor="node-name">Nazwa</Label>
            <Input id="node-name" value={name} onChange={(e) => setName(e.target.value)} />
            <FormError>{error}</FormError>
          </div>
        )}

        {(mode === "folder" || mode === "assembly") && (
          <div className="flex flex-col gap-2">
            <Label htmlFor="node-name">Nazwa</Label>
            <Input id="node-name" value={name} onChange={(e) => setName(e.target.value)} />

            {mode === "assembly" && (
              <>
                <Label htmlFor="node-material">Materiał (opcjonalnie)</Label>
                <Input
                  id="node-material"
                  value={material}
                  onChange={(e) => setMaterial(e.target.value)}
                  placeholder="np. Stal S235"
                />
                <Label htmlFor="node-mass">Masa [kg] (opcjonalnie)</Label>
                <Input
                  id="node-mass"
                  type="number"
                  step="any"
                  value={mass}
                  onChange={(e) => setMass(e.target.value)}
                />
                <Label>Rodzaj (opcjonalnie)</Label>
                <Select value={rodzaj || "none"} onValueChange={(v) => setRodzaj(v === "none" ? "" : (v as string))}>
                  <SelectTrigger className="w-full">
                    <SelectValue>
                      {(v: string) =>
                        v === "none" || !v ? "Nie wybrano" : v
                      }
                    </SelectValue>
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="none">Nie wybrano</SelectItem>
                    <SelectItem value="Zakupowa">Zakupowa</SelectItem>
                    <SelectItem value="Wykonywana">Wykonywana</SelectItem>
                  </SelectContent>
                </Select>
              </>
            )}
            <FormError>{error}</FormError>
          </div>
        )}

        {mode === "file" && (
          <div className="flex flex-col gap-2">
            <Label htmlFor="node-file">Plik (opcjonalnie — można dograć później)</Label>
            <Input
              id="node-file"
              type="file"
              onChange={(e) => setFile(e.target.files?.[0] ?? null)}
            />
            {!file && (
              <>
                <Label htmlFor="node-file-name">Nazwa (jeśli bez pliku)</Label>
                <Input
                  id="node-file-name"
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  placeholder="np. Karta katalogowa"
                />
              </>
            )}
            <Label htmlFor="node-props">Właściwości JSON (opcjonalnie)</Label>
            <Input
              id="node-props"
              value={propsText}
              onChange={(e) => setPropsText(e.target.value)}
              placeholder='np. {"material":"Stal S235"}'
            />
            <FormError>{error}</FormError>
          </div>
        )}

        {mode === "existing" && (
          <div className="flex flex-col gap-2">
            {parentId ? (
              <Hint>
                Część lub Złożenie z całej bazy — ten sam komponent można wykorzystać w wielu
                złożeniach, także z innych projektów.
              </Hint>
            ) : (
              <Hint>
                Część lub Złożenie z całej bazy — element z innego projektu zostanie do niego
                przeniesiony jako widoczny korzeń; odpięty wcześniej element tego projektu
                zostanie po prostu przywrócony.
              </Hint>
            )}
            {candidates.length === 0 ? (
              <Hint>Brak Części/Złożeń w bazie.</Hint>
            ) : (
              <>
                <Label>Element</Label>
                <Combobox
                  items={candidates.map((c) => c.id)}
                  value={childId || null}
                  onValueChange={(v) => setChildId((v as string | null) ?? "")}
                  itemToStringLabel={(id: string) =>
                    candidateLabel(candidates.find((c) => c.id === id)) ?? ""
                  }
                >
                  <ComboboxInput placeholder="Wpisz nazwę, żeby wyszukać…" />
                  <ComboboxContent>
                    <ComboboxEmpty>Brak pasujących elementów.</ComboboxEmpty>
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
                    <Label htmlFor="node-quantity">Ilość</Label>
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
            Anuluj
          </Button>
          <Button onClick={handleSubmit} disabled={submitting}>
            {submitting ? "Zapisywanie…" : "Dodaj"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export { AddNodeDialog }
