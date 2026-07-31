import { useEffect, useState } from "react"

import { api } from "@/api/client"
import type { Item } from "@/api/types"
import { Button } from "@/components/ui/button"
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
      <Label>Rodzaj</Label>
      <div className="flex gap-1.5">
        <Button
          size="sm"
          variant={rodzaj === "Wykonywana" ? "default" : "outline"}
          onClick={() => changeRodzaj("Wykonywana")}
        >
          Wykonywana
        </Button>
        <Button
          size="sm"
          variant={rodzaj === "Zakupowa" ? "default" : "outline"}
          onClick={() => changeRodzaj("Zakupowa")}
        >
          Zakupowa
        </Button>
      </div>

      <Label htmlFor="part-name">Nazwa</Label>
      <Input
        id="part-name"
        value={name}
        onChange={(e) => setName(e.target.value)}
        onBlur={saveName}
        onKeyDown={(e) => {
          if (e.key === "Enter") (e.target as HTMLInputElement).blur()
        }}
      />

      {rodzaj === "Wykonywana" && (
        <>
          <PropField label="Materiał" propKey="material" item={item} onSave={saveField} />
          <PriceRow item={item} onChanged={onChanged} />
          <PropField label="Dodatkowe informacje" propKey="notes" item={item} onSave={saveField} />
        </>
      )}

      {rodzaj === "Zakupowa" && (
        <>
          <PropField label="Producent" propKey="manufacturer" item={item} onSave={saveField} />
          <PropField label="Numer zamówieniowy" propKey="orderNumber" item={item} onSave={saveField} />
          <PropField label="Numer zamówieniowy 2" propKey="orderNumber2" item={item} onSave={saveField} />
          <PropField label="Masa [kg]" propKey="mass" item={item} onSave={saveField} type="number" />
          <PriceRow item={item} onChanged={onChanged} />
          <PropField label="Dodatkowe informacje" propKey="notes" item={item} onSave={saveField} />
        </>
      )}

      {!rodzaj && <Hint>Wybierz rodzaj, żeby zobaczyć właściwości części.</Hint>}
    </div>
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
}: {
  label: string
  propKey: string
  item: Item
  onSave: (key: string, value: string) => void | Promise<void>
  type?: "text" | "number"
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
