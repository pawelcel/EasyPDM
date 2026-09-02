import { useEffect, useRef, useState } from "react"

import { api } from "@/api/client"
import type { Item, Project } from "@/api/types"
import { Button } from "@/components/ui/button"
import { ConfirmDialog } from "@/components/ui/confirm-dialog"
import { FormError } from "@/components/ui/form-error"
import { Hint } from "@/components/ui/hint"
import { ResizeHandle } from "@/components/ui/resize-handle"
import { ClientFilterSelect } from "@/features/items/client-filter-select"
import type { DatabaseFilters } from "@/features/items/database-filters"
import { DocumentationDialog } from "@/features/items/documentation-dialog"
import { ItemDetailPanel } from "@/features/items/item-detail-panel"
import { ItemRow } from "@/features/items/item-row"
import { PartKindSelect, type PartKindFilter } from "@/features/items/part-kind-select"
import { RecordTypeSelect, type RecordType } from "@/features/items/record-type-select"
import { SavedFiltersBar } from "@/features/items/saved-filters-bar"
import { ManufacturerFilterSelect } from "@/features/manufacturers/manufacturer-filter-select"
import { ProjectDetailPanel } from "@/features/projects/project-detail-panel"
import { ProjectRow } from "@/features/projects/project-row"
import { useResizableWidth } from "@/hooks/use-resizable-width"
import { useLanguage } from "@/i18n/use-language"

type Selection = { kind: "item"; id: string } | { kind: "project"; id: string }

function ItemList({
  items,
  loading,
  error,
  projects,
  search,
  tag,
  onSearchChange,
  onTagChange,
  isAdmin,
  onItemsRefetch,
  onTagsRefetch,
  onProjectsRefetch,
  onNavigateToProject,
  pendingSelectedItemId,
  onPendingSelectedItemIdConsumed,
}: {
  items: Item[]
  loading: boolean
  error: boolean
  projects: Project[]
  search: string
  tag: string
  onSearchChange: (value: string) => void
  onTagChange: (value: string) => void
  isAdmin: boolean
  onItemsRefetch: () => void | Promise<void>
  onTagsRefetch: () => void | Promise<void>
  onProjectsRefetch: () => void | Promise<void>
  onNavigateToProject: (projectId: string) => void
  // Nawigacja z zewnątrz (np. z dzwonka powiadomień) do konkretnego elementu, niezależnie
  // od aktualnych filtrów/wyszukiwania — zob. useEffect niżej, korzysta z tego samego
  // mechanizmu co "Przejdź" na wierszu BOM-u (handleSelectChild/externalItem).
  pendingSelectedItemId?: string | null
  onPendingSelectedItemIdConsumed?: () => void
}) {
  const { t } = useLanguage()
  const [recordType, setRecordType] = useState<RecordType>("all")
  const [partKind, setPartKind] = useState<PartKindFilter>("all")
  const [manufacturer, setManufacturer] = useState("")
  // clientId (jako string, jak wymagają wartości <Select>) klienta przypisanego do
  // PROJEKTU elementu/projektu — w odróżnieniu od "rodzaju"/"producenta" to nie jest
  // filtr specyficzny dla Części, więc działa niezależnie od recordType (na projektach
  // i na elementach naraz).
  const [client, setClient] = useState("")
  const [selection, setSelection] = useState<Selection | null>(null)
  // Zapamiętywana per przeglądarka, niezależnie od szerokości drzewa w widoku projektu
  // (features/tree/project-tree-view.tsx) — to inny układ (płaska lista, nie drzewo).
  const { width: listWidth, startResize } = useResizableWidth("easypdm.databaseListWidth", 220, 640, 320)
  // Element pobrany BEZPOŚREDNIO po ID (przez przycisk "Przejdź" na wierszu BOM-u), z
  // pominięciem aktualnie przefiltrowanej/wyszukanej listy po lewej — cel nawigacji może
  // wcale nie pasować do bieżących filtrów (inny rodzaj/producent/wynik wyszukiwania), więc
  // szukanie go w visibleItems byłoby zawodne (efekt niżej resetujący zaznaczenie przy
  // filtrowaniu i tak wywaliłby je jako "zniknęło z listy"). Ma pierwszeństwo przed
  // selection — czyszczony przy każdym zwykłym kliknięciu w lewej liście.
  const [externalItem, setExternalItem] = useState<Item | null>(null)
  const [confirmingDeleteId, setConfirmingDeleteId] = useState<string | null>(null)
  const [deletingPending, setDeletingPending] = useState(false)
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [confirmingProjectDelete, setConfirmingProjectDelete] = useState(false)
  const [projectDeletingPending, setProjectDeletingPending] = useState(false)
  const [projectDeleteError, setProjectDeleteError] = useState<string | null>(null)
  // Błąd akcji przy zaznaczonym projekcie/elemencie z belki nad listą (duplikuj) — zob.
  // features/tree/project-tree-view.tsx, ta sama belka co w widoku projektu.
  const [itemActionError, setItemActionError] = useState<string | null>(null)
  const [selectedItemChildren, setSelectedItemChildren] = useState<
    { item: Item; quantity: number; position: number }[]
  >([])

  // Licznik żądań — jeśli użytkownik kliknie "Przejdź" na drugim wierszu BOM-u zanim
  // odpowiedź na pierwsze kliknięcie wróci, a odpowiedzi dotrą w innej kolejności niż
  // zostały wysłane, bez tego strażnika panel pokazałby element ze STARSZEGO kliknięcia
  // zamiast tego, na który użytkownik faktycznie czeka.
  const selectChildRequestId = useRef(0)

  async function handleSelectChild(childId: string) {
    setSelection(null)
    const requestId = ++selectChildRequestId.current
    try {
      const item = await api.getItem(childId)
      if (selectChildRequestId.current === requestId) setExternalItem(item)
    } catch {
      if (selectChildRequestId.current === requestId) setExternalItem(null)
    }
  }

  useEffect(() => {
    if (!pendingSelectedItemId) return
    handleSelectChild(pendingSelectedItemId)
    onPendingSelectedItemIdConsumed?.()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pendingSelectedItemId])

  // externalItem to zamrożona migawka (pobrana raz przez handleSelectChild), niepowiązana
  // z odświeżeniem listy items — bez tego edycje (zmiana nazwy/statusu/właściwości) na
  // elemencie pokazanym przez "Przejdź" nie byłyby widoczne w panelu, dopóki ktoś nie
  // zaznaczyłby czegoś innego i nie wrócił.
  async function refetchItemsAndExternal() {
    await onItemsRefetch()
    if (externalItem) {
      try {
        setExternalItem(await api.getItem(externalItem.id))
      } catch {
        setExternalItem(null)
      }
    }
  }

  // Filtr "rodzaj części" ma sens tylko dla Części, a "producent" tylko dla Części
  // zakupowych — zmiana filtra wyżej w hierarchii kasuje te niżej, żeby nie został
  // "ukryty" filtr, który dalej coś zawęża, choć jego kontrolka jest już wyszarzona.
  function changeRecordType(next: RecordType) {
    setRecordType(next)
    setPartKind("all")
    setManufacturer("")
    setSelection(null)
  }

  function changePartKind(next: PartKindFilter) {
    setPartKind(next)
    setManufacturer("")
  }

  // Wczytanie zapisanego zestawu filtrów ustawia WSZYSTKIE wartości naraz, więc celowo
  // omija powyższe funkcje z kaskadowym resetem (np. changeRecordType zerowałby partKind
  // i manufacturer, które ten sam zapis chce ustawić na coś innego niż "wszystkie").
  function applyFilters(f: DatabaseFilters) {
    onSearchChange(f.search)
    onTagChange(f.tag)
    setRecordType(f.recordType)
    setPartKind(f.partKind)
    setManufacturer(f.manufacturer)
    setClient(f.client)
    setSelection(null)
  }

  async function confirmDeleteCompletely() {
    if (!confirmingDeleteId) return
    setDeletingPending(true)
    setDeleteError(null)
    try {
      await api.deleteItem(confirmingDeleteId)
      setConfirmingDeleteId(null)
      setSelection(null)
      setExternalItem(null)
      await onItemsRefetch()
    } catch (err) {
      setDeleteError(err instanceof Error ? err.message : t("item.deleteFailed"))
    } finally {
      setDeletingPending(false)
    }
  }

  async function handleDuplicateSelected() {
    if (!selectedItem) return
    setItemActionError(null)
    try {
      const { id: newItemId } = await api.duplicateItem(selectedItem.id)
      await refetchItemsAndExternal()
      setExternalItem(null)
      setSelection({ kind: "item", id: newItemId })
    } catch (err) {
      setItemActionError(err instanceof Error ? err.message : t("item.duplicateFailed"))
    }
  }

  async function confirmProjectDelete() {
    if (!selectedProject) return
    setProjectDeletingPending(true)
    setProjectDeleteError(null)
    try {
      await api.deleteProject(selectedProject.id)
      setConfirmingProjectDelete(false)
      setSelection(null)
      await onProjectsRefetch()
    } catch (err) {
      setProjectDeleteError(err instanceof Error ? err.message : t("project.deleteFailed"))
    } finally {
      setProjectDeletingPending(false)
    }
  }

  // Elementy same nie niosą klienta -- tylko ich PROJEKT (Project.clientId) -- stąd mapa
  // do doczytania klienta elementu po projectId; element bez projektu (projectId=null,
  // zob. elementy bez projektu) nigdy nie pasuje do żadnego wybranego klienta.
  const projectsById = new Map(projects.map((p) => [p.id, p]))

  const visibleProjects =
    recordType === "project"
      ? projects.filter(
          (p) =>
            (!search || p.name.toLowerCase().includes(search.toLowerCase())) &&
            (!client || String(p.clientId) === client)
        )
      : []

  const visibleItems =
    recordType === "project"
      ? []
      : items.filter((item) => {
          if (recordType === "part" && item.itemType !== "part") return false
          if (recordType === "assembly" && item.itemType !== "assembly") return false
          if (recordType === "other" && (item.itemType === "part" || item.itemType === "assembly")) {
            return false
          }
          if (partKind !== "all" && (item.itemType !== "part" || item.properties.rodzaj !== partKind)) {
            return false
          }
          if (manufacturer && (item.itemType !== "part" || item.properties.manufacturer !== manufacturer)) {
            return false
          }
          if (client && String(projectsById.get(item.projectId ?? "")?.clientId) !== client) {
            return false
          }
          return true
        })

  const selectedItem =
    externalItem ?? (selection?.kind === "item" ? (visibleItems.find((i) => i.id === selection.id) ?? null) : null)

  // Jeśli zaznaczony rekord zniknie z bieżącej, przefiltrowanej listy (zmiana filtra,
  // odświeżenie, usunięcie) — panel po prawej wraca do stanu "nic nie wybrano".
  useEffect(() => {
    if (!selection) return
    const stillVisible =
      selection.kind === "item"
        ? visibleItems.some((item) => item.id === selection.id)
        : visibleProjects.some((p) => p.id === selection.id)
    if (!stillVisible) setSelection(null)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [visibleItems, visibleProjects])

  // W odróżnieniu od widoku projektu (project-tree-view.tsx), tu nie ma z góry załadowanego
  // całego drzewka relacji — zaznaczony element może być z dowolnego projektu, więc bezpośrednie
  // dzieci (do sekcji BOM/Pliki w ItemDetailPanel) dociągamy osobno, dopiero po zaznaczeniu.
  useEffect(() => {
    if (!selectedItem) {
      setSelectedItemChildren([])
      return
    }
    let cancelled = false
    api
      .getItemChildren(selectedItem.id)
      .then((children) => {
        if (!cancelled) setSelectedItemChildren(children)
      })
      .catch(() => {
        if (!cancelled) setSelectedItemChildren([])
      })
    return () => {
      cancelled = true
    }
    // Zależność po id (nie po całym obiekcie) — visibleItems/selectedItem dostają nową
    // referencję tablicy przy każdym re-renderze; refetch ma sens tylko przy realnej zmianie
    // zaznaczenia, nie przy każdym niepowiązanym re-renderze listy.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedItem?.id])

  if (loading) return <Hint>{t("common.loading")}</Hint>
  if (error) return <Hint>{t("database.loadError")}</Hint>

  const selectedProject =
    selection?.kind === "project" ? (visibleProjects.find((p) => p.id === selection.id) ?? null) : null
  const deletingItem = confirmingDeleteId
    ? (externalItem?.id === confirmingDeleteId ? externalItem : visibleItems.find((i) => i.id === confirmingDeleteId))
    : null

  return (
    <div className="flex h-full flex-col gap-3">
      <div className="flex flex-wrap gap-2">
        <RecordTypeSelect value={recordType} onChange={changeRecordType} />
        <PartKindSelect value={partKind} onChange={changePartKind} disabled={recordType !== "part"} />
        <ManufacturerFilterSelect
          value={manufacturer}
          onChange={setManufacturer}
          disabled={partKind !== "Zakupowa"}
        />
        <ClientFilterSelect projects={projects} value={client} onChange={setClient} />
        <SavedFiltersBar
          currentFilters={{ search, tag, recordType, partKind, manufacturer, client }}
          onApply={applyFilters}
        />
      </div>

      {/* Belka zawsze widoczna (nie tylko po zaznaczeniu czegoś) — zmieniają się jedynie
          przyciski w środku, żeby układ się nie "przeskakiwał" przy zaznaczaniu/odznaczaniu.
          Akcje dotyczące aktualnie zaznaczonego wiersza (projekt albo element) — wyrównane
          (absolute, position: relative na belce) do lewej krawędzi panelu podglądu poniżej
          (listWidth + szerokość ResizeHandle), tak samo jak w widoku projektu (zob.
          features/tree/project-tree-view.tsx). Dawniej renderowane wewnątrz
          ProjectDetailPanel/ItemDetailPanel — stamtąd całkowicie usunięte (hideActions). */}
      <div className="relative h-9 rounded-xl bg-card p-2 ring-1 ring-foreground/10">
        {selectedProject && (
          <div
            className="absolute top-1/2 flex -translate-y-1/2 flex-wrap items-center gap-1.5"
            style={{ left: listWidth + 16, maxWidth: `calc(100% - ${listWidth + 16}px)` }}
          >
            <Button size="sm" variant="outline" onClick={() => onNavigateToProject(selectedProject.id)}>
              {t("project.goToProject")}
            </Button>
            <DocumentationDialog
              trigger={
                <Button size="sm" variant="outline">
                  {t("documentation.button")}
                </Button>
              }
              fetchExtensions={() => api.getProjectDocumentationExtensions(selectedProject.id)}
              buildDownloadUrl={(extensions) => api.projectDocumentationUrl(selectedProject.id, extensions)}
            />
            {isAdmin && (
              <Button
                size="sm"
                variant="destructive"
                onClick={() => {
                  setProjectDeleteError(null)
                  setConfirmingProjectDelete(true)
                }}
              >
                {t("project.deleteButton")}
              </Button>
            )}
          </div>
        )}

        {selectedItem && (
          <div
            className="absolute top-1/2 flex -translate-y-1/2 flex-wrap items-center gap-1.5"
            style={{ left: listWidth + 16, maxWidth: `calc(100% - ${listWidth + 16}px)` }}
          >
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
              <Button
                size="sm"
                variant="destructive"
                onClick={() => {
                  setDeleteError(null)
                  setConfirmingDeleteId(selectedItem.id)
                }}
              >
                {t("item.deleteCompletely")}
              </Button>
            )}
          </div>
        )}
      </div>
      <FormError>{itemActionError}</FormError>

      {visibleProjects.length === 0 && visibleItems.length === 0 ? (
        <Hint>{t("database.empty")}</Hint>
      ) : (
        <div className="flex min-h-0 flex-1">
          {/* Własny, niezależny scroll tej kolumny (a nie całej strony) — przy dużej bazie
              lista elementów rośnie dowolnie, ale rodzic ma ograniczoną wysokość (h-full na
              korzeniu + overflow-hidden na <main> dla tego widoku, zob. App.tsx). Szerokość
              przeciągalna uchwytem niżej (zob. useResizableWidth). */}
          <div
            className="flex min-h-0 shrink-0 flex-col gap-0.5 overflow-y-auto rounded-xl bg-card p-2 ring-1 ring-foreground/10"
            style={{ width: listWidth }}
          >
            {visibleProjects.map((project) => (
              <ProjectRow
                key={project.id}
                project={project}
                selected={!externalItem && selection?.kind === "project" && selection.id === project.id}
                onSelect={() => {
                  setExternalItem(null)
                  setSelection({ kind: "project", id: project.id })
                }}
              />
            ))}
            {visibleItems.map((item) => (
              <ItemRow
                key={item.id}
                item={item}
                projectName={projects.find((p) => p.id === item.projectId)?.name}
                selected={!externalItem && selection?.kind === "item" && selection.id === item.id}
                onSelect={() => {
                  setExternalItem(null)
                  setSelection({ kind: "item", id: item.id })
                }}
              />
            ))}
          </div>

          <ResizeHandle onMouseDown={startResize} />

          <div className="min-h-0 min-w-0 flex-1 overflow-y-auto rounded-xl bg-card p-4 ring-1 ring-foreground/10">
            {selectedItem ? (
              <ItemDetailPanel
                key={selectedItem.id}
                item={selectedItem}
                projectName={projects.find((p) => p.id === selectedItem.projectId)?.name}
                childEntries={selectedItemChildren}
                onSelectChild={handleSelectChild}
                onItemsRefetch={refetchItemsAndExternal}
                onTagsRefetch={onTagsRefetch}
                // Akcje (duplikuj/dokumentacja/usuń całkowicie) renderowane w belce nad
                // listą zamiast tutaj — zob. hideActions.
                hideActions
              />
            ) : selectedProject ? (
              <ProjectDetailPanel
                key={selectedProject.id}
                project={selectedProject}
                isAdmin={isAdmin}
                onUpdated={onProjectsRefetch}
                onDeleted={async () => {
                  setSelection(null)
                  await onProjectsRefetch()
                }}
                hideActions
              />
            ) : (
              <Hint>{t("database.selectItemHint")}</Hint>
            )}
          </div>
        </div>
      )}

      {deletingItem && (
        <ConfirmDialog
          open
          title={t("item.deleteCompletely")}
          description={t("item.deleteConfirmDescription", { name: deletingItem.fileName })}
          confirmLabel={t("item.deleteCompletely")}
          variant="destructive"
          onConfirm={confirmDeleteCompletely}
          onCancel={() => setConfirmingDeleteId(null)}
          pending={deletingPending}
          error={deleteError}
        />
      )}

      {confirmingProjectDelete && selectedProject && (
        <ConfirmDialog
          open
          title={t("project.deleteButton")}
          description={t("project.deleteConfirmDescription", {
            name: selectedProject.name,
            count: selectedProject.itemCount,
          })}
          confirmLabel={t("project.deleteButton")}
          variant="destructive"
          onConfirm={confirmProjectDelete}
          onCancel={() => setConfirmingProjectDelete(false)}
          pending={projectDeletingPending}
          error={projectDeleteError}
        />
      )}
    </div>
  )
}

export { ItemList }
