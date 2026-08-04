import { fileTypeLabel, itemDisplayLabel, itemTypeLabelKey, type Item } from "@/api/types"
import { cn } from "@/lib/utils"
import { iconColorClass, itemIcon } from "@/lib/item-visuals"
import { useLanguage } from "@/i18n/use-language"

function ItemRow({
  item,
  projectName,
  selected,
  onSelect,
}: {
  item: Item
  projectName?: string
  selected: boolean
  onSelect: () => void
}) {
  const { t } = useLanguage()
  const Icon = itemIcon(item)
  const typeLabelKey = itemTypeLabelKey(item)
  const typeLabel = typeLabelKey ? t(typeLabelKey) : fileTypeLabel(item)

  return (
    <button
      type="button"
      onClick={onSelect}
      className={cn(
        "flex items-center gap-2 rounded-md px-2 py-1.5 text-left text-sm hover:bg-accent",
        selected && "bg-accent"
      )}
    >
      <Icon className={cn("size-4 shrink-0", iconColorClass(item))} />
      <div className="min-w-0 flex-1">
        <div className="truncate">{itemDisplayLabel(item)}</div>
        <div className="truncate text-[12px] text-muted-foreground">
          {typeLabel}
          {projectName ? ` · ${projectName}` : ""}
        </div>
      </div>
    </button>
  )
}

export { ItemRow }
