import { useEffect, useMemo, useState } from "react"
import { Info } from "lucide-react"

import { api } from "@/api/client"
import type { ManufacturerDetail } from "@/api/types"
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
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog"
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
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { useMaterials } from "@/features/materials/use-materials"
import { useManufacturers } from "@/features/manufacturers/use-manufacturers"
import { useLanguage } from "@/i18n/use-language"

// Pola współdzielone między PartPropertyForm (edycja już istniejącej Części/Złożenia —
// onSave od razu zapisuje przez API) i AddNodeDialog (tworzenie nowego elementu — onSave
// tylko aktualizuje lokalny stan, zapis dopiero przy właściwym POST /nodes). Dlatego
// przyjmują gołą "value"/"onSave" zamiast całego "item" — nie zakładają, że element już
// istnieje w bazie.

function MaterialField({
  value,
  onSave,
  disabled,
  onError,
}: {
  value: string
  onSave: (key: string, value: string) => void | Promise<void>
  disabled: boolean
  onError?: (message: string | null) => void
}) {
  const { t } = useLanguage()
  const { materials } = useMaterials()

  async function save(next: string) {
    try {
      onError?.(null)
      await onSave("material", next)
    } catch (err) {
      onError?.(err instanceof Error ? err.message : t("part.saveFieldFailed"))
    }
  }

  // Grupa/podgrupa tu to wyłącznie pomoc przy zawężaniu wyboru materiału poniżej — nie są
  // same w sobie zapisywane we właściwościach Części (ta zapisuje tylko nazwę materiału).
  const [groupFilter, setGroupFilter] = useState("")
  const [subgroupFilter, setSubgroupFilter] = useState("")

  // Materiał ma z definicji jedną, konkretną grupę/podgrupę w katalogu — jeśli materiał
  // wybrano wprost z wyszukiwarki (z pominięciem filtrów wyżej, np. przez wpisanie nazwy),
  // te dwa Selecty muszą dogonić rzeczywistą grupę/podgrupę TEGO materiału, zamiast zostać
  // na "Wszystkie grupy"/"Wszystkie podgrupy" — inaczej wyglądałoby, jakby wybór materiału
  // nie ustawiał w ogóle grupy/podgrupy.
  useEffect(() => {
    if (!value) return
    const selected = materials.find((m) => m.name === value)
    if (!selected) return
    setGroupFilter(selected.group ?? "")
    setSubgroupFilter(selected.subgroup ?? "")
  }, [value, materials])

  const existingGroups = useMemo(
    () => Array.from(new Set(materials.map((m) => m.group).filter((g): g is string => !!g))).sort(),
    [materials]
  )
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
  const filteredMaterialNames = materials
    .filter(
      (m) => (!groupFilter || m.group === groupFilter) && (!subgroupFilter || m.subgroup === subgroupFilter)
    )
    .map((m) => m.name)

  return (
    <>
      <Label>{t("part.material")}</Label>
      {materials.length > 0 ? (
        <div className="flex flex-col gap-1.5">
          <Combobox
            items={filteredMaterialNames}
            value={value || null}
            onValueChange={(v) => save((v as string | null) ?? "")}
            itemToStringLabel={(name: string) => name}
            disabled={disabled}
          >
            <ComboboxInput placeholder={t("part.searchPlaceholder")} showClear />
            <ComboboxContent>
              <ComboboxEmpty>{t("part.noMatchingMaterials")}</ComboboxEmpty>
              <ComboboxList>
                {(name: string) => <ComboboxItem key={name} value={name}>{name}</ComboboxItem>}
              </ComboboxList>
            </ComboboxContent>
          </Combobox>

          {existingGroups.length > 0 && (
            <div className="flex min-w-0 gap-1.5">
              <div className="min-w-0 flex-1">
                <Select
                  value={groupFilter || "all"}
                  onValueChange={(v) => {
                    setGroupFilter(v === "all" ? "" : (v as string))
                    setSubgroupFilter("")
                  }}
                  disabled={disabled}
                >
                  <SelectTrigger className="w-full">
                    <SelectValue>
                      {(v: string) => (v === "all" || !v ? t("material.allGroups") : v)}
                    </SelectValue>
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="all">{t("material.allGroups")}</SelectItem>
                    {existingGroups.map((g) => (
                      <SelectItem key={g} value={g}>
                        {g}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>

              <div className="min-w-0 flex-1">
                <Select
                  value={subgroupFilter || "all"}
                  onValueChange={(v) => setSubgroupFilter(v === "all" ? "" : (v as string))}
                  disabled={disabled || filterableSubgroups.length === 0}
                >
                  <SelectTrigger className="w-full">
                    <SelectValue>
                      {(v: string) => (v === "all" || !v ? t("material.allSubgroups") : v)}
                    </SelectValue>
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="all">{t("material.allSubgroups")}</SelectItem>
                    {filterableSubgroups.map((s) => (
                      <SelectItem key={s} value={s}>
                        {s}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            </div>
          )}
        </div>
      ) : (
        <Hint>{t("part.noMaterialsHint")}</Hint>
      )}
    </>
  )
}

function ManufacturerField({
  value,
  onSave,
  disabled,
  onError,
}: {
  value: string
  onSave: (key: string, value: string) => void | Promise<void>
  disabled: boolean
  onError?: (message: string | null) => void
}) {
  const { t } = useLanguage()
  const { manufacturers } = useManufacturers("")
  const manufacturerNames = manufacturers.map((m) => m.name)
  const matched = manufacturers.find((m) => m.name === value)

  async function save(next: string) {
    try {
      onError?.(null)
      await onSave("manufacturer", next)
    } catch (err) {
      onError?.(err instanceof Error ? err.message : t("part.saveFieldFailed"))
    }
  }

  return (
    <>
      <Label>{t("part.manufacturer")}</Label>
      {manufacturers.length > 0 ? (
        <div className="flex gap-1.5">
          <div className="flex-1">
            <Combobox
              items={manufacturerNames}
              value={value || null}
              onValueChange={(v) => save((v as string | null) ?? "")}
              itemToStringLabel={(name: string) => name}
              disabled={disabled}
            >
              <ComboboxInput placeholder={t("part.searchPlaceholder")} showClear />
              <ComboboxContent>
                <ComboboxEmpty>{t("part.noMatchingManufacturers")}</ComboboxEmpty>
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
          <ManufacturerInfoButton manufacturerId={matched?.id ?? null} />
        </div>
      ) : (
        <Hint>{t("part.noManufacturersHint")}</Hint>
      )}
    </>
  )
}

function ManufacturerInfoButton({ manufacturerId }: { manufacturerId: number | null }) {
  const { t } = useLanguage()
  const [open, setOpen] = useState(false)
  const [detail, setDetail] = useState<ManufacturerDetail | null>(null)

  useEffect(() => {
    if (!open || manufacturerId === null) return
    api.getManufacturer(manufacturerId).then(setDetail)
  }, [open, manufacturerId])

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next)
        if (!next) setDetail(null)
      }}
    >
      <DialogTrigger
        render={
          <Button
            size="icon"
            variant="outline"
            disabled={manufacturerId === null}
            aria-label={t("part.viewManufacturerAria")}
          >
            <Info className="size-4" />
          </Button>
        }
      />
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{detail?.name ?? t("part.manufacturer")}</DialogTitle>
        </DialogHeader>
        {detail ? (
          detail.contacts.length > 0 ? (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>{t("common.fullName")}</TableHead>
                  <TableHead>{t("common.position")}</TableHead>
                  <TableHead>{t("common.phone")}</TableHead>
                  <TableHead>{t("common.email")}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {detail.contacts.map((c) => (
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
          )
        ) : (
          <Hint>{t("common.loading")}</Hint>
        )}
      </DialogContent>
    </Dialog>
  )
}

function PropField({
  label,
  propKey,
  value,
  onSave,
  type = "text",
  disabled = false,
  onError,
}: {
  label: string
  propKey: string
  value: string
  onSave: (key: string, value: string) => void | Promise<void>
  type?: "text" | "number"
  disabled?: boolean
  onError?: (message: string | null) => void
}) {
  const { t } = useLanguage()
  const [localValue, setLocalValue] = useState(value)
  useEffect(() => setLocalValue(value), [value])

  async function save() {
    if (localValue === value) return
    try {
      onError?.(null)
      await onSave(propKey, localValue)
    } catch (err) {
      setLocalValue(value)
      onError?.(err instanceof Error ? err.message : t("part.saveFieldFailed"))
    }
  }

  return (
    <>
      <Label htmlFor={`part-prop-${propKey}`}>{label}</Label>
      <Input
        id={`part-prop-${propKey}`}
        type={type}
        step={type === "number" ? "any" : undefined}
        value={localValue}
        disabled={disabled}
        onChange={(e) => setLocalValue(e.target.value)}
        onBlur={save}
        onKeyDown={(e) => {
          if (e.key === "Enter") (e.target as HTMLInputElement).blur()
        }}
        className={
          type === "number"
            ? "[appearance:textfield] [&::-webkit-inner-spin-button]:appearance-none [&::-webkit-outer-spin-button]:appearance-none"
            : undefined
        }
      />
    </>
  )
}

export { ManufacturerField, MaterialField, PropField }
