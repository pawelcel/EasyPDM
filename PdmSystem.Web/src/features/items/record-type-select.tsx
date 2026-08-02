import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import type { TranslationKey } from "@/i18n/translations"
import { useLanguage } from "@/i18n/use-language"

export type RecordType = "all" | "project" | "part" | "assembly" | "other"

const OPTIONS: { value: RecordType; labelKey: TranslationKey }[] = [
  { value: "all", labelKey: "filter.allTypes" },
  { value: "project", labelKey: "itemType.project" },
  { value: "part", labelKey: "itemType.part" },
  { value: "assembly", labelKey: "itemType.assembly" },
  { value: "other", labelKey: "filter.other" },
]

function RecordTypeSelect({
  value,
  onChange,
}: {
  value: RecordType
  onChange: (value: RecordType) => void
}) {
  const { t } = useLanguage()
  const labelFor = (v: RecordType) => t(OPTIONS.find((o) => o.value === v)!.labelKey)

  return (
    <Select value={value} onValueChange={(v) => onChange(v as RecordType)}>
      <SelectTrigger className="min-w-36">
        <SelectValue>{(v: string) => labelFor(v as RecordType)}</SelectValue>
      </SelectTrigger>
      <SelectContent>
        {OPTIONS.map((o) => (
          <SelectItem key={o.value} value={o.value}>
            {t(o.labelKey)}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

export { RecordTypeSelect }
