import { X } from "lucide-react"

import { Badge } from "@/components/ui/badge"
import { useLanguage } from "@/i18n/use-language"

function TagPill({ name, onRemove }: { name: string; onRemove: () => void }) {
  const { t } = useLanguage()

  return (
    <Badge variant="secondary" className="gap-1.5 pr-1">
      {name}
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
