import { useEffect, useState } from "react"

import { api } from "@/api/client"
import { canEditOwnerLocked, isLocked, type Item } from "@/api/types"
import { useAuth } from "@/features/auth/use-auth"
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
import { ManufacturerField, MaterialField, PropField } from "@/features/items/property-fields"
import { useLanguage } from "@/i18n/use-language"

const CURRENCIES = [
  { value: "PLN", symbol: "zł" },
  { value: "EUR", symbol: "€" },
  { value: "USD", symbol: "$" },
]

// Rodzaj/Nazwa/Materiał — wydzielone z reszty formularza, bo pokazują się od razu w
// nagłówku panelu (obok podglądu), nie dopiero w sekcji "Właściwości" niżej.
function PartSummaryFields({
  item,
  onChanged,
}: {
  item: Item
  onChanged: () => void | Promise<void>
}) {
  const { t } = useLanguage()
  const { user } = useAuth()
  const rodzaj = typeof item.properties.rodzaj === "string" ? item.properties.rodzaj : ""
  // "Rodzaj" (i pola od niego zależne, w tym Materiał) to koncepcja WYŁĄCZNIE Części —
  // Złożenia go nie mają w ogóle, dostają tu tylko pole nazwy (i ewentualnie Masę, patrz
  // add-node-dialog.tsx przy tworzeniu — do edycji Masy istniejącego Złożenia służy
  // generyczny PropertyEditor w sekcji "Właściwości").
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

      {!isAssembly && (
        <>
          <Label>{t("part.kind")}</Label>
          <div className="flex flex-wrap gap-1.5">
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
        </>
      )}

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

      {!isAssembly && (rodzaj === "Wykonywana" || rodzaj === "Normalia") && (
        <MaterialField
          value={typeof item.properties.material === "string" ? item.properties.material : ""}
          onSave={saveField}
          disabled={locked}
        />
      )}

      {!isAssembly && !rodzaj && <Hint>{t("part.selectKindHint")}</Hint>}
    </div>
  )
}

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
  const statusLocked = isLocked(item)
  const ownerBlocked = user ? !canEditOwnerLocked(item, user.id) : false
  const locked = statusLocked || ownerBlocked

  async function saveField(key: string, value: string) {
    await api.updateProperties(item.id, { [key]: value })
    await onChanged()
  }

  function propValue(key: string): string {
    const stored = item.properties[key]
    return typeof stored === "string" ? stored : stored === undefined || stored === null ? "" : String(stored)
  }

  return (
    <div className="flex flex-col gap-2">
      {rodzaj === "Wykonywana" && (
        <>
          <PriceRow item={item} onChanged={onChanged} />
          <PropField label={t("part.notes")} propKey="notes" value={propValue("notes")} onSave={saveField} disabled={locked} />
        </>
      )}

      {rodzaj === "Zakupowa" && (
        <>
          <ManufacturerField value={propValue("manufacturer")} onSave={saveField} disabled={locked} />
          <PropField label={t("part.orderNumber")} propKey="orderNumber" value={propValue("orderNumber")} onSave={saveField} disabled={locked} />
          <PropField label={t("part.orderNumber2")} propKey="orderNumber2" value={propValue("orderNumber2")} onSave={saveField} disabled={locked} />
          <PropField label={t("part.mass")} propKey="mass" value={propValue("mass")} onSave={saveField} type="number" disabled={locked} />
          <PriceRow item={item} onChanged={onChanged} />
          <PropField label={t("part.notes")} propKey="notes" value={propValue("notes")} onSave={saveField} disabled={locked} />
        </>
      )}

      {rodzaj === "Normalia" && (
        <>
          <PropField label={t("part.norm")} propKey="norm" value={propValue("norm")} onSave={saveField} disabled={locked} />
          <PropField label={t("part.notes")} propKey="notes" value={propValue("notes")} onSave={saveField} disabled={locked} />
        </>
      )}

      {rodzaj === "Klienta" && (
        <PropField label={t("part.notes")} propKey="notes" value={propValue("notes")} onSave={saveField} disabled={locked} />
      )}
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

export { PartPropertyForm, PartSummaryFields }
