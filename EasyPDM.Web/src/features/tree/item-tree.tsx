import { useState } from "react"
import {
  DndContext,
  DragOverlay,
  PointerSensor,
  closestCenter,
  useSensor,
  useSensors,
  type DragEndEvent,
} from "@dnd-kit/core"
import { SortableContext, arrayMove, useSortable, verticalListSortingStrategy } from "@dnd-kit/sortable"
import { CSS } from "@dnd-kit/utilities"
import {
  ChevronDown,
  ChevronRight,
  FolderKanban,
  GripVertical,
  Plus,
  X,
} from "lucide-react"

import { api } from "@/api/client"
import { itemDisplayLabel, type Item } from "@/api/types"
import { Button } from "@/components/ui/button"
import { cn } from "@/lib/utils"
import { iconColorClass, itemIcon, ownerLockVisual } from "@/lib/item-visuals"
import { AddNodeDialog } from "@/features/items/add-node-dialog"
import { useAuth } from "@/features/auth/use-auth"
import type { useProjectTree } from "@/features/tree/use-project-tree"
import { useLanguage } from "@/i18n/use-language"

type Tree = ReturnType<typeof useProjectTree>

// Podgląd przeciąganego wiersza pokazywany przez DragOverlay — to zwykły "chip" poza
// drzewem (portal), nie fragment drzewka, więc nie próbuje odtwarzać wcięć/ikon rozwijania.
function TreeDragPreview({ item }: { item: Item | undefined }) {
  if (!item) return null
  const TypeIcon = itemIcon(item)
  return (
    <div className="flex items-center gap-1.5 rounded-md border bg-popover px-2.5 py-1.5 text-sm shadow-md">
      <TypeIcon className={cn("size-3.5 shrink-0", iconColorClass(item))} />
      <span className="truncate">{itemDisplayLabel(item)}</span>
    </div>
  )
}

function ItemTree({
  tree,
  projectId,
  projectName,
  isProjectSelected,
  selectedId,
  onSelect,
  onSelectProject,
  selectionMode,
  selectedIds,
  onToggleSelect,
}: {
  tree: Tree
  projectId: string
  projectName: string
  isProjectSelected: boolean
  selectedId: string | null
  onSelect: (id: string, parentId: string | null) => void
  onSelectProject: () => void
  selectionMode: boolean
  selectedIds: Set<string>
  onToggleSelect: (id: string) => void
}) {
  const { t } = useLanguage()
  const rootSensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 4 } }))
  const [activeRootId, setActiveRootId] = useState<string | null>(null)

  if (tree.loading) return null

  async function handleRootDragEnd(event: DragEndEvent) {
    setActiveRootId(null)
    const { active, over } = event
    if (!over || active.id === over.id) return

    const ids = tree.roots.map((r) => r.id)
    const fromIndex = ids.indexOf(String(active.id))
    const toIndex = ids.indexOf(String(over.id))
    if (fromIndex === -1 || toIndex === -1) return

    await api.reorderRoots(projectId, arrayMove(ids, fromIndex, toIndex))
    await tree.refetch()
  }

  return (
    <div className="flex flex-col gap-0.5 p-2">
      <div
        className={cn(
          "group flex items-center gap-1 rounded-md px-1.5 py-1 text-sm hover:bg-accent",
          isProjectSelected && "bg-accent"
        )}
      >
        <span className="size-4 shrink-0" />
        <FolderKanban className="size-3.5 shrink-0 text-muted-foreground" />
        <button
          type="button"
          onClick={onSelectProject}
          className="flex-1 truncate text-left font-medium"
        >
          {projectName}
        </button>
        <div className="hidden items-center gap-0.5 group-hover:flex">
          <AddNodeDialog
            trigger={
              <Button size="icon-xs" variant="ghost" aria-label={t("addNode.addToProjectAria")}>
                <Plus className="size-3" />
              </Button>
            }
            projectId={projectId}
            parentId={null}
            parentType={null}
            onCreated={tree.refetch}
          />
        </div>
      </div>

      <div className="flex flex-col gap-0.5 pl-3.5">
        {tree.roots.length === 0 ? (
          <p className="px-1.5 py-1 text-sm text-muted-foreground">{t("addNode.noItemsInProject")}</p>
        ) : (
          <DndContext
            sensors={rootSensors}
            collisionDetection={closestCenter}
            onDragStart={(e) => setActiveRootId(String(e.active.id))}
            onDragEnd={handleRootDragEnd}
            onDragCancel={() => setActiveRootId(null)}
          >
            <SortableContext items={tree.roots.map((r) => r.id)} strategy={verticalListSortingStrategy}>
              {tree.roots.map((item) => (
                <TreeNode
                  key={item.id}
                  item={item}
                  quantity={null}
                  parentId={null}
                  depth={0}
                  projectId={projectId}
                  childrenOf={tree.childrenOf}
                  selectedId={selectedId}
                  onSelect={onSelect}
                  onRefetch={tree.refetch}
                  selectionMode={selectionMode}
                  selectedIds={selectedIds}
                  onToggleSelect={onToggleSelect}
                />
              ))}
            </SortableContext>
            <DragOverlay>
              {activeRootId ? <TreeDragPreview item={tree.itemsById.get(activeRootId)} /> : null}
            </DragOverlay>
          </DndContext>
        )}
      </div>
    </div>
  )
}

function TreeNode({
  item,
  quantity,
  parentId,
  depth,
  projectId,
  childrenOf,
  selectedId,
  onSelect,
  onRefetch,
  selectionMode,
  selectedIds,
  onToggleSelect,
}: {
  item: Item
  quantity: number | null
  parentId: string | null
  depth: number
  projectId: string
  childrenOf: Tree["childrenOf"]
  selectedId: string | null
  onSelect: (id: string, parentId: string | null) => void
  onRefetch: () => void | Promise<void>
  selectionMode: boolean
  selectedIds: Set<string>
  onToggleSelect: (id: string) => void
}) {
  const { t } = useLanguage()
  const { user } = useAuth()
  const [expanded, setExpanded] = useState(true)
  const children = childrenOf(item.id)
  const hasChildren = children.length > 0
  const TypeIcon = itemIcon(item)
  const lockVisual = ownerLockVisual(item, user?.id)
  const checked = selectedIds.has(item.id)

  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id: item.id,
  })

  const childSensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 4 } }))
  const [activeChildId, setActiveChildId] = useState<string | null>(null)

  async function handleChildDragEnd(event: DragEndEvent) {
    setActiveChildId(null)
    const { active, over } = event
    if (!over || active.id === over.id) return

    const ids = children.map((c) => c.item.id)
    const fromIndex = ids.indexOf(String(active.id))
    const toIndex = ids.indexOf(String(over.id))
    if (fromIndex === -1 || toIndex === -1) return

    await api.reorderChildren(item.id, arrayMove(ids, fromIndex, toIndex))
    await onRefetch()
  }

  return (
    <div
      ref={setNodeRef}
      style={{ transform: CSS.Transform.toString(transform), transition }}
      className={isDragging ? "opacity-40" : undefined}
    >
      <div
        className={cn(
          "group flex items-center gap-1 rounded-md px-1.5 py-1 text-sm select-none hover:bg-accent",
          selectedId === item.id && "bg-accent"
        )}
        style={{ paddingLeft: depth * 16 + 6 }}
      >
        {selectionMode && (
          <input
            type="checkbox"
            checked={checked}
            onChange={() => onToggleSelect(item.id)}
            onClick={(e) => e.stopPropagation()}
            aria-label={t("bulk.selectItemAria", { name: itemDisplayLabel(item) })}
            className="size-3.5 shrink-0 accent-primary"
          />
        )}

        <span
          {...attributes}
          {...listeners}
          className="flex size-4 shrink-0 cursor-grab touch-none items-center justify-center text-muted-foreground/40 hover:text-muted-foreground active:cursor-grabbing"
          aria-label={t("item.dragToReorderAria")}
        >
          <GripVertical className="size-3.5" />
        </span>

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

        <TypeIcon className={cn("size-3.5 shrink-0", iconColorClass(item))} />
        {lockVisual && (
          <lockVisual.Icon
            className={cn("size-3 shrink-0", lockVisual.colorClass)}
            aria-label={t("item.ownerLockIconAria")}
          />
        )}

        <button
          type="button"
          onClick={() => onSelect(item.id, parentId)}
          className="flex-1 truncate text-left"
        >
          {itemDisplayLabel(item)}
          {quantity !== null && quantity !== 1 && (
            <span className="ml-1.5 text-xs text-muted-foreground">×{quantity}</span>
          )}
        </button>

        <div className="hidden items-center gap-0.5 group-hover:flex">
          {(item.itemType === "folder" || item.itemType === "assembly") && (
            <AddNodeDialog
              trigger={
                <Button size="icon-xs" variant="ghost" aria-label={t("addNode.addSubitemAria")}>
                  <Plus className="size-3" />
                </Button>
              }
              projectId={projectId}
              parentId={item.id}
              parentType={item.itemType}
              onCreated={onRefetch}
            />
          )}
          {parentId && (
            <Button
              size="icon-xs"
              variant="ghost"
              aria-label={t("addNode.removeRelationAria")}
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
        <DndContext
          sensors={childSensors}
          collisionDetection={closestCenter}
          onDragStart={(e) => setActiveChildId(String(e.active.id))}
          onDragEnd={handleChildDragEnd}
          onDragCancel={() => setActiveChildId(null)}
        >
          <SortableContext items={children.map((c) => c.item.id)} strategy={verticalListSortingStrategy}>
            <div>
              {children.map(({ item: child, quantity: childQty }) => (
                <TreeNode
                  key={`${item.id}-${child.id}`}
                  item={child}
                  quantity={childQty}
                  parentId={item.id}
                  depth={depth + 1}
                  projectId={projectId}
                  childrenOf={childrenOf}
                  selectedId={selectedId}
                  onSelect={onSelect}
                  onRefetch={onRefetch}
                  selectionMode={selectionMode}
                  selectedIds={selectedIds}
                  onToggleSelect={onToggleSelect}
                />
              ))}
            </div>
          </SortableContext>
          <DragOverlay>
            {activeChildId ? (
              <TreeDragPreview item={children.find((c) => c.item.id === activeChildId)?.item} />
            ) : null}
          </DragOverlay>
        </DndContext>
      )}
    </div>
  )
}

export { ItemTree }
