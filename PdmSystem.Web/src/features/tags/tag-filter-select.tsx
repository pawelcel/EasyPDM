import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { useLanguage } from "@/i18n/use-language"

function TagFilterSelect({
  tags,
  value,
  onChange,
}: {
  tags: string[]
  value: string
  onChange: (tag: string) => void
}) {
  const { t } = useLanguage()

  return (
    <Select
      value={value || "all"}
      onValueChange={(v) => onChange(v === "all" ? "" : (v as string))}
    >
      <SelectTrigger className="min-w-36">
        <SelectValue>{(v: string) => (v === "all" || !v ? t("tag.allTags") : v)}</SelectValue>
      </SelectTrigger>
      <SelectContent>
        <SelectItem value="all">{t("tag.allTags")}</SelectItem>
        {tags.map((tag) => (
          <SelectItem key={tag} value={tag}>
            {tag}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

export { TagFilterSelect }
