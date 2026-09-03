import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { useManufacturerProductSubtypes } from "@/features/manufacturers/use-manufacturer-product-types"
import { useLanguage } from "@/i18n/use-language"

// Trzeci, najgłębszy poziom filtra: Producent -> Seria/Typ -> Podtyp. Renderowany dopiero,
// gdy wybrano serię/typ (zob. item-list.tsx), a wyszarzony, gdy ta seria nie ma żadnych
// podtypów — nie każda je ma.
function ProductSubtypeFilterSelect({
  manufacturerName,
  productTypeName,
  value,
  onChange,
}: {
  manufacturerName: string
  productTypeName: string
  value: string
  onChange: (value: string) => void
}) {
  const { t } = useLanguage()
  const { subtypes } = useManufacturerProductSubtypes(manufacturerName, productTypeName)

  return (
    <Select
      value={value || "all"}
      onValueChange={(v) => onChange(v === "all" ? "" : (v as string))}
      disabled={subtypes.length === 0}
    >
      <SelectTrigger className="min-w-40">
        <SelectValue>
          {(v: string) => (v === "all" || !v ? t("filter.allProductSubtypes") : v)}
        </SelectValue>
      </SelectTrigger>
      <SelectContent>
        <SelectItem value="all">{t("filter.allProductSubtypes")}</SelectItem>
        {subtypes.map((s) => (
          <SelectItem key={s.id} value={s.name}>
            {s.name}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

export { ProductSubtypeFilterSelect }
