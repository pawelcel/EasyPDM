import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { useManufacturerProductTypes } from "@/features/manufacturers/use-manufacturer-product-types"
import { useLanguage } from "@/i18n/use-language"

// Zawężenie do konkretnego typu produktu WYBRANEGO producenta — bliźniak
// ManufacturerFilterSelect, tylko z listą zależną od tego, co wybrano piętro wyżej.
// Renderowane dopiero po wybraniu producenta (zob. item-list.tsx), więc pusta nazwa tutaj
// nie jest normalnym stanem — mimo to lista wtedy po prostu wyjdzie pusta, bez błędu.
function ProductTypeFilterSelect({
  manufacturerName,
  value,
  onChange,
}: {
  manufacturerName: string
  value: string
  onChange: (value: string) => void
}) {
  const { t } = useLanguage()
  const { productTypes } = useManufacturerProductTypes(manufacturerName)

  return (
    <Select
      value={value || "all"}
      onValueChange={(v) => onChange(v === "all" ? "" : (v as string))}
      disabled={productTypes.length === 0}
    >
      <SelectTrigger className="min-w-40">
        <SelectValue>{(v: string) => (v === "all" || !v ? t("filter.allProductTypes") : v)}</SelectValue>
      </SelectTrigger>
      <SelectContent>
        <SelectItem value="all">{t("filter.allProductTypes")}</SelectItem>
        {productTypes.map((name) => (
          <SelectItem key={name} value={name}>
            {name}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

export { ProductTypeFilterSelect }
