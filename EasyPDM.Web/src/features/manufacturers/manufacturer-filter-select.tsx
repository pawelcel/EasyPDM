import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { useManufacturers } from "@/features/manufacturers/use-manufacturers"
import { useLanguage } from "@/i18n/use-language"

function ManufacturerFilterSelect({
  value,
  onChange,
  disabled,
}: {
  value: string
  onChange: (value: string) => void
  disabled?: boolean
}) {
  const { t } = useLanguage()
  const { manufacturers } = useManufacturers("")

  return (
    <Select
      value={value || "all"}
      onValueChange={(v) => onChange(v === "all" ? "" : (v as string))}
      disabled={disabled}
    >
      <SelectTrigger className="min-w-40">
        <SelectValue>{(v: string) => (v === "all" || !v ? t("filter.allManufacturers") : v)}</SelectValue>
      </SelectTrigger>
      <SelectContent>
        <SelectItem value="all">{t("filter.allManufacturers")}</SelectItem>
        {manufacturers.map((m) => (
          <SelectItem key={m.id} value={m.name}>
            {m.name}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

export { ManufacturerFilterSelect }
