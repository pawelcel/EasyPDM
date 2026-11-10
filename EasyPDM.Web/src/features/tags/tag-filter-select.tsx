import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { useLanguage } from "@/i18n/use-language"

// Kilka tagów naraz: lista zostaje otwarta po kliknięciu, a pokazane są elementy mające
// WSZYSTKIE zaznaczone (zob. GET /api/items). "Wszystkie tagi" czyści wybór.
function TagFilterSelect({
  tags,
  value,
  onChange,
}: {
  tags: string[]
  value: string[]
  onChange: (tags: string[]) => void
}) {
  const { t } = useLanguage()

  return (
    <Select
      multiple
      value={value}
      onValueChange={(v) => {
        const next = v as string[]
        onChange(next.includes("") ? [] : next)
      }}
    >
      <SelectTrigger className="max-w-72 min-w-36">
        <SelectValue>
          {(v: string[]) => (
            <span className="truncate">{v.length === 0 ? t("tag.allTags") : v.join(", ")}</span>
          )}
        </SelectValue>
      </SelectTrigger>
      <SelectContent>
        <SelectItem value="">{t("tag.allTags")}</SelectItem>
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
