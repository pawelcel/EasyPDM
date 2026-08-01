import { useEffect, useMemo, useState } from "react"

import { api } from "@/api/client"
import { isLocked, type Item } from "@/api/types"
import { Button } from "@/components/ui/button"
import {
  Combobox,
  ComboboxContent,
  ComboboxEmpty,
  ComboboxInput,
  ComboboxItem,
  ComboboxList,
} from "@/components/ui/combobox"
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
  const rodzaj = typeof item.properties.rodzaj === "string" ? item.properties.rodzaj : ""
  const locked = isLocked(item)

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
      {locked && (
        <Hint>Właściwości (poza ceną) można edytować tylko w statusie „W pracy”.</Hint>
      )}

      <Label>Rodzaj</Label>
      <div className="flex gap-1.5">
        <Button
          size="sm"
          variant={rodzaj === "Wykonywana" ? "default" : "outline"}
          disabled={locked}
          onClick={() => changeRodzaj("Wykonywana")}
        >
          Wykonywana
        </Button>
        <Button
          size="sm"
          variant={rodzaj === "Zakupowa" ? "default" : "outline"}
          disabled={locked}
          onClick={() => changeRodzaj("Zakupowa")}
        >
          Zakupowa
        </Button>
      </div>

      <Label htmlFor="part-name">Nazwa</Label>
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
          <MaterialField item={item} onSave={saveField} disabled={locked} />
          <PriceRow item={item} onChanged={onChanged} />
          <PropField label="Dodatkowe informacje" propKey="notes" item={item} onSave={saveField} disabled={locked} />
        </>
      )}

      {rodzaj === "Zakupowa" && (
        <>
          <PropField label="Producent" propKey="manufacturer" item={item} onSave={saveField} disabled={locked} />
          <PropField label="Numer zamówieniowy" propKey="orderNumber" item={item} onSave={saveField} disabled={locked} />
          <PropField label="Numer zamówieniowy 2" propKey="orderNumber2" item={item} onSave={saveField} disabled={locked} />
          <PropField label="Masa [kg]" propKey="mass" item={item} onSave={saveField} type="number" disabled={locked} />
          <PriceRow item={item} onChanged={onChanged} />
          <PropField label="Dodatkowe informacje" propKey="notes" item={item} onSave={saveField} disabled={locked} />
        </>
      )}

      {!rodzaj && <Hint>Wybierz rodzaj, żeby zobaczyć właściwości części.</Hint>}
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
      <Label>Materiał</Label>
      {materials.length > 0 ? (
        <>
          {existingGroups.length > 0 && (
            <div className="flex gap-1.5">
              <Select
                value={groupFilter || "all"}
                onValueChange={(v) => {
                  setGroupFilter(v === "all" ? "" : (v as string))
                  setSubgroupFilter("")
                }}
                disabled={disabled}
              >
                <SelectTrigger className="flex-1">
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

              {filterableSubgroups.length > 0 && (
                <Select
                  value={subgroupFilter || "all"}
                  onValueChange={(v) => setSubgroupFilter(v === "all" ? "" : (v as string))}
                  disabled={disabled}
                >
                  <SelectTrigger className="flex-1">
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
              )}
            </div>
          )}

          <Combobox
            items={filteredMaterialNames}
            value={value || null}
            onValueChange={(v) => onSave("material", (v as string | null) ?? "")}
            itemToStringLabel={(name: string) => name}
            disabled={disabled}
          >
            <ComboboxInput placeholder="Wpisz nazwę, żeby wyszukać…" showClear />
            <ComboboxContent>
              <ComboboxEmpty>Brak pasujących materiałów.</ComboboxEmpty>
              <ComboboxList>
                {(name: string) => <ComboboxItem key={name} value={name}>{name}</ComboboxItem>}
              </ComboboxList>
            </ComboboxContent>
          </Combobox>
        </>
      ) : (
        <Hint>Brak materiałów — dodaj je w panelu bocznym („Lista materiałów”).</Hint>
      )}
    </>
  )
}

function PriceRow({
  item,
  onChanged,
}: {
  item: Item
  onChanged: () => void | Promise<void>
}) {
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
      <Label htmlFor="part-price">Cena</Label>
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
          className="flex-1"
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
            <SelectItem value="Netto">Netto</SelectItem>
            <SelectItem value="Brutto">Brutto</SelectItem>
          </SelectContent>
        </Select>
      </div>

      <div className="-mt-1 text-[12.5px] text-muted-foreground">
        {priceDate && `Cena wprowadzona: ${priceDate}`}
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
      />
    </>
  )
}

export { PartPropertyForm }
