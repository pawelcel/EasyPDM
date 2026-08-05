import { useEffect, useMemo, useState } from "react"
import { Info } from "lucide-react"

import { api } from "@/api/client"
import { canEditOwnerLocked, isLocked, type Item, type ManufacturerDetail } from "@/api/types"
import { useAuth } from "@/features/auth/use-auth"
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

const CURRENCIES = [
  { value: "PLN", symbol: "zł" },
  { value: "EUR", symbol: "€" },
  { value: "USD", symbol: "$" },
]

function PartPropertyForm({
  item,
  onChanged,
}: {
  item: Item
  onChanged: () => void | Promise<void>
}) {
  const { t } = useLanguage()
  const { user } = useAuth()
  const rodzaj = typeof item.properties.rodzaj === "string" ? item.properties.rodzaj : ""
  // Materiał dotyczy tylko Części — Złożenia mogą mieć Masę, ale nie Materiał (w
  // odróżnieniu od reszty pól zależnych od "rodzaju", które dla obu typów działają tak samo).
  const isAssembly = item.itemType === "assembly"
  const statusLocked = isLocked(item)
  const ownerBlocked = user ? !canEditOwnerLocked(item, user.id) : false
  const locked = statusLocked || ownerBlocked

  const [name, setName] = useState(item.fileName)
  useEffect(() => setName(item.fileName), [item.fileName])

  async function saveName() {
    const trimmed = name.trim()
    if (!trimmed || trimmed === item.fileName) {
      setName(item.fileName)
      return
    }
    await api.renameItem(item.id, trimmed)
    await onChanged()
  }

  async function changeRodzaj(next: string) {
    await api.updateProperties(item.id, { rodzaj: next })
    await onChanged()
  }

  async function saveField(key: string, value: string) {
    await api.updateProperties(item.id, { [key]: value })
    await onChanged()
  }

  return (
    <div className="flex flex-col gap-2">
      {statusLocked && <Hint>{t("part.lockedHint")}</Hint>}
      {ownerBlocked && !statusLocked && <Hint>{t("item.ownerLockedHint")}</Hint>}

      <Label>{t("part.kind")}</Label>
      <div className="flex gap-1.5">
        <Button
          size="sm"
          variant={rodzaj === "Wykonywana" ? "default" : "outline"}
          disabled={locked}
          onClick={() => changeRodzaj("Wykonywana")}
        >
          {t("part.kindManufactured")}
        </Button>
        <Button
          size="sm"
          variant={rodzaj === "Zakupowa" ? "default" : "outline"}
          disabled={locked}
          onClick={() => changeRodzaj("Zakupowa")}
        >
          {t("part.kindPurchased")}
        </Button>
        <Button
          size="sm"
          variant={rodzaj === "Normalia" ? "default" : "outline"}
          disabled={locked}
          onClick={() => changeRodzaj("Normalia")}
        >
          {t("part.kindStandard")}
        </Button>
        <Button
          size="sm"
          variant={rodzaj === "Klienta" ? "default" : "outline"}
          disabled={locked}
          onClick={() => changeRodzaj("Klienta")}
        >
          {t("part.kindClient")}
        </Button>
      </div>

      <Label htmlFor="part-name">{t("common.name")}</Label>
      <Input
        id="part-name"
        value={name}
        disabled={locked}
        onChange={(e) => setName(e.target.value)}
        onBlur={saveName}
        onKeyDown={(e) => {
          if (e.key === "Enter") (e.target as HTMLInputElement).blur()
        }}
      />

      {rodzaj === "Wykonywana" && (
        <>
          {!isAssembly && <MaterialField item={item} onSave={saveField} disabled={locked} />}
          <PriceRow item={item} onChanged={onChanged} />
          <PropField label={t("part.notes")} propKey="notes" item={item} onSave={saveField} disabled={locked} />
        </>
      )}

      {rodzaj === "Zakupowa" && (
        <>
          <ManufacturerField item={item} onSave={saveField} disabled={locked} />
          <PropField label={t("part.orderNumber")} propKey="orderNumber" item={item} onSave={saveField} disabled={locked} />
          <PropField label={t("part.orderNumber2")} propKey="orderNumber2" item={item} onSave={saveField} disabled={locked} />
          <PropField label={t("part.mass")} propKey="mass" item={item} onSave={saveField} type="number" disabled={locked} />
          <PriceRow item={item} onChanged={onChanged} />
          <PropField label={t("part.notes")} propKey="notes" item={item} onSave={saveField} disabled={locked} />
        </>
      )}

      {rodzaj === "Normalia" && (
        <>
          {!isAssembly && <MaterialField item={item} onSave={saveField} disabled={locked} />}
          <PropField label={t("part.norm")} propKey="norm" item={item} onSave={saveField} disabled={locked} />
          <PropField label={t("part.notes")} propKey="notes" item={item} onSave={saveField} disabled={locked} />
        </>
      )}

      {rodzaj === "Klienta" && (
        <PropField label={t("part.notes")} propKey="notes" item={item} onSave={saveField} disabled={locked} />
      )}

      {!rodzaj && <Hint>{t("part.selectKindHint")}</Hint>}
    </div>
  )
}

function MaterialField({
  item,
  onSave,
  disabled,
}: {
  item: Item
  onSave: (key: string, value: string) => void | Promise<void>
  disabled: boolean
}) {
  const { t } = useLanguage()
  const { materials } = useMaterials()
  const stored = item.properties.material
  const value = typeof stored === "string" ? stored : ""

  // Grupa/podgrupa tu to wyłącznie pomoc przy zawężaniu wyboru materiału poniżej — nie są
  // same w sobie zapisywane we właściwościach Części (ta zapisuje tylko nazwę materiału).
  const [groupFilter, setGroupFilter] = useState("")
  const [subgroupFilter, setSubgroupFilter] = useState("")

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
        <div className="flex gap-1.5">
          <div className="min-w-0 flex-1">
            <Combobox
              items={filteredMaterialNames}
              value={value || null}
              onValueChange={(v) => onSave("material", (v as string | null) ?? "")}
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
          </div>

          {existingGroups.length > 0 && (
            <Select
              value={groupFilter || "all"}
              onValueChange={(v) => {
                setGroupFilter(v === "all" ? "" : (v as string))
                setSubgroupFilter("")
              }}
              disabled={disabled}
            >
              <SelectTrigger className="w-44 shrink-0">
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
          )}

          {existingGroups.length > 0 && (
            <Select
              value={subgroupFilter || "all"}
              onValueChange={(v) => setSubgroupFilter(v === "all" ? "" : (v as string))}
              disabled={disabled || filterableSubgroups.length === 0}
            >
              <SelectTrigger className="w-44 shrink-0">
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
          )}
        </div>
      ) : (
        <Hint>{t("part.noMaterialsHint")}</Hint>
      )}
    </>
  )
}

function ManufacturerField({
  item,
  onSave,
  disabled,
}: {
  item: Item
  onSave: (key: string, value: string) => void | Promise<void>
  disabled: boolean
}) {
  const { t } = useLanguage()
  const { manufacturers } = useManufacturers("")
  const stored = item.properties.manufacturer
  const value = typeof stored === "string" ? stored : ""
  const manufacturerNames = manufacturers.map((m) => m.name)
  const matched = manufacturers.find((m) => m.name === value)

  return (
    <>
      <Label>{t("part.manufacturer")}</Label>
      {manufacturers.length > 0 ? (
        <div className="flex gap-1.5">
          <div className="flex-1">
            <Combobox
              items={manufacturerNames}
              value={value || null}
              onValueChange={(v) => onSave("manufacturer", (v as string | null) ?? "")}
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

function PriceRow({
  item,
  onChanged,
}: {
  item: Item
  onChanged: () => void | Promise<void>
}) {
  const { t } = useLanguage()
  const stored = item.properties.price
  const initial = stored === undefined || stored === null ? "" : String(stored)
  const currency = typeof item.properties.currency === "string" ? item.properties.currency : "PLN"
  const priceType = typeof item.properties.priceType === "string" ? item.properties.priceType : ""
  const priceDate = typeof item.properties.priceDate === "string" ? item.properties.priceDate : ""

  const [price, setPrice] = useState(initial)
  useEffect(() => setPrice(initial), [initial])

  async function save(fields: Record<string, string>) {
    await api.updateProperties(item.id, {
      ...fields,
      priceDate: new Date().toISOString().slice(0, 10),
    })
    await onChanged()
  }

  return (
    <>
      <Label htmlFor="part-price">{t("part.price")}</Label>
      <div className="flex gap-1.5">
        <Input
          id="part-price"
          type="number"
          step="any"
          value={price}
          onChange={(e) => setPrice(e.target.value)}
          onBlur={() => {
            if (price !== initial) save({ price })
          }}
          onKeyDown={(e) => {
            if (e.key === "Enter") (e.target as HTMLInputElement).blur()
          }}
          className="flex-1 [appearance:textfield] [&::-webkit-inner-spin-button]:appearance-none [&::-webkit-outer-spin-button]:appearance-none"
        />

        <Select value={currency} onValueChange={(v) => save({ currency: v as string })}>
          <SelectTrigger className="w-16">
            <SelectValue>
              {(v: string) => CURRENCIES.find((c) => c.value === v)?.symbol ?? v}
            </SelectValue>
          </SelectTrigger>
          <SelectContent>
            {CURRENCIES.map((c) => (
              <SelectItem key={c.value} value={c.value}>
                {c.symbol} {c.value}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>

        <Select
          value={priceType || "none"}
          onValueChange={(v) => save({ priceType: v === "none" ? "" : (v as string) })}
        >
          <SelectTrigger className="w-24">
            <SelectValue>{(v: string) => (v === "none" || !v ? "—" : v)}</SelectValue>
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="none">—</SelectItem>
            <SelectItem value="Netto">{t("part.priceNetto")}</SelectItem>
            <SelectItem value="Brutto">{t("part.priceBrutto")}</SelectItem>
          </SelectContent>
        </Select>
      </div>

      <div className="-mt-1 text-[12.5px] text-muted-foreground">
        {priceDate && t("part.priceEnteredOn", { date: priceDate })}
      </div>
    </>
  )
}

function PropField({
  label,
  propKey,
  item,
  onSave,
  type = "text",
  disabled = false,
}: {
  label: string
  propKey: string
  item: Item
  onSave: (key: string, value: string) => void | Promise<void>
  type?: "text" | "number"
  disabled?: boolean
}) {
  const stored = item.properties[propKey]
  const initial = stored === undefined || stored === null ? "" : String(stored)
  const [value, setValue] = useState(initial)
  useEffect(() => setValue(initial), [initial])

  return (
    <>
      <Label htmlFor={`part-prop-${propKey}`}>{label}</Label>
      <Input
        id={`part-prop-${propKey}`}
        type={type}
        step={type === "number" ? "any" : undefined}
        value={value}
        disabled={disabled}
        onChange={(e) => setValue(e.target.value)}
        onBlur={() => {
          if (value !== initial) onSave(propKey, value)
        }}
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

export { PartPropertyForm }
