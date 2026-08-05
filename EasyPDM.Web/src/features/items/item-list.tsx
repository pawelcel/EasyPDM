import { useEffect, useState } from "react"

import { api } from "@/api/client"
import type { Item, Project } from "@/api/types"
import { ConfirmDialog } from "@/components/ui/confirm-dialog"
import { Hint } from "@/components/ui/hint"
import type { DatabaseFilters } from "@/features/items/database-filters"
import { ItemDetailPanel } from "@/features/items/item-detail-panel"
import { ItemRow } from "@/features/items/item-row"
import { PartKindSelect, type PartKindFilter } from "@/features/items/part-kind-select"
import { RecordTypeSelect, type RecordType } from "@/features/items/record-type-select"
import { SavedFiltersBar } from "@/features/items/saved-filters-bar"
import { ManufacturerFilterSelect } from "@/features/manufacturers/manufacturer-filter-select"
import { ProjectDetailPanel } from "@/features/projects/project-detail-panel"
import { ProjectRow } from "@/features/projects/project-row"
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
}) {
  const { t } = useLanguage()
  const [recordType, setRecordType] = useState<RecordType>("all")
  const [partKind, setPartKind] = useState<PartKindFilter>("all")
  const [manufacturer, setManufacturer] = useState("")
  const [selection, setSelection] = useState<Selection | null>(null)
  // Element pobrany BEZPOŚREDNIO po ID (przez przycisk "Przejdź" na wierszu BOM-u), z
  // pominięciem aktualnie przefiltrowanej/wyszukanej listy po lewej — cel nawigacji może
  // wcale nie pasować do bieżących filtrów (inny rodzaj/producent/wynik wyszukiwania), więc
  // szukanie go w visibleItems byłoby zawodne (efekt niżej resetujący zaznaczenie przy
  // filtrowaniu i tak wywaliłby je jako "zniknęło z listy"). Ma pierwszeństwo przed
  // selection — czyszczony przy każdym zwykłym kliknięciu w lewej liście.
  const [externalItem, setExternalItem] = useState<Item | null>(null)
  const [confirmingDeleteId, setConfirmingDeleteId] = useState<string | null>(null)
  const [selectedItemChildren, setSelectedItemChildren] = useState<
    { item: Item; quantity: number; position: number }[]
  >([])

  async function handleSelectChild(childId: string) {
    setSelection(null)
    try {
      setExternalItem(await api.getItem(childId))
    } catch {
      setExternalItem(null)
    }
  }

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

  // Wczytanie zapisanego zestawu filtrów ustawia WSZYSTKIE pięć wartości naraz, więc celowo
  // omija powyższe funkcje z kaskadowym resetem (np. changeRecordType zerowałby partKind
  // i manufacturer, które ten sam zapis chce ustawić na coś innego niż "wszystkie").
  function applyFilters(f: DatabaseFilters) {
    onSearchChange(f.search)
    onTagChange(f.tag)
    setRecordType(f.recordType)
    setPartKind(f.partKind)
    setManufacturer(f.manufacturer)
    setSelection(null)
  }

  async function confirmDeleteCompletely() {
    if (!confirmingDeleteId) return
    await api.deleteItem(confirmingDeleteId)
    setConfirmingDeleteId(null)
    setSelection(null)
    setExternalItem(null)
    await onItemsRefetch()
  }

  const visibleProjects =
    recordType === "project"
      ? projects.filter((p) => !search || p.name.toLowerCase().includes(search.toLowerCase()))
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
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap gap-2">
        <RecordTypeSelect value={recordType} onChange={changeRecordType} />
        <PartKindSelect value={partKind} onChange={changePartKind} disabled={recordType !== "part"} />
        <ManufacturerFilterSelect
          value={manufacturer}
          onChange={setManufacturer}
          disabled={partKind !== "Zakupowa"}
        />
        <SavedFiltersBar
          currentFilters={{ search, tag, recordType, partKind, manufacturer }}
          onApply={applyFilters}
        />
      </div>

      {visibleProjects.length === 0 && visibleItems.length === 0 ? (
        <Hint>{t("database.empty")}</Hint>
      ) : (
        <div className="grid grid-cols-4 items-start gap-4">
          {/* max-h + overflow-y-auto — przy dużej bazie ta kolumna ma WŁASNY, ograniczony
              scroll zamiast rozciągać całą stronę do wysokości pełnej listy elementów
              (sticky top pod nagłówkiem, który sam jest "sticky top-0"). */}
          <div className="sticky top-24 col-span-1 flex max-h-[calc(100vh-6rem)] flex-col gap-0.5 overflow-y-auto rounded-xl bg-card p-2 ring-1 ring-foreground/10">
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

          <div className="col-span-3 rounded-xl bg-card p-4 ring-1 ring-foreground/10">
            {selectedItem ? (
              <ItemDetailPanel
                key={selectedItem.id}
                item={selectedItem}
                projectName={projects.find((p) => p.id === selectedItem.projectId)?.name}
                childEntries={selectedItemChildren}
                onSelectChild={handleSelectChild}
                onItemsRefetch={refetchItemsAndExternal}
                onTagsRefetch={onTagsRefetch}
                onDeleteCompletely={isAdmin ? () => setConfirmingDeleteId(selectedItem.id) : undefined}
                onDuplicated={(newId) => {
                  setExternalItem(null)
                  setSelection({ kind: "item", id: newId })
                }}
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
                onNavigateToProject={() => onNavigateToProject(selectedProject.id)}
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
        />
      )}
    </div>
  )
}

export { ItemList }
