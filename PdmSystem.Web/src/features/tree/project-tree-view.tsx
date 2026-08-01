
import { useEffect, useState } from "react"
import { api } from "@/api/client"
import type { Project } from "@/api/types"
import { ConfirmDialog } from "@/components/ui/confirm-dialog"
import { ItemDetailPanel } from "@/features/items/item-detail-panel"
import { ProjectDetailPanel } from "@/features/projects/project-detail-panel"
import { ItemTree } from "@/features/tree/item-tree"
import { useProjectTree } from "@/features/tree/use-project-tree"

type Selection = { kind: "project" } | { kind: "item"; id: string; parentId: string | null }

function ProjectTreeView({
  project,
  isAdmin,
  onTagsRefetch,
  onProjectUpdated,
  onProjectDeleted,
}: {
  project: Project
  isAdmin: boolean
  onTagsRefetch: () => void | Promise<void>
  onProjectUpdated: () => void | Promise<void>
  onProjectDeleted: () => void | Promise<void>
}) {
  const tree = useProjectTree(project.id)
  // Domyślnie zaznaczony jest sam projekt — to teraz pierwsza (i zawsze widoczna) pozycja
  // w strukturze, więc naturalnie jest tym, co widać po wejściu w projekt.
  const [selection, setSelection] = useState<Selection>({ kind: "project" })
  const [confirmingDelete, setConfirmingDelete] = useState(false)

  // Jeśli wybrany element zniknął z drzewka (np. go usunięto), wracamy do projektu.
  useEffect(() => {
    if (selection.kind === "item" && !tree.itemsById.has(selection.id)) {
      setSelection({ kind: "project" })
    }
  }, [selection, tree.itemsById])

  const selectedItem = selection.kind === "item" ? tree.itemsById.get(selection.id) : undefined

  async function handleRemoveFromStructure() {
    if (selection.kind !== "item") return
    if (selection.parentId) {
      // Element z rodzicem — odpinamy konkretną krawędź, sam rekord zostaje.
      await api.removeChild(selection.parentId, selection.id)
    } else {
      // Element bez rodzica — nie ma czego odpiąć, więc przestaje być widoczny
      // jako korzeń w drzewku (rekord i przynależność do projektu zostają).
      await api.setShowInTree(selection.id, false)
    }
    setSelection({ kind: "project" })
    await tree.refetch()
  }

  async function confirmDeleteCompletely() {
    if (!selectedItem) return
    await api.deleteItem(selectedItem.id)
    setConfirmingDelete(false)
    setSelection({ kind: "project" })
    await tree.refetch()
  }

  return (
    <div className="grid grid-cols-3 gap-4">
      <div className="col-span-1 rounded-xl bg-card ring-1 ring-foreground/10">
        <ItemTree
          tree={tree}
          projectId={project.id}
          projectName={project.name}
          isProjectSelected={selection.kind === "project"}
          selectedId={selection.kind === "item" ? selection.id : null}
          onSelect={(id, parentId) => setSelection({ kind: "item", id, parentId })}
          onSelectProject={() => setSelection({ kind: "project" })}
        />
      </div>

      <div className="col-span-2 rounded-xl bg-card p-4 ring-1 ring-foreground/10">
        {selectedItem ? (
          <ItemDetailPanel
            key={selectedItem.id}
            item={selectedItem}
            projectName={project.name}
            childEntries={tree.childrenOf(selectedItem.id)}
            onSelectChild={(childId) => setSelection({ kind: "item", id: childId, parentId: selectedItem.id })}
            onItemsRefetch={tree.refetch}
            onTagsRefetch={onTagsRefetch}
            onRemoveFromStructure={handleRemoveFromStructure}
            onDeleteCompletely={isAdmin ? () => setConfirmingDelete(true) : undefined}
          />
        ) : (
          <ProjectDetailPanel
            project={project}
            isAdmin={isAdmin}
            onUpdated={onProjectUpdated}
            onDeleted={onProjectDeleted}
          />
        )}
      </div>

      {confirmingDelete && selectedItem && (
        <ConfirmDialog
          open
          title="Usuń całkowicie"
          description={`Na pewno usunąć „${selectedItem.fileName}” wraz ze wszystkimi podelementami? Tej operacji nie można cofnąć.`}
          confirmLabel="Usuń całkowicie"
          variant="destructive"
          onConfirm={confirmDeleteCompletely}
          onCancel={() => setConfirmingDelete(false)}
        />
      )}
    </div>
  )
}

export { ProjectTreeView }
