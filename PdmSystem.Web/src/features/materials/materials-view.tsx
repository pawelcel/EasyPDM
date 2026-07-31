import { useMemo, useState } from "react"
import { Trash2 } from "lucide-react"

import { api } from "@/api/client"
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
import { useMaterials } from "@/features/materials/use-materials"

function MaterialsView() {
  const { materials, refetch } = useMaterials()
  const [groupFilter, setGroupFilter] = useState("")

  const existingGroups = useMemo(
    () => Array.from(new Set(materials.map((m) => m.group).filter((g): g is string => !!g))).sort(),
    [materials]
  )

  const visibleMaterials = groupFilter
    ? materials.filter((m) => m.group === groupFilter)
    : materials

  async function remove(materialName: string) {
    await api.removeMaterial(materialName)
    await refetch()
  }

  return (
    <div className="mx-auto max-w-xl">
      <h2 className="mb-4 text-lg font-semibold tracking-tight">Lista materiałów</h2>

      <div className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
        <div className="mb-3 flex items-center justify-between gap-2">
          <AddMaterialDialog existingGroups={existingGroups} onAdded={refetch} />

          {existingGroups.length > 0 && (
            <div className="flex items-center gap-2">
              <Label className="text-xs whitespace-nowrap">Filtruj po grupie:</Label>
              <Select
                value={groupFilter || "all"}
                onValueChange={(v) => setGroupFilter(v === "all" ? "" : (v as string))}
              >
                <SelectTrigger className="w-48">
                  <SelectValue>
                    {(v: string) => (v === "all" || !v ? "Wszystkie grupy" : v)}
                  </SelectValue>
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">Wszystkie grupy</SelectItem>
                  {existingGroups.map((g) => (
                    <SelectItem key={g} value={g}>
                      {g}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          )}
        </div>

        {visibleMaterials.length > 0 ? (
          <ul className="flex flex-col gap-0.5">
            {visibleMaterials.map((m) => (
              <li
                key={m.name}
                className="flex items-center justify-between gap-2 rounded-md px-2 py-1.5 text-sm hover:bg-accent"
              >
                <span className="truncate">
                  {m.name}
                  {m.group && (
                    <span className="ml-2 text-[12.5px] text-muted-foreground">{m.group}</span>
                  )}
                </span>
                <Button
                  size="icon-xs"
                  variant="ghost"
                  aria-label={`Usuń ${m.name}`}
                  onClick={() => remove(m.name)}
                >
                  <Trash2 className="size-3.5 text-muted-foreground" />
                </Button>
              </li>
            ))}
          </ul>
        ) : (
          <Hint>
            {materials.length === 0
              ? "Brak materiałów — dodaj pierwszy przyciskiem powyżej."
              : "Brak materiałów w tej grupie."}
          </Hint>
        )}
      </div>
    </div>
  )
}

function AddMaterialDialog({
  existingGroups,
  onAdded,
}: {
  existingGroups: string[]
  onAdded: () => void | Promise<void>
}) {
  const [open, setOpen] = useState(false)
  const [name, setName] = useState("")
  const [group, setGroup] = useState("")
  const [error, setError] = useState("")

  function reset() {
    setName("")
    setGroup("")
    setError("")
  }

  async function add() {
    const trimmed = name.trim()
    if (!trimmed) {
      setError("Nazwa materiału jest wymagana.")
      return
    }
    await api.addMaterial(trimmed, group.trim() || null)
    setOpen(false)
    reset()
    await onAdded()
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next)
        if (!next) reset()
      }}
    >
      <DialogTrigger render={<Button>+ Dodaj materiał</Button>} />
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Dodaj materiał</DialogTitle>
        </DialogHeader>

        <div className="flex flex-col gap-2">
          <Label htmlFor="material-name">Nazwa</Label>
          <Input
            id="material-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="np. Stal S235"
          />

          <Label htmlFor="material-group">Grupa (opcjonalnie)</Label>
          <Input
            id="material-group"
            value={group}
            onChange={(e) => setGroup(e.target.value)}
            list="material-group-options"
            placeholder="np. Stale"
          />
          <datalist id="material-group-options">
            {existingGroups.map((g) => (
              <option key={g} value={g} />
            ))}
          </datalist>
          <Hint>
            Grupa to tylko pomoc przy filtrowaniu tej listy — nie trafia do właściwości Części.
          </Hint>

          <FormError>{error}</FormError>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => setOpen(false)}>
            Anuluj
          </Button>
          <Button onClick={add}>Dodaj</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export { MaterialsView }
