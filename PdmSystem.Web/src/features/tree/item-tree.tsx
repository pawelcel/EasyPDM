import { useState } from "react"
import { Box, Boxes, ChevronDown, ChevronRight, File, Folder, Plus, X } from "lucide-react"

import { api } from "@/api/client"
import type { Item } from "@/api/types"
import { Button } from "@/components/ui/button"
import { cn } from "@/lib/utils"
import { AddNodeDialog } from "@/features/items/add-node-dialog"
import type { useProjectTree } from "@/features/tree/use-project-tree"

type Tree = ReturnType<typeof useProjectTree>

const TYPE_ICON = {
  folder: Folder,
  part: Box,
  assembly: Boxes,
  file: File,
} as const

function ItemTree({
  tree,
  projectId,
  selectedId,
  onSelect,
}: {
  tree: Tree
  projectId: string
  selectedId: string | null
  onSelect: (id: string, parentId: string | null) => void
}) {
  if (tree.loading) return null

  return (
    <div className="flex flex-col gap-0.5 p-2">
      <div className="flex justify-end px-1.5 pb-1">
        <AddNodeDialog
          trigger={
            <Button size="icon-xs" variant="ghost" aria-label="Dodaj element w projekcie">
              <Plus className="size-3.5" />
            </Button>
          }
          projectId={projectId}
          parentId={null}
          existingItems={tree.items}
          onCreated={tree.refetch}
        />
      </div>

      {tree.roots.length === 0 ? (
        <p className="px-1.5 text-sm text-muted-foreground">Brak elementów w tym projekcie.</p>
      ) : (
        tree.roots.map((item) => (
          <TreeNode
            key={item.id}
            item={item}
            quantity={null}
            parentId={null}
            depth={0}
            projectId={projectId}
            allItems={tree.items}
            childrenOf={tree.childrenOf}
            selectedId={selectedId}
            onSelect={onSelect}
            onRefetch={tree.refetch}
          />
        ))
      )}
    </div>
  )
}

function TreeNode({
  item,
  quantity,
  parentId,
  depth,
  projectId,
  allItems,
  childrenOf,
  selectedId,
  onSelect,
  onRefetch,
}: {
  item: Item
  quantity: number | null
  parentId: string | null
  depth: number
  projectId: string
  allItems: Item[]
  childrenOf: Tree["childrenOf"]
  selectedId: string | null
  onSelect: (id: string, parentId: string | null) => void
  onRefetch: () => void | Promise<void>
}) {
  const [expanded, setExpanded] = useState(true)
  const children = childrenOf(item.id)
  const hasChildren = children.length > 0
  const TypeIcon = TYPE_ICON[item.itemType]

  return (
    <div>
      <div
        className={cn(
          "group flex items-center gap-1 rounded-md px-1.5 py-1 text-sm hover:bg-accent",
          selectedId === item.id && "bg-accent"
        )}
        style={{ paddingLeft: depth * 16 + 6 }}
      >
        <button
          type="button"
          onClick={() => setExpanded((e) => !e)}
          className={cn(
            "flex size-4 shrink-0 items-center justify-center text-muted-foreground",
            !hasChildren && "invisible"
          )}
        >
          {expanded ? <ChevronDown className="size-3.5" /> : <ChevronRight className="size-3.5" />}
        </button>

        <TypeIcon className="size-3.5 shrink-0 text-muted-foreground" />

        <button
          type="button"
          onClick={() => onSelect(item.id, parentId)}
          className="flex-1 truncate text-left"
        >
          {item.fileName}
          {item.itemNumber !== null && (
            <span className="ml-1.5 text-xs text-muted-foreground">#{item.itemNumber}</span>
          )}
          {quantity !== null && quantity !== 1 && (
            <span className="ml-1.5 text-xs text-muted-foreground">×{quantity}</span>
          )}
        </button>

        <div className="hidden items-center gap-0.5 group-hover:flex">
          <AddNodeDialog
            trigger={
              <Button size="icon-xs" variant="ghost" aria-label="Dodaj podelement">
                <Plus className="size-3" />
              </Button>
            }
            projectId={projectId}
            parentId={item.id}
            existingItems={allItems}
            onCreated={onRefetch}
          />
          {parentId && (
            <Button
              size="icon-xs"
              variant="ghost"
              aria-label="Usuń powiązanie"
              onClick={async () => {
                await api.removeChild(parentId, item.id)
                await onRefetch()
              }}
            >
              <X className="size-3" />
            </Button>
          )}
        </div>
      </div>

      {expanded && hasChildren && (
        <div>
          {children.map(({ item: child, quantity: childQty }) => (
            <TreeNode
              key={`${item.id}-${child.id}`}
              item={child}
              quantity={childQty}
              parentId={item.id}
              depth={depth + 1}
              projectId={projectId}
              allItems={allItems}
              childrenOf={childrenOf}
              selectedId={selectedId}
              onSelect={onSelect}
              onRefetch={onRefetch}
            />
          ))}
        </div>
      )}
    </div>
  )
}

export { ItemTree }
