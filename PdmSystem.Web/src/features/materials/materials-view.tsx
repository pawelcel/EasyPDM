import { useEffect, useMemo, useState, type ReactElement } from "react"
import { Pencil, Trash2 } from "lucide-react"

import { api, ApiError } from "@/api/client"
import type { Material } from "@/api/types"
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
  const [subgroupFilter, setSubgroupFilter] = useState("")

  const existingGroups = useMemo(
    () => Array.from(new Set(materials.map((m) => m.group).filter((g): g is string => !!g))).sort(),
    [materials]
  )
  const existingSubgroups = useMemo(
    () =>
      Array.from(new Set(materials.map((m) => m.subgroup).filter((s): s is string => !!s))).sort(),
    [materials]
  )
  // Podpowiedzi podgrup do filtra zawężone do wybranej grupy (jeśli jest wybrana) — nie ma
  // sensu proponować podgrupy, której w ogóle nie ma w tej grupie.
  const filterableSubgroups = useMemo(
    () =>
      Array.from(
        new Set(
          materials
            .filter((m) => !groupFilter || m.group === groupFilter)
            .map((m) => m.subgroup)
            .filter((s): s is string => !!s)
        )
      ).sort(),
    [materials, groupFilter]
  )

  const visibleMaterials = materials.filter(
    (m) => (!groupFilter || m.group === groupFilter) && (!subgroupFilter || m.subgroup === subgroupFilter)
  )

  async function remove(id: number) {
    await api.removeMaterial(id)
    await refetch()
  }

  return (
    <div className="mx-auto max-w-3xl">
      <h2 className="mb-4 text-lg font-semibold tracking-tight">Lista materiałów</h2>

      <div className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
        <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
          <MaterialDialog
            trigger={<Button>+ Dodaj materiał</Button>}
            title="Dodaj materiał"
            confirmLabel="Dodaj"
            existingGroups={existingGroups}
            existingSubgroups={existingSubgroups}
            onSubmit={async (body) => {
              await api.addMaterial(body)
              await refetch()
            }}
          />

          <div className="flex flex-wrap items-center gap-2">
            {existingGroups.length > 0 && (
              <>
                <Label className="text-xs whitespace-nowrap">Grupa:</Label>
                <Select
                  value={groupFilter || "all"}
                  onValueChange={(v) => {
                    setGroupFilter(v === "all" ? "" : (v as string))
                    setSubgroupFilter("")
                  }}
                >
                  <SelectTrigger className="min-w-48 max-w-64">
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
              </>
            )}

            {filterableSubgroups.length > 0 && (
              <>
                <Label className="text-xs whitespace-nowrap">Podgrupa:</Label>
                <Select
                  value={subgroupFilter || "all"}
                  onValueChange={(v) => setSubgroupFilter(v === "all" ? "" : (v as string))}
                >
                  <SelectTrigger className="min-w-48 max-w-64">
                    <SelectValue>
                      {(v: string) => (v === "all" || !v ? "Wszystkie podgrupy" : v)}
                    </SelectValue>
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="all">Wszystkie podgrupy</SelectItem>
                    {filterableSubgroups.map((s) => (
                      <SelectItem key={s} value={s}>
                        {s}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </>
            )}
          </div>
        </div>

        {visibleMaterials.length > 0 ? (
          <ul className="flex flex-col gap-0.5">
            {visibleMaterials.map((m) => (
              <li
                key={m.id}
                className="flex items-center justify-between gap-2 rounded-md px-2 py-1.5 text-sm hover:bg-accent"
              >
                <span className="truncate">
                  {m.name}
                  {(m.group || m.subgroup) && (
                    <span className="ml-2 text-[12.5px] text-muted-foreground">
                      {[m.group, m.subgroup].filter(Boolean).join(" / ")}
                    </span>
                  )}
                </span>
                <div className="flex shrink-0 items-center gap-0.5">
                  <MaterialDialog
                    trigger={
                      <Button size="icon-xs" variant="ghost" aria-label={`Edytuj ${m.name}`}>
                        <Pencil className="size-3.5 text-muted-foreground" />
                      </Button>
                    }
                    title="Edytuj materiał"
                    confirmLabel="Zapisz"
                    initial={m}
                    existingGroups={existingGroups}
                    existingSubgroups={existingSubgroups}
                    onSubmit={async (body) => {
                      await api.updateMaterial(m.id, body)
                      await refetch()
                    }}
                  />
                  <Button
                    size="icon-xs"
                    variant="ghost"
                    aria-label={`Usuń ${m.name}`}
                    onClick={() => remove(m.id)}
                  >
                    <Trash2 className="size-3.5 text-muted-foreground" />
                  </Button>
                </div>
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

function MaterialDialog({
  trigger,
  title,
  confirmLabel,
  initial,
  existingGroups,
  existingSubgroups,
  onSubmit,
}: {
  trigger: ReactElement
  title: string
  confirmLabel: string
  initial?: Material
  existingGroups: string[]
  existingSubgroups: string[]
  onSubmit: (body: { name: string; group: string | null; subgroup: string | null }) => Promise<void>
}) {
  const [open, setOpen] = useState(false)
  const [name, setName] = useState(initial?.name ?? "")
  const [group, setGroup] = useState(initial?.group ?? "")
  const [subgroup, setSubgroup] = useState(initial?.subgroup ?? "")
  const [error, setError] = useState("")

  // Ten sam komponent obsługuje dodawanie i edycję wielu wierszy naraz (jedna instancja na
  // materiał) — jeśli "initial" się zmieni (np. po odświeżeniu listy po zapisie), pola mają
  // podążyć za nowymi danymi, a nie zostać przy tym, co było przy montowaniu.
  useEffect(() => {
    setName(initial?.name ?? "")
    setGroup(initial?.group ?? "")
    setSubgroup(initial?.subgroup ?? "")
  }, [initial])

  function reset() {
    setName(initial?.name ?? "")
    setGroup(initial?.group ?? "")
    setSubgroup(initial?.subgroup ?? "")
    setError("")
  }

  async function submit() {
    const trimmed = name.trim()
    if (!trimmed) {
      setError("Nazwa materiału jest wymagana.")
      return
    }
    setError("")
    try {
      await onSubmit({ name: trimmed, group: group.trim() || null, subgroup: subgroup.trim() || null })
      setOpen(false)
      reset()
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        setError("Materiał o tej nazwie już istnieje.")
      } else {
        setError("Nie udało się zapisać materiału.")
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
      <DialogTrigger render={trigger} />
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
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
            placeholder="np. Stal"
          />
          <datalist id="material-group-options">
            {existingGroups.map((g) => (
              <option key={g} value={g} />
            ))}
          </datalist>

          <Label htmlFor="material-subgroup">Podgrupa (opcjonalnie)</Label>
          <Input
            id="material-subgroup"
            value={subgroup}
            onChange={(e) => setSubgroup(e.target.value)}
            list="material-subgroup-options"
            placeholder="np. Węglowe"
          />
          <datalist id="material-subgroup-options">
            {existingSubgroups.map((s) => (
              <option key={s} value={s} />
            ))}
          </datalist>

          <Hint>
            Grupa i podgrupa to tylko pomoc przy filtrowaniu tej listy — nie trafiają do
            właściwości Części.
          </Hint>

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

export { MaterialsView }
