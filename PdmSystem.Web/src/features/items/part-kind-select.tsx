import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { useLanguage } from "@/i18n/use-language"

export type PartKindFilter = "all" | "Zakupowa" | "Wykonywana" | "Normalia" | "Klienta"

function PartKindSelect({
  value,
  onChange,
  disabled,
}: {
  value: PartKindFilter
  onChange: (value: PartKindFilter) => void
  disabled?: boolean
}) {
  const { t } = useLanguage()
  const labelFor = (v: PartKindFilter) => {
    if (v === "Zakupowa") return t("part.kindPurchased")
    if (v === "Wykonywana") return t("part.kindManufactured")
    if (v === "Normalia") return t("part.kindStandard")
    if (v === "Klienta") return t("part.kindClient")
    return t("filter.allKinds")
  }

  return (
    <Select value={value} onValueChange={(v) => onChange(v as PartKindFilter)} disabled={disabled}>
      <SelectTrigger className="min-w-36">
        <SelectValue>{(v: string) => labelFor(v as PartKindFilter)}</SelectValue>
      </SelectTrigger>
      <SelectContent>
        <SelectItem value="all">{t("filter.allKinds")}</SelectItem>
        <SelectItem value="Wykonywana">{t("part.kindManufactured")}</SelectItem>
        <SelectItem value="Zakupowa">{t("part.kindPurchased")}</SelectItem>
        <SelectItem value="Normalia">{t("part.kindStandard")}</SelectItem>
        <SelectItem value="Klienta">{t("part.kindClient")}</SelectItem>
      </SelectContent>
    </Select>
  )
}

export { PartKindSelect }
