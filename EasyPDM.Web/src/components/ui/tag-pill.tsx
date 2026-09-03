import { X } from "lucide-react"

import { Badge } from "@/components/ui/badge"
import { useLanguage } from "@/i18n/use-language"

// onSelect/selected są opcjonalne — bez nich pigułka jest zwykłą etykietą z "x" (tak
// używają jej tagi elementu). Z nimi sama nazwa staje się klikalna i można ją wyróżnić
// jako wybraną (tak używa jej lista serii/typów producenta, gdzie kliknięcie rozwija
// podtypy).
function TagPill({
  name,
  onRemove,
  onSelect,
  selected,
}: {
  name: string
  onRemove: () => void
  onSelect?: () => void
  selected?: boolean
}) {
  const { t } = useLanguage()

  return (
    <Badge
      variant="secondary"
      className={`gap-1.5 pr-1 ${selected ? "ring-1 ring-primary" : ""}`}
    >
      {onSelect ? (
        <button type="button" onClick={onSelect} className="hover:text-primary">
          {name}
        </button>
      ) : (
        name
      )}
      <button
        type="button"
        onClick={onRemove}
        aria-label={t("tag.removeAria", { name })}
        className="rounded-full text-muted-foreground hover:text-destructive"
      >
        <X className="size-3" />
      </button>
    </Badge>
  )
}

export { TagPill }
