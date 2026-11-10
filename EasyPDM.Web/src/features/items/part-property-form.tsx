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
  ClientAndName2Fields,
  ManufacturerField,
  MaterialField,
  ProductTypeAndSubtypeFields,
  PropField,
} from "@/features/items/property-fields"
import { useProcesses } from "@/features/processes/use-processes"
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

// Klucze properties NALEŻĄCE do danego rodzaju (patrz pola renderowane w każdym bloku
// PartPropertyForm niżej) — przy zmianie rodzaju te z POPRZEDNIEGO rodzaju są czyszczone
// (ustawiane na ""), żeby nie zostały jako martwe, ale wciąż przeszukiwalne dane (Cała
// baza szuka po CAŁEJ treści properties, nie tylko po polach aktualnie pokazywanych w
// UI — bez tego np. producent z porzuconej Części zakupowej dalej by się wyszukiwał po
// zmianie rodzaju na Normalię). "notes" celowo pominięte — to pole wspólne dla wszystkich
// rodzajów, nie "należy" do żadnego konkretnego. Tak samo "mass" — masę ma Część każdego
// rodzaju i Złożenie, więc zmiana rodzaju nie ma prawa jej kasować.
const PART_KIND_FIELDS: Record<string, string[]> = {
  Wykonywana: ["material", "price", "currency", "priceType", "priceDate", "processes"],
  Zakupowa: [
    "manufacturer",
    "productType",
    "productSubtype",
    "orderNumber",
    "orderNumber2",
    "price",
    "currency",
    "priceType",
    "priceDate",
  ],
  Normalia: ["material", "norm"],
  Klienta: ["client", "clientName2"],
}

// Masa jest polem WSPÓLNYM dla Części każdego rodzaju i dla Złożenia (renderowanym raz, nad
// blokami rodzajów), więc nie ma jej tu na liście do czyszczenia — tak samo jak w
// PART_KIND_FIELDS wyżej.
const ASSEMBLY_KIND_FIELDS: Record<string, string[]> = {
  Wykonywane: ["processes"],
  Zakupowe: ["manufacturer", "productType", "productSubtype"],
  Klienta: ["client", "clientName2"],
}

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
      // Pola POPRZEDNIEGO rodzaju czyszczone razem ze zmianą — inaczej zostają jako martwe,
      // ale wciąż przeszukiwalne w "Całej bazie" (zob. PART_KIND_FIELDS/ASSEMBLY_KIND_FIELDS
      // wyżej).
      const staleFields = (isAssembly ? ASSEMBLY_KIND_FIELDS : PART_KIND_FIELDS)[rodzaj] ?? []
      const cleared = Object.fromEntries(staleFields.map((key) => [key, ""]))
      await api.updateProperties(item.id, { ...cleared, rodzaj: next })
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
      {/* Przyciski zostają widoczne, tylko wyszarzone — żeby było wiadomo, jakie rodzaje
          istnieją i że akurat tego elementu już nie dotyczą. Wyjaśnienie pokazujemy tylko
          wtedy, gdy blokuje WŁAŚNIE to (statusLocked/ownerBlocked mają własne komunikaty
          wyżej), żeby nie mnożyć trzech podpowiedzi naraz. */}
      {item.kindLocked && !locked && <Hint>{t("part.kindLockedHint")}</Hint>}
      <div className="flex flex-wrap gap-1.5">
        {(isAssembly ? ASSEMBLY_KINDS : PART_KINDS).map((kind) => (
          <Button
            key={kind.value}
            size="sm"
            variant={rodzaj === kind.value ? "default" : "outline"}
            disabled={locked || item.kindLocked}
            onClick={() => changeRodzaj(kind.value)}
          >
            {t(kind.labelKey)}
          </Button>
        ))}
      </div>

      <Label htmlFor="part-name">{t("common.name")}</Label>
      {item.nameLocked && !locked && <Hint>{t("part.nameLockedHint")}</Hint>}
      <Input
        id="part-name"
        value={name}
        disabled={locked || item.nameLocked}
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

  async function saveProcesses(next: string[]) {
    try {
      setError(null)
      await api.updateProperties(item.id, { processes: next })
      await onChanged()
    } catch (err) {
      setError(err instanceof Error ? err.message : t("part.saveFieldFailed"))
    }
  }

  async function saveField(key: string, value: string) {
    // Producent → Seria/Typ → Podtyp to łańcuch zależności: każdy poziom należy do
    // konkretnej wartości poziomu wyżej, więc zmiana wyżej unieważnia to, co niżej
    // (inaczej został by podtyp spoza wybranej serii albo seria spoza katalogu producenta).
    let fields: Record<string, string> = { [key]: value }
    if (key === "manufacturer" && value !== propValue("manufacturer")) {
      fields = { manufacturer: value, productType: "", productSubtype: "" }
    } else if (key === "productType" && value !== propValue("productType")) {
      fields = { productType: value, productSubtype: "" }
    } else if (key === "client" && value !== propValue("client")) {
      fields = { client: value, clientName2: "" }
    }
    await api.updateProperties(item.id, fields)
    await onChanged()
  }

  function propValue(key: string): string {
    const stored = item.properties[key]
    return typeof stored === "string" ? stored : stored === undefined || stored === null ? "" : String(stored)
  }

  return (
    <div className="flex flex-col gap-2">
      {/* Masa stoi NAD blokami rodzajów, bo jako jedyna właściwość dotyczy ich wszystkich —
          i Części każdego rodzaju, i Złożenia. Wcześniej była polem wyłącznie Części
          zakupowej, a przy Złożeniu wpadała do generycznego edytora surowych właściwości,
          więc makro CAD wpisywało masę w miejsce, którego nie było widać. */}
      <PropField
        label={t("part.mass")}
        propKey="mass"
        value={propValue("mass")}
        onSave={saveField}
        type="number"
        disabled={locked}
        onError={setError}
      />

      {rodzaj === "Wykonywana" && (
        <>
          <ProcessesField item={item} target="part" disabled={locked} onSave={saveProcesses} />
          <PriceRow item={item} onChanged={onChanged} onError={setError} />
          <PropField label={t("part.notes")} propKey="notes" value={propValue("notes")} onSave={saveField} disabled={locked} onError={setError} />
        </>
      )}

      {rodzaj === "Zakupowa" && (
        <>
          <ManufacturerField value={propValue("manufacturer")} onSave={saveField} disabled={locked} onError={setError} />
          <ProductTypeAndSubtypeFields
            manufacturerName={propValue("manufacturer")}
            productType={propValue("productType")}
            productSubtype={propValue("productSubtype")}
            onSave={saveField}
            disabled={locked}
            onError={setError}
          />
          <PropField label={t("part.orderNumber")} propKey="orderNumber" value={propValue("orderNumber")} onSave={saveField} disabled={locked} onError={setError} />
          <PropField label={t("part.orderNumber2")} propKey="orderNumber2" value={propValue("orderNumber2")} onSave={saveField} disabled={locked} onError={setError} />
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
        <>
          <ClientAndName2Fields
            clientName={propValue("client")}
            clientName2={propValue("clientName2")}
            onSave={saveField}
            disabled={locked}
            onError={setError}
          />
          <PropField label={t("part.notes")} propKey="notes" value={propValue("notes")} onSave={saveField} disabled={locked} onError={setError} />
        </>
      )}

      {/* Złożenie wykonywane: masa (wyżej) i dodatkowe informacje, jak przy części. Wcześniej
          miało tylko generyczny edytor klucz–wartość, z którym nie było wiadomo, co zrobić. */}
      {rodzaj === "Wykonywane" && (
        <>
          <ProcessesField item={item} target="assembly" disabled={locked} onSave={saveProcesses} />
          <PropField label={t("part.notes")} propKey="notes" value={propValue("notes")} onSave={saveField} disabled={locked} onError={setError} />
        </>
      )}

      {/* Złożenie zakupowe — kupiony podzespół ma producenta i typ produktu dokładnie tak
          samo jak część zakupowa, i tak jak ona dodatkowe informacje. Masa ma własne pole
          wyżej. */}
      {rodzaj === "Zakupowe" && (
        <>
          <ManufacturerField value={propValue("manufacturer")} onSave={saveField} disabled={locked} onError={setError} />
          <ProductTypeAndSubtypeFields
            manufacturerName={propValue("manufacturer")}
            productType={propValue("productType")}
            productSubtype={propValue("productSubtype")}
            onSave={saveField}
            disabled={locked}
            onError={setError}
          />
          <PropField label={t("part.notes")} propKey="notes" value={propValue("notes")} onSave={saveField} disabled={locked} onError={setError} />
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


// Obróbki elementu wykonywanego, wybierane z katalogu (menu "Obróbka") — kilka albo żadna.
// Lista pokazuje tylko obróbki dla danego typu elementu, a obok nich te już zapisane w
// elemencie, których w katalogu nie ma (usunięte albo przemianowane): zostają widoczne i da
// się je odznaczyć, zamiast znikać po cichu.
function ProcessesField({
  item,
  target,
  disabled,
  onSave,
}: {
  item: Item
  target: "part" | "assembly"
  disabled: boolean
  onSave: (next: string[]) => void | Promise<void>
}) {
  const { t } = useLanguage()
  const { processes } = useProcesses()
  const stored = item.properties.processes
  const selected = Array.isArray(stored) ? stored.filter((x): x is string => typeof x === "string") : []
  const catalog = processes
    .filter((p) => (target === "part" ? p.forParts : p.forAssemblies))
    .map((p) => p.name)
  const outside = selected.filter((name) => !catalog.includes(name))

  return (
    <>
      <Label>{t("part.processes")}</Label>
      {catalog.length === 0 && outside.length === 0 ? (
        <Hint>{t("part.processesEmptyCatalog")}</Hint>
      ) : (
        <Select
          multiple
          value={selected}
          disabled={disabled}
          onValueChange={(v) => void onSave(v as string[])}
        >
          <SelectTrigger className="w-full">
            <SelectValue>
              {(v: string[]) => (
                <span className="truncate">{v.length === 0 ? t("part.processesNone") : v.join(", ")}</span>
              )}
            </SelectValue>
          </SelectTrigger>
          <SelectContent>
            {catalog.map((name) => (
              <SelectItem key={name} value={name}>
                {name}
              </SelectItem>
            ))}
            {outside.map((name) => (
              <SelectItem key={name} value={name}>
                {name} <span className="text-muted-foreground">({t("part.processesOutside")})</span>
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      )}
    </>
  )
}

export { ASSEMBLY_KIND_FIELDS, PART_KIND_FIELDS, PartPropertyForm, PartSummaryFields }
