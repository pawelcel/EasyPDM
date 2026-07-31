import { type ReactElement, useState } from "react"

import { api } from "@/api/client"
import type { Item, ItemType } from "@/api/types"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog"
import { FormError } from "@/components/ui/form-error"
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

function AddNodeDialog({
  trigger,
  projectId,
  parentId,
  existingItems,
  lockMode,
  onCreated,
}: {
  trigger: ReactElement
  projectId: string
  parentId: string | null
  existingItems: Item[]
  lockMode?: ItemType
  onCreated: () => void | Promise<void>
}) {
  const [open, setOpen] = useState(false)
  const [mode, setMode] = useState<Mode>(lockMode ?? "folder")

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

  const availableModes: Mode[] = parentId
    ? ["folder", "part", "assembly", "file", "existing"]
    : ["folder", "part", "assembly", "file"]
  const candidates = existingItems.filter((i) => i.id !== parentId)

  function reset() {
    setMode(lockMode ?? "folder")
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
    if (!parentId) return
    if (!childId) {
      setError("Wybierz element.")
      return
    }
    const qty = Number(quantity)
    if (!Number.isFinite(qty) || qty <= 0) {
      setError("Ilość musi być liczbą większą od zera.")
      return
    }

    setSubmitting(true)
    setError("")
    try {
      await api.addChild(parentId, childId, qty)
      setOpen(false)
      reset()
      await onCreated()
    } catch {
      setError("Nie udało się dodać podelementu.")
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
            <Label>Element</Label>
            <Select value={childId} onValueChange={(v) => setChildId(v as string)}>
              <SelectTrigger className="w-full">
                <SelectValue>
                  {(v: string) => candidates.find((c) => c.id === v)?.fileName ?? "Wybierz…"}
                </SelectValue>
              </SelectTrigger>
              <SelectContent>
                {candidates.map((c) => (
                  <SelectItem key={c.id} value={c.id}>
                    {c.fileName}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <Label htmlFor="node-quantity">Ilość</Label>
            <Input
              id="node-quantity"
              type="number"
              min="0"
              step="any"
              value={quantity}
              onChange={(e) => setQuantity(e.target.value)}
            />
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
