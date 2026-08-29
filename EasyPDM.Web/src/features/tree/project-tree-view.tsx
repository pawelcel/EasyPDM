
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
import { ResizeHandle } from "@/components/ui/resize-handle"
import { AddTagRow } from "@/features/tags/add-tag-row"
import { DocumentationDialog } from "@/features/items/documentation-dialog"
import { ItemDetailPanel } from "@/features/items/item-detail-panel"
import { ProjectDetailPanel } from "@/features/projects/project-detail-panel"
import { ItemTree } from "@/features/tree/item-tree"
import { useProjectTree } from "@/features/tree/use-project-tree"
import { useResizableWidth } from "@/hooks/use-resizable-width"
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
  const [confirmingProjectDelete, setConfirmingProjectDelete] = useState(false)
  // Błędy akcji przy zaznaczonym projekcie/elemencie z belki nad drzewem (usuń ze
  // struktury, duplikuj) — osobne od bulkError (akcje masowe) i od formularza projektu.
  const [itemActionError, setItemActionError] = useState<string | null>(null)

  // Zaznaczenie "checkboxami" (do akcji masowych) jest niezależne od "selection" powyżej —
  // to drugie decyduje, co pokazuje panel po prawej, pierwsze służy wyłącznie do wsadowego
  // dodania tagu / zmiany statusu / usunięcia wielu elementów naraz. Checkboxy istnieją w
  // drzewie tylko, gdy "selectionMode" jest włączony — bez świadomego wejścia w ten tryb nie
  // da się nic zaznaczyć (unika przypadkowego zaznaczania przy zwykłym przeglądaniu drzewa).
  const [selectionMode, setSelectionMode] = useState(false)
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set())
  const [confirmingBulkDelete, setConfirmingBulkDelete] = useState(false)
  const [bulkError, setBulkError] = useState<string | null>(null)
  // Zapamiętywana per przeglądarka (nie per projekt), żeby użytkownik ustawił ją raz i
  // miał tak samo w każdym projekcie.
  const { width: treeWidth, startResize } = useResizableWidth("easypdm.projectTreeWidth", 220, 640, 320)

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
    setItemActionError(null)
    try {
      if (selection.parentId) {
        // Element z rodzicem — odpinamy konkretną krawędź, sam rekord zostaje. Element
        // staje się BEZ PROJEKTU (moveItemToProject(id, null)) zamiast pokazywać się w
        // korzeniu bieżącego projektu — zaśmiecałoby to jego strukturę czymś, co z tym
        // projektem nie ma już nic wspólnego. Nadal w pełni widoczny i znajdywalny przez
        // globalne wyszukiwanie ("Cała baza"); to samo zachowanie co synchronizacja BOM w
        // makrach CAD (zob. sync_stale_children/SyncStaleChildren).
        await api.removeChild(selection.parentId, selection.id)
        await api.moveItemToProject(selection.id, null)
      } else {
        // Element bez rodzica — nie ma czego odpiąć, więc przestaje być widoczny
        // jako korzeń w drzewku (rekord i przynależność do projektu zostają).
        await api.setShowInTree(selection.id, false)
      }
      setSelection({ kind: "project" })
      await tree.refetch()
    } catch (err) {
      setItemActionError(err instanceof Error ? err.message : t("item.removeFromStructureFailed"))
    }
  }

  async function handleDuplicateSelected() {
    if (!selectedItem) return
    setItemActionError(null)
    try {
      const { id: newItemId } = await api.duplicateItem(
        selectedItem.id,
        selectedItemParentId !== undefined
          ? { parentId: selectedItemParentId, insertAfterOriginal: true }
          : undefined
      )
      await tree.refetch()
      setSelection({ kind: "item", id: newItemId, parentId: selectedItemParentId })
    } catch (err) {
      setItemActionError(err instanceof Error ? err.message : t("item.duplicateFailed"))
    }
  }

  async function confirmDeleteCompletely() {
    if (!selectedItem) return
    await api.deleteItem(selectedItem.id)
    setConfirmingDelete(false)
    setSelection({ kind: "project" })
    await tree.refetch()
  }

  async function confirmProjectDelete() {
    await api.deleteProject(project.id)
    setConfirmingProjectDelete(false)
    await onProjectDeleted()
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
    <div className="flex h-full flex-col gap-3">
      <div className="relative flex flex-wrap items-center gap-2 rounded-xl bg-card p-2 ring-1 ring-foreground/10">
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

        {/* Akcje dotyczące aktualnie zaznaczonego węzła drzewa (projekt albo element) —
            wyrównane (absolute, position: relative na belce) do lewej krawędzi panelu
            podglądu poniżej (treeWidth + szerokość ResizeHandle, zob. useResizableWidth),
            żeby nie mieszać się z przyciskiem "Zaznacz wiele"/akcjami masowymi po lewej,
            a jednocześnie podążały za suwakiem szerokości drzewa. Dawniej renderowane
            wewnątrz ProjectDetailPanel/ItemDetailPanel — stamtąd całkowicie usunięte
            (hideActions). */}
        {selection.kind === "project" && (
          <div
            className="absolute top-1/2 flex -translate-y-1/2 items-center gap-1.5"
            style={{ left: treeWidth + 16 }}
          >
            <DocumentationDialog
              trigger={
                <Button size="sm" variant="outline">
                  {t("documentation.button")}
                </Button>
              }
              fetchExtensions={() => api.getProjectDocumentationExtensions(project.id)}
              buildDownloadUrl={(extensions) => api.projectDocumentationUrl(project.id, extensions)}
            />
            {isAdmin && (
              <Button size="sm" variant="destructive" onClick={() => setConfirmingProjectDelete(true)}>
                {t("project.deleteButton")}
              </Button>
            )}
          </div>
        )}

        {selection.kind === "item" && selectedItem && (
          <div
            className="absolute top-1/2 flex -translate-y-1/2 items-center gap-1.5"
            style={{ left: treeWidth + 16 }}
          >
            {selectedItemParentId !== undefined && (
              <Button size="sm" variant="outline" onClick={handleRemoveFromStructure}>
                {t("item.removeFromStructure")}
              </Button>
            )}
            {(selectedItem.itemType === "part" || selectedItem.itemType === "assembly") && (
              <Button size="sm" variant="outline" onClick={handleDuplicateSelected}>
                {t("item.duplicate")}
              </Button>
            )}
            {(selectedItem.itemType === "part" || selectedItem.itemType === "assembly") && (
              <DocumentationDialog
                trigger={
                  <Button size="sm" variant="outline">
                    {t("documentation.button")}
                  </Button>
                }
                fetchExtensions={() => api.getItemDocumentationExtensions(selectedItem.id)}
                buildDownloadUrl={(extensions) => api.itemDocumentationUrl(selectedItem.id, extensions)}
              />
            )}
            {isAdmin && (
              <Button size="sm" variant="destructive" onClick={() => setConfirmingDelete(true)}>
                {t("item.deleteCompletely")}
              </Button>
            )}
          </div>
        )}
      </div>
      <FormError>{bulkError}</FormError>
      <FormError>{itemActionError}</FormError>

      <div className="flex min-h-0 flex-1">
        <div
          className="flex min-h-0 shrink-0 flex-col overflow-y-auto rounded-xl bg-card ring-1 ring-foreground/10"
          style={{ width: treeWidth }}
        >
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

        <ResizeHandle onMouseDown={startResize} />

        <div className="min-h-0 min-w-0 flex-1 overflow-y-auto rounded-xl bg-card p-4 ring-1 ring-foreground/10">
          {selectedItem ? (
            <ItemDetailPanel
              key={selectedItem.id}
              item={selectedItem}
              projectName={project.name}
              childEntries={tree.childrenOf(selectedItem.id)}
              onSelectChild={(childId, parentId) => setSelection({ kind: "item", id: childId, parentId })}
              onItemsRefetch={tree.refetch}
              onTagsRefetch={onTagsRefetch}
              // Akcje (usuń ze struktury/duplikuj/dokumentacja/usuń całkowicie) renderowane
              // w belce nad drzewem zamiast tutaj — zob. hideActions.
              hideActions
            />
          ) : (
            <ProjectDetailPanel
              project={project}
              isAdmin={isAdmin}
              onUpdated={onProjectUpdated}
              onDeleted={onProjectDeleted}
              hideActions
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

      {confirmingProjectDelete && (
        <ConfirmDialog
          open
          title={t("project.deleteButton")}
          description={t("project.deleteConfirmDescription", {
            name: project.name,
            count: project.itemCount,
          })}
          confirmLabel={t("project.deleteButton")}
          variant="destructive"
          onConfirm={confirmProjectDelete}
          onCancel={() => setConfirmingProjectDelete(false)}
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
