import { itemTypeLabel, type Item } from "@/api/types"
import { Card, CardContent, CardHeader } from "@/components/ui/card"
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/components/ui/collapsible"
import { ItemDetailPanel } from "@/features/items/item-detail-panel"

function ItemCard({
  item,
  projectName,
  open,
  onToggle,
  onItemsRefetch,
  onTagsRefetch,
}: {
  item: Item
  projectName?: string
  open: boolean
  onToggle: () => void
  onItemsRefetch: () => void | Promise<void>
  onTagsRefetch: () => void | Promise<void>
}) {
  const modified = item.modifiedAt ? new Date(item.modifiedAt).toLocaleString("pl-PL") : "—"
  const typeLabel = itemTypeLabel(item)

  return (
    <Card>
      <Collapsible open={open} onOpenChange={onToggle}>
        <CollapsibleTrigger className="w-full cursor-pointer text-left">
          <CardHeader className="flex-row items-baseline justify-between">
            <div>
              <div className="text-[15px] font-semibold">
                {item.fileName}
                {item.itemNumber !== null && (
                  <span className="ml-1.5 text-[12.5px] font-normal text-muted-foreground">
                    #{item.itemNumber}
                  </span>
                )}
              </div>
              <div className="text-[12.5px] text-muted-foreground">
                {typeLabel} · zmodyfikowano {modified}
                {projectName ? ` · ${projectName}` : ""}
              </div>
            </div>
          </CardHeader>
        </CollapsibleTrigger>

        <CollapsibleContent>
          <CardContent className="border-t pt-3.5">
            <ItemDetailPanel
              item={item}
              showHeader={false}
              onItemsRefetch={onItemsRefetch}
              onTagsRefetch={onTagsRefetch}
            />
          </CardContent>
        </CollapsibleContent>
      </Collapsible>
    </Card>
  )
}

export { ItemCard }
