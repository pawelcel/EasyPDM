
import { useEffect, useState } from "react"
import { api } from "@/api/client"
import { STATUS_LABEL_KEYS, type ItemStatus, type Project } from "@/api/types"
import { Button } from "@/components/ui/button"
import { ConfirmDialog } from "@/components/ui/confirm-dialog"
import { FormError } from "@/components/ui/form-error"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { AddTagRow } from "@/features/tags/add-tag-row"
import { ItemDetailPanel } from "@/features/items/item-detail-panel"
import { ProjectDetailPanel } from "@/features/projects/project-detail-panel"
import { ItemTree } from "@/features/tree/item-tree"
import { useProjectTree } from "@/features/tree/use-project-tree"
import { useLanguage } from "@/i18n/use-language"

// parentId: string (zwykłe dziecko) | null (prawdziwy korzeń projektu, bez rodzica) |
// undefined (przeszliśmy tu przyciskiem "Przejdź" na zagłębionym wpisie BOM-u — prawdziwy
// rodzic istnieje, ale to jakieś POD-złożenie, którego tu nie znamy). Rozróżnienie
// null/undefined jest celowe — patrz handleRemoveFromStructure i miejsce, gdzie
// onRemoveFromStructure jest (nie) przekazywane do ItemDetailPanel.
type Selection = { kind: "project" } | { kind: "item"; id: string; parentId: string | null | undefined }

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
  const { t } = useLanguage()
  const tree = useProjectTree(project.id)
  // Domyślnie zaznaczony jest sam projekt — to teraz pierwsza (i zawsze widoczna) pozycja
  // w strukturze, więc naturalnie jest tym, co widać po wejściu w projekt.
  const [selection, setSelection] = useState<Selection>({ kind: "project" })
  const [confirmingDelete, setConfirmingDelete] = useState(false)

  // Zaznaczenie "checkboxami" (do akcji masowych) jest niezależne od "selection" powyżej —
  // to drugie decyduje, co pokazuje panel po prawej, pierwsze służy wyłącznie do wsadowego
  // dodania tagu / zmiany statusu / usunięcia wielu elementów naraz. Checkboxy istnieją w
  // drzewie tylko, gdy "selectionMode" jest włączony — bez świadomego wejścia w ten tryb nie
  // da się nic zaznaczyć (unika przypadkowego zaznaczania przy zwykłym przeglądaniu drzewa).
  const [selectionMode, setSelectionMode] = useState(false)
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set())
  const [confirmingBulkDelete, setConfirmingBulkDelete] = useState(false)
  const [bulkError, setBulkError] = useState<string | null>(null)

  function toggleSelectionMode() {
    setSelectionMode((prev) => {
      const next = !prev
      if (!next) {
        setSelectedIds(new Set())
        setBulkError(null)
      }
      return next
    })
  }

  // Jeśli wybrany element zniknął z drzewka (np. go usunięto), wracamy do projektu.
  useEffect(() => {
    if (selection.kind === "item" && !tree.itemsById.has(selection.id)) {
      setSelection({ kind: "project" })
    }
  }, [selection, tree.itemsById])

  // Analogicznie dla zaznaczenia masowego — elementy usunięte gdzie indziej znikają z Setu.
  useEffect(() => {
    setSelectedIds((prev) => {
      const next = new Set([...prev].filter((id) => tree.itemsById.has(id)))
      return next.size === prev.size ? prev : next
    })
  }, [tree.itemsById])

  const selectedItem = selection.kind === "item" ? tree.itemsById.get(selection.id) : undefined
  const selectedItemParentId = selection.kind === "item" ? selection.parentId : null
  const selectedForBulk = [...selectedIds].map((id) => tree.itemsById.get(id)).filter((i) => i != null)
  // Elementy "Wydane" są celowo wykluczone z masowej zmiany statusu — pojedyncza zmiana z
  // "Wydany" podnosi rewizję i normalnie wymaga potwierdzenia (z opcjonalnym komentarzem do
  // rewizji); w trybie masowym nie ma jak tego potwierdzić, więc taka zmiana jest tu zablokowana.
  const eligibleForStatus = selectedForBulk.some(
    (i) => (i.itemType === "part" || i.itemType === "assembly") && i.status !== "wydany"
  )

  function toggleSelect(id: string) {
    setSelectedIds((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

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

  async function handleBulkAddTag(name: string) {
    for (const id of selectedIds) {
      await api.addTag(id, name)
    }
    await tree.refetch()
    await onTagsRefetch()
  }

  async function handleBulkStatusChange(status: ItemStatus) {
    setBulkError(null)
    let failures = 0
    let skippedReleased = 0
    for (const id of selectedIds) {
      const item = tree.itemsById.get(id)
      if (!item || (item.itemType !== "part" && item.itemType !== "assembly")) continue
      // Zmiana statusu z "Wydany" podnosi rewizję i normalnie wymaga potwierdzenia — w trybie
      // masowym nie ma jak go zebrać, więc takie elementy są pomijane, nie tylko wysyłane
      // "na ślepo" do backendu.
      if (item.status === "wydany") {
        skippedReleased++
        continue
      }
      try {
        await api.setStatus(id, status)
      } catch {
        failures++
      }
    }
    const messages = [
      skippedReleased > 0 ? t("bulk.statusChangeSkippedReleased", { count: skippedReleased }) : null,
      failures > 0 ? t("bulk.statusChangeFailed", { count: failures }) : null,
    ].filter((m) => m !== null)
    setBulkError(messages.length > 0 ? messages.join(" ") : null)
    await tree.refetch()
  }

  async function confirmBulkDelete() {
    for (const id of selectedIds) {
      await api.deleteItem(id)
    }
    setConfirmingBulkDelete(false)
    setSelectedIds(new Set())
    await tree.refetch()
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-2 rounded-xl bg-card p-2 ring-1 ring-foreground/10">
        <Button
          size="sm"
          variant={selectionMode ? "default" : "outline"}
          onClick={toggleSelectionMode}
        >
          {selectionMode ? t("bulk.disableSelection") : t("bulk.enableSelection")}
        </Button>

        {selectionMode && selectedIds.size > 0 && (
          <>
            <span className="px-1 text-[12.5px] text-muted-foreground">
              {t("bulk.selectedCount", { count: selectedIds.size })}
            </span>
            <AddTagRow onAdd={handleBulkAddTag} />
            <Select
              value=""
              onValueChange={(v) => v && handleBulkStatusChange(v as ItemStatus)}
              disabled={!eligibleForStatus}
            >
              <SelectTrigger className="w-40">
                <SelectValue>{() => t("item.statusChangeConfirm")}</SelectValue>
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="w_pracy">{t(STATUS_LABEL_KEYS.w_pracy)}</SelectItem>
                <SelectItem value="sprawdzany">{t(STATUS_LABEL_KEYS.sprawdzany)}</SelectItem>
                <SelectItem value="wydany">{t(STATUS_LABEL_KEYS.wydany)}</SelectItem>
              </SelectContent>
            </Select>
            {isAdmin && (
              <Button size="sm" variant="destructive" onClick={() => setConfirmingBulkDelete(true)}>
                {t("bulk.deleteButton")}
              </Button>
            )}
            <Button size="sm" variant="ghost" onClick={() => setSelectedIds(new Set())}>
              {t("bulk.clearSelection")}
            </Button>
          </>
        )}
      </div>
      <FormError>{bulkError}</FormError>

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
            selectionMode={selectionMode}
            selectedIds={selectedIds}
            onToggleSelect={toggleSelect}
          />
        </div>

        <div className="col-span-2 rounded-xl bg-card p-4 ring-1 ring-foreground/10">
          {selectedItem ? (
            <ItemDetailPanel
              key={selectedItem.id}
              item={selectedItem}
              projectName={project.name}
              childEntries={tree.childrenOf(selectedItem.id)}
              onSelectChild={(childId, parentId) => setSelection({ kind: "item", id: childId, parentId })}
              onItemsRefetch={tree.refetch}
              onTagsRefetch={onTagsRefetch}
              // parentId undefined = przejście tu przyciskiem "Przejdź" na zagłębionym wpisie
              // BOM-u, prawdziwy rodzic nieznany — nie oferujemy "Usuń ze struktury" w ogóle,
              // zamiast zgadywać (błędnie odpiąć od niewłaściwego rodzica albo błędnie
              // schować jako "korzeń", którym ten element wcale nie jest).
              onRemoveFromStructure={selectedItemParentId !== undefined ? handleRemoveFromStructure : undefined}
              onDeleteCompletely={isAdmin ? () => setConfirmingDelete(true) : undefined}
              onDuplicated={(newId) =>
                setSelection({ kind: "item", id: newId, parentId: selectedItemParentId })
              }
              duplicateParentId={selectedItemParentId}
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
      </div>

      {confirmingDelete && selectedItem && (
        <ConfirmDialog
          open
          title={t("item.deleteCompletely")}
          description={t("item.deleteConfirmDescription", { name: selectedItem.fileName })}
          confirmLabel={t("item.deleteCompletely")}
          variant="destructive"
          onConfirm={confirmDeleteCompletely}
          onCancel={() => setConfirmingDelete(false)}
        />
      )}

      {confirmingBulkDelete && (
        <ConfirmDialog
          open
          title={t("bulk.deleteButton")}
          description={t("bulk.deleteConfirmDescription", { count: selectedIds.size })}
          confirmLabel={t("bulk.deleteButton")}
          variant="destructive"
          onConfirm={confirmBulkDelete}
          onCancel={() => setConfirmingBulkDelete(false)}
        />
      )}
    </div>
  )
}

export { ProjectTreeView }
