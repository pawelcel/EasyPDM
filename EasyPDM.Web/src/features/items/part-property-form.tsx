import { useEffect, useState } from "react"

import { api } from "@/api/client"
import { canEditOwnerLocked, isLocked, type Item } from "@/api/types"
import { useAuth } from "@/features/auth/use-auth"
import { Button } from "@/components/ui/button"
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
import {
  ManufacturerField,
  MaterialField,
  ProductTypeField,
  PropField,
} from "@/features/items/property-fields"
import type { TranslationKey } from "@/i18n/translations"
import { useLanguage } from "@/i18n/use-language"

const CURRENCIES = [
  { value: "PLN", symbol: "zł" },
  { value: "EUR", symbol: "€" },
  { value: "USD", symbol: "$" },
]

// Dozwolone wartości properties.rodzaj — osobne listy dla Części i Złożenia. Wartości
// Złożenia są w rodzaju nijakim i CELOWO różnią się napisem od odpowiedników Części
// ("Zakupowe" vs "Zakupowa"), bo napis rodzaju jest jednocześnie kluczem numeracji
// (item_number_prefixes); wyjątkiem jest "Klienta", identyczne dla obu, bo taki prefiks
// jest wspólny. Zob. ItemEndpoints.AssemblyPrefixKind po stronie API.
const PART_KINDS: { value: string; labelKey: TranslationKey }[] = [
  { value: "Wykonywana", labelKey: "part.kindManufactured" },
  { value: "Zakupowa", labelKey: "part.kindPurchased" },
  { value: "Normalia", labelKey: "part.kindStandard" },
  { value: "Klienta", labelKey: "part.kindClient" },
]

const ASSEMBLY_KINDS: { value: string; labelKey: TranslationKey }[] = [
  { value: "Wykonywane", labelKey: "assembly.kindManufactured" },
  { value: "Zakupowe", labelKey: "assembly.kindPurchased" },
  { value: "Klienta", labelKey: "assembly.kindClient" },
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
  // Rodzaj mają OBA typy, tylko z innych list (zob. PART_KINDS/ASSEMBLY_KINDS niżej) —
  // Złożenie nie ma odpowiednika "Normalii", a jego wartości są w rodzaju nijakim
  // ("Wykonywane" zamiast "Wykonywana"), żeby dało się je odróżnić w item_number_prefixes.
  // Pola zależne od rodzaju różnią się dalej: Materiał to koncepcja wyłącznie Części, a
  // Masę istniejącego Złożenia edytuje się generycznym PropertyEditorem w "Właściwościach".
  const isAssembly = item.itemType === "assembly"
  const statusLocked = isLocked(item)
  const ownerBlocked = user ? !canEditOwnerLocked(item, user.id) : false
  const locked = statusLocked || ownerBlocked

  const [name, setName] = useState(item.fileName)
  const [error, setError] = useState<string | null>(null)
  useEffect(() => setName(item.fileName), [item.fileName])

  async function saveName() {
    const trimmed = name.trim()
    if (!trimmed || trimmed === item.fileName) {
      setName(item.fileName)
      return
    }
    try {
      setError(null)
      await api.renameItem(item.id, trimmed)
      await onChanged()
    } catch (err) {
      setName(item.fileName)
      setError(err instanceof Error ? err.message : t("item.renameFailed"))
    }
  }

  async function changeRodzaj(next: string) {
    try {
      setError(null)
      await api.updateProperties(item.id, { rodzaj: next })
      await onChanged()
    } catch (err) {
      setError(err instanceof Error ? err.message : t("part.saveFieldFailed"))
    }
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
      <div className="flex flex-wrap gap-1.5">
        {(isAssembly ? ASSEMBLY_KINDS : PART_KINDS).map((kind) => (
          <Button
            key={kind.value}
            size="sm"
            variant={rodzaj === kind.value ? "default" : "outline"}
            disabled={locked}
            onClick={() => changeRodzaj(kind.value)}
          >
            {t(kind.labelKey)}
          </Button>
        ))}
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

      {!isAssembly && (rodzaj === "Wykonywana" || rodzaj === "Normalia") && (
        <MaterialField
          value={typeof item.properties.material === "string" ? item.properties.material : ""}
          onSave={saveField}
          disabled={locked}
          onError={setError}
        />
      )}

      {!rodzaj && <Hint>{t("part.selectKindHint")}</Hint>}
      <FormError>{error}</FormError>
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
  const [error, setError] = useState<string | null>(null)

  async function saveField(key: string, value: string) {
    // Typ produktu należy do KONKRETNEGO producenta — po zmianie producenta poprzedni typ
    // byłby już spoza jego katalogu, więc znika razem z nim.
    const fields: Record<string, string> =
      key === "manufacturer" && value !== propValue("manufacturer")
        ? { manufacturer: value, productType: "" }
        : { [key]: value }
    await api.updateProperties(item.id, fields)
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
          <PriceRow item={item} onChanged={onChanged} onError={setError} />
          <PropField label={t("part.notes")} propKey="notes" value={propValue("notes")} onSave={saveField} disabled={locked} onError={setError} />
        </>
      )}

      {rodzaj === "Zakupowa" && (
        <>
          <ManufacturerField value={propValue("manufacturer")} onSave={saveField} disabled={locked} onError={setError} />
          <ProductTypeField
            manufacturerName={propValue("manufacturer")}
            value={propValue("productType")}
            onSave={saveField}
            disabled={locked}
            onError={setError}
          />
          <PropField label={t("part.orderNumber")} propKey="orderNumber" value={propValue("orderNumber")} onSave={saveField} disabled={locked} onError={setError} />
          <PropField label={t("part.orderNumber2")} propKey="orderNumber2" value={propValue("orderNumber2")} onSave={saveField} disabled={locked} onError={setError} />
          <PropField label={t("part.mass")} propKey="mass" value={propValue("mass")} onSave={saveField} type="number" disabled={locked} onError={setError} />
          <PriceRow item={item} onChanged={onChanged} onError={setError} />
          <PropField label={t("part.notes")} propKey="notes" value={propValue("notes")} onSave={saveField} disabled={locked} onError={setError} />
        </>
      )}

      {rodzaj === "Normalia" && (
        <>
          <PropField label={t("part.norm")} propKey="norm" value={propValue("norm")} onSave={saveField} disabled={locked} onError={setError} />
          <PropField label={t("part.notes")} propKey="notes" value={propValue("notes")} onSave={saveField} disabled={locked} onError={setError} />
        </>
      )}

      {rodzaj === "Klienta" && (
        <PropField label={t("part.notes")} propKey="notes" value={propValue("notes")} onSave={saveField} disabled={locked} onError={setError} />
      )}

      {/* Złożenie zakupowe — kupiony podzespół ma producenta i typ produktu dokładnie tak
          samo jak część zakupowa. Reszta jego właściwości (masa, cena, cokolwiek własnego)
          zostaje w generycznym PropertyEditorze pod spodem, zob. item-detail-panel.tsx. */}
      {rodzaj === "Zakupowe" && (
        <>
          <ManufacturerField value={propValue("manufacturer")} onSave={saveField} disabled={locked} onError={setError} />
          <ProductTypeField
            manufacturerName={propValue("manufacturer")}
            value={propValue("productType")}
            onSave={saveField}
            disabled={locked}
            onError={setError}
          />
        </>
      )}

      <FormError>{error}</FormError>
    </div>
  )
}

function PriceRow({
  item,
  onChanged,
  onError,
}: {
  item: Item
  onChanged: () => void | Promise<void>
  onError?: (message: string | null) => void
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
    try {
      onError?.(null)
      await api.updateProperties(item.id, {
        ...fields,
        priceDate: new Date().toISOString().slice(0, 10),
      })
      await onChanged()
    } catch (err) {
      if ("price" in fields) setPrice(initial)
      onError?.(err instanceof Error ? err.message : t("part.saveFieldFailed"))
    }
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
