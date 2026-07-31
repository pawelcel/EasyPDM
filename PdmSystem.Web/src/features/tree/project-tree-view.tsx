import { useEffect, useState } from "react"

import { api } from "@/api/client"
import { Hint } from "@/components/ui/hint"
import { ItemDetailPanel } from "@/features/items/item-detail-panel"
import { ItemTree } from "@/features/tree/item-tree"
import { useProjectTree } from "@/features/tree/use-project-tree"

function ProjectTreeView({
  projectId,
  projectName,
  onTagsRefetch,
}: {
  projectId: string
  projectName: string
  onTagsRefetch: () => void | Promise<void>
}) {
  const tree = useProjectTree(projectId)
  const [selected, setSelected] = useState<{ id: string; parentId: string | null } | null>(null)

  // Jeśli wybrany element zniknął z drzewka (np. go usunięto), czyścimy zaznaczenie.
  useEffect(() => {
    if (selected && !tree.itemsById.has(selected.id)) setSelected(null)
  }, [selected, tree.itemsById])

  const selectedItem = selected ? tree.itemsById.get(selected.id) : undefined

  async function handleRemoveFromStructure() {
    if (!selected) return
    if (selected.parentId) {
      // Element z rodzicem — odpinamy konkretną krawędź, sam rekord zostaje.
      await api.removeChild(selected.parentId, selected.id)
    } else {
      // Element bez rodzica — nie ma czego odpiąć, więc przestaje być widoczny
      // jako korzeń w drzewku (rekord i przynależność do projektu zostają).
      await api.setShowInTree(selected.id, false)
    }
    setSelected(null)
    await tree.refetch()
  }

  async function handleDeleteCompletely() {
    if (!selectedItem) return
    const confirmed = window.confirm(
      `Na pewno usunąć „${selectedItem.fileName}” wraz ze wszystkimi podelementami? Tej operacji nie można cofnąć.`
    )
    if (!confirmed) return
    await api.deleteItem(selectedItem.id)
    setSelected(null)
    await tree.refetch()
  }

  return (
    <div className="grid grid-cols-3 gap-4">
      <div className="col-span-1 rounded-xl bg-card ring-1 ring-foreground/10">
        <ItemTree
          tree={tree}
          projectId={projectId}
          selectedId={selected?.id ?? null}
          onSelect={(id, parentId) => setSelected({ id, parentId })}
        />
      </div>

      <div className="col-span-2 rounded-xl bg-card p-4 ring-1 ring-foreground/10">
        {selectedItem ? (
          <ItemDetailPanel
            key={selectedItem.id}
            item={selectedItem}
            projectName={projectName}
            childItems={tree.childrenOf(selectedItem.id).map((c) => c.item)}
            onItemsRefetch={tree.refetch}
            onTagsRefetch={onTagsRefetch}
            onRemoveFromStructure={handleRemoveFromStructure}
            onDeleteCompletely={handleDeleteCompletely}
          />
        ) : (
          <Hint>Wybierz element w strukturze po lewej, żeby zobaczyć jego właściwości.</Hint>
        )}
      </div>
    </div>
  )
}

export { ProjectTreeView }
