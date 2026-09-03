import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import type { KindFilter } from "@/features/items/database-filters"
import { useLanguage } from "@/i18n/use-language"

// Jedna lista rodzajów dla Części i Złożeń naraz — stąd etykiety z obiema końcówkami
// ("Zakupowa/-e"). Samo dopasowanie pojęcia do napisu w properties.rodzaj siedzi w
// matchesKindFilter (database-filters.ts), razem z resztą semantyki filtrów.
function PartKindSelect({
  value,
  onChange,
  disabled,
}: {
  value: KindFilter
  onChange: (value: KindFilter) => void
  disabled?: boolean
}) {
  const { t } = useLanguage()
  const labelFor = (v: KindFilter) => {
    if (v === "purchased") return t("filter.kindPurchased")
    if (v === "manufactured") return t("filter.kindManufactured")
    if (v === "standard") return t("part.kindStandard")
    if (v === "client") return t("part.kindClient")
    return t("filter.allKinds")
  }

  return (
    <Select value={value} onValueChange={(v) => onChange(v as KindFilter)} disabled={disabled}>
      <SelectTrigger className="min-w-36">
        <SelectValue>{(v: string) => labelFor(v as KindFilter)}</SelectValue>
      </SelectTrigger>
      <SelectContent>
        <SelectItem value="all">{t("filter.allKinds")}</SelectItem>
        <SelectItem value="manufactured">{t("filter.kindManufactured")}</SelectItem>
        <SelectItem value="purchased">{t("filter.kindPurchased")}</SelectItem>
        <SelectItem value="standard">{t("part.kindStandard")}</SelectItem>
        <SelectItem value="client">{t("part.kindClient")}</SelectItem>
      </SelectContent>
    </Select>
  )
}

export { PartKindSelect }
