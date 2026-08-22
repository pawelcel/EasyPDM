import { Fragment, useCallback, useEffect, useState } from "react"
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
import { ArrowUpRight, Eye, GripVertical, Upload } from "lucide-react"

import { api } from "@/api/client"
import {
  bomPositionLabel,
  canEditOwnerLocked,
  fileTypeLabel,
  isLocked,
  itemDisplayLabel,
  itemTypeLabelKey,
  type BomEntry,
  type Item,
} from "@/api/types"
import { Button } from "@/components/ui/button"
import { FormError } from "@/components/ui/form-error"
import { Hint } from "@/components/ui/hint"
import { Input } from "@/components/ui/input"
import { SectionLabel } from "@/components/ui/section-label"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { TagPill } from "@/components/ui/tag-pill"
import { AddNodeDialog } from "@/features/items/add-node-dialog"
import { AddTagRow } from "@/features/tags/add-tag-row"
import { AttachmentsPanel } from "@/features/items/attachments-panel"
import { useAuth } from "@/features/auth/use-auth"
import { DocumentationDialog } from "@/features/items/documentation-dialog"
import { ItemHistoryPanel } from "@/features/items/item-history-panel"
import { OwnerControl } from "@/features/items/owner-control"
import { PartPropertyForm, PartSummaryFields } from "@/features/items/part-property-form"
import { PropertyEditor } from "@/features/items/property-editor"
import { StatusControl } from "@/features/items/status-control"
import { ItemPreviewBox } from "@/features/preview/item-preview-box"
import { PreviewDialog } from "@/features/preview/preview-dialog"
import { useLanguage } from "@/i18n/use-language"
import { previewKindOf } from "@/lib/file-preview"

// W BOM-ie nie każda Część ma Materiał/Producenta/Numery zamówieniowe (zależy od "rodzaju") —
// brakującą wartość pokazujemy jako "-", zamiast pustej komórki.
function bomPropertyOrDash(properties: Record<string, unknown>, key: string): string {
  const value = properties[key]
  return typeof value === "string" && value.trim() ? value : "-"
}

function ItemDetailPanel({
  item,
  projectName,
  showHeader = true,
  childEntries = [],
  onSelectChild,
  onItemsRefetch,
  onTagsRefetch,
  onRemoveFromStructure,
  onDeleteCompletely,
  onDuplicated,
  duplicateParentId,
}: {
  item: Item
  projectName?: string
  showHeader?: boolean
  childEntries?: { item: Item; quantity: number; position: number }[]
  // parentId przekazywany razem z id — dla bezpośredniego dziecka to zawsze TEN element
  // (poprawny rodzic). Dla zagłębionego wpisu (głębszego niż jeden poziom) to celowo
  // undefined: jego prawdziwy bezpośredni rodzic to jakieś POD-złożenie (nie to złożenie),
  // więc podanie złego parentId zepsułoby "Usuń ze struktury" po przejściu tam (odpięłoby od
  // NIEWŁAŚCIWEGO rodzica albo błędnie schowało jako "korzeń"). undefined ≠ null: null to
  // świadomie stwierdzony PRAWDZIWY korzeń projektu (bez żadnego rodzica), undefined to
  // "rodzic istnieje, ale nieznany tutaj" — z undefined "Usuń ze struktury" się nie pokazuje.
  onSelectChild?: (id: string, parentId: string | null | undefined) => void
  onItemsRefetch: () => void | Promise<void>
  onTagsRefetch: () => void | Promise<void>
  onRemoveFromStructure?: () => void | Promise<void>
  onDeleteCompletely?: () => void | Promise<void>
  onDuplicated?: (newItemId: string) => void | Promise<void>
  // Obecność tego pola (nawet gdy null — oryginał jest korzeniem projektu) włącza
  // umieszczenie kopii DOKŁADNIE pod oryginałem, na tym samym poziomie struktury. Brak tego
  // pola (undefined — np. duplikowanie z widoku całej bazy, bez załadowanej struktury/relacji)
  // po prostu dopisuje kopię na koniec listy korzeni projektu.
  duplicateParentId?: string | null
}) {
  const { t } = useLanguage()
  const { user } = useAuth()
  const [actionError, setActionError] = useState<string | null>(null)
  const canDuplicate =
    (item.itemType === "part" || item.itemType === "assembly") && onDuplicated !== undefined
  const canDownloadDocumentation = item.itemType === "part" || item.itemType === "assembly"
  // Niezależne od isLocked (status 'w_pracy') — dopóki właściciel ma tego elementu
  // zablokowanego, tylko on może edytować (nawet admin nie omija tego).
  const ownerEditable = user ? canEditOwnerLocked(item, user.id) : true

  // ItemHistoryPanel odświeża się sam tylko przy zmianie itemId (wejście w inny element) —
  // akcje, które dopisują nowy wpis do historii (status, blokada/zwolnienie, właściwości,
  // załączniki), NIE zmieniają itemId, więc same z siebie nie odświeżyłyby panelu historii.
  // Ten licznik przekazywany jest do ItemHistoryPanel jako dodatkowa zależność, żeby po
  // każdej takiej akcji faktycznie doczytał świeże dane zamiast czekać na ponowne zaznaczenie.
  const [historyRefreshSignal, setHistoryRefreshSignal] = useState(0)
  const [previewingMainFile, setPreviewingMainFile] = useState(false)
  const [previewingFileId, setPreviewingFileId] = useState<string | null>(null)
  async function refreshAfterAction() {
    setHistoryRefreshSignal((n) => n + 1)
    await onItemsRefetch()
  }

  async function handleDuplicate() {
    setActionError(null)
    try {
      const { id: newItemId } = await api.duplicateItem(
        item.id,
        duplicateParentId !== undefined
          ? { parentId: duplicateParentId, insertAfterOriginal: true }
          : undefined
      )
      await onItemsRefetch()
      await onDuplicated?.(newItemId)
    } catch (err) {
      setActionError(err instanceof Error ? err.message : t("item.duplicateFailed"))
    }
  }

  // onRemoveFromStructure to funkcja przekazana z rodzica (project-tree-view.tsx) — jeśli
  // backend ją odrzuci (np. element/rodzic zablokowany przez innego właściciela), rzuci
  // błąd; łapiemy go tutaj, żeby przycisk nie "milczał" zamiast pokazać komunikat.
  async function handleRemoveFromStructureClick() {
    if (!onRemoveFromStructure) return
    setActionError(null)
    try {
      await onRemoveFromStructure()
    } catch (err) {
      setActionError(err instanceof Error ? err.message : t("item.removeFromStructureFailed"))
    }
  }
  const attachedFiles = childEntries.filter((c) => c.item.itemType === "file").map((c) => c.item)
  const bomEntries = childEntries.filter(
    (c) => c.item.itemType === "part" || c.item.itemType === "assembly"
  )
  const modified = item.modifiedAt ? new Date(item.modifiedAt).toLocaleString("pl-PL") : "—"
  const typeLabelKey = itemTypeLabelKey(item)
  const typeLabel = typeLabelKey ? t(typeLabelKey) : fileTypeLabel(item)

  const [bomError, setBomError] = useState<string | null>(null)
  const [activeChildId, setActiveChildId] = useState<string | null>(null)
  const [nestedBom, setNestedBom] = useState<BomEntry[]>([])
  const dragSensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 4 } }))

  // Elementy zagłębione głębiej niż bezpośrednie dziecko (np. część w złożeniu podpiętym
  // pod to złożenie) — bezpośrednie dzieci przychodzą przez childEntries/bomEntries,
  // to dociąga resztę.
  const refetchNestedBom = useCallback(() => {
    api.getBom(item.id).then(setNestedBom).catch(() => setNestedBom([]))
  }, [item.id])

  // Odświeżamy przy wejściu w ten element ORAZ za każdym razem, gdy childEntries się zmienia
  // (nowa referencja tablicy przychodzi po każdym tree.refetch()) — zmiana L.p. bezpośrednich
  // dzieci może przyjść nie tylko z tej tabeli (drag&drop/ręczny wpis tutaj), ale też z
  // przeciągania w drzewku po lewej, dodania/usunięcia elementu gdzie indziej itd. Grupowanie
  // zagłębionych wpisów po path[0] opiera się na aktualnych "position", więc musi nadążać za
  // KAŻDĄ zmianą struktury, nie tylko tą zainicjowaną z poziomu BOM-u.
  useEffect(() => {
    if (item.itemType !== "assembly") return
    refetchNestedBom()
  }, [item.itemType, refetchNestedBom, childEntries])

  // path[0] odpowiada "position" bezpośredniego dziecka, pod którym dany wpis się
  // zagłębia — backend zwraca wiersze już posortowane po path, więc kolejność w każdej
  // grupie jest od razu poprawna (depth-first).
  const nestedByTopPosition = new Map<number, BomEntry[]>()
  for (const entry of nestedBom) {
    const top = entry.path[0]
    const group = nestedByTopPosition.get(top)
    if (group) group.push(entry)
    else nestedByTopPosition.set(top, [entry])
  }

  async function handleBomDragEnd(event: DragEndEvent) {
    setActiveChildId(null)
    const { active, over } = event
    if (!over || active.id === over.id) return

    const ids = bomEntries.map((e) => e.item.id)
    const fromIndex = ids.indexOf(String(active.id))
    const toIndex = ids.indexOf(String(over.id))
    if (fromIndex === -1 || toIndex === -1) return

    const reordered = arrayMove(ids, fromIndex, toIndex)

    setBomError(null)
    try {
      await api.reorderChildren(item.id, reordered)
      await onItemsRefetch()
    } catch (err) {
      setBomError(err instanceof Error ? err.message : t("item.reorderFailed"))
    }
  }

  async function handleAddTag(name: string) {
    await api.addTag(item.id, name)
    await onTagsRefetch()
    await onItemsRefetch()
  }

  async function handleRemoveTag(tagName: string) {
    await api.removeTag(item.id, tagName)
    await onItemsRefetch()
  }

  return (
    <div>
      {(onRemoveFromStructure || canDuplicate || canDownloadDocumentation || onDeleteCompletely) && (
        <div className="mb-3 flex flex-col gap-1.5 border-b pb-3">
          <div className="flex gap-1.5">
            {onRemoveFromStructure && (
              <Button size="sm" variant="outline" onClick={handleRemoveFromStructureClick}>
                {t("item.removeFromStructure")}
              </Button>
            )}
            {canDuplicate && (
              <Button size="sm" variant="outline" onClick={handleDuplicate}>
                {t("item.duplicate")}
              </Button>
            )}
            {canDownloadDocumentation && (
              <DocumentationDialog
                trigger={
                  <Button size="sm" variant="outline">
                    {t("documentation.button")}
                  </Button>
                }
                fetchExtensions={() => api.getItemDocumentationExtensions(item.id)}
                buildDownloadUrl={(extensions) => api.itemDocumentationUrl(item.id, extensions)}
              />
            )}
            {onDeleteCompletely && (
              <Button size="sm" variant="destructive" onClick={onDeleteCompletely}>
                {t("item.deleteCompletely")}
              </Button>
            )}
          </div>
          <FormError>{actionError}</FormError>
        </div>
      )}

      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="min-w-0 flex-1">
          {showHeader && (
            <>
              <div className="text-[15px] font-semibold">{itemDisplayLabel(item)}</div>
              <div className="text-[12.5px] text-muted-foreground">
                {typeLabel} · {t("item.modifiedOn")} {modified}
                {projectName ? ` · ${projectName}` : ""}
              </div>
            </>
          )}

          {(item.itemType === "part" || item.itemType === "assembly") && (
            <div className="mt-2">
              <StatusControl item={item} disabled={!ownerEditable} onChanged={refreshAfterAction} />
              <OwnerControl item={item} onChanged={refreshAfterAction} />
            </div>
          )}

          {(item.itemType === "part" || item.itemType === "assembly") && (
            <div className="mt-3">
              <PartSummaryFields item={item} onChanged={refreshAfterAction} />
            </div>
          )}

          {item.itemType === "file" &&
            (item.filePath ? (
              <div className="mt-3 flex items-center gap-3">
                <a className="text-[13px] text-primary hover:underline" href={api.fileDownloadUrl(item.id)} download>
                  {t("item.downloadFile")}
                </a>
                {previewKindOf(item.fileName) && (
                  <Button size="sm" variant="outline" onClick={() => setPreviewingMainFile(true)}>
                    <Eye className="size-3.5" /> {t("common.preview")}
                  </Button>
                )}
              </div>
            ) : (
              <Hint>{t("item.fileNotUploaded")}</Hint>
            ))}
        </div>

        <ItemPreviewBox item={item} refreshSignal={historyRefreshSignal} />
      </div>

      {previewingMainFile && item.itemType === "file" && (
        <PreviewDialog
          open
          onOpenChange={(open) => !open && setPreviewingMainFile(false)}
          fileName={item.fileName}
          url={api.fileDownloadUrl(item.id)}
          kind={previewKindOf(item.fileName)!}
        />
      )}

      {item.itemType === "folder" && (
        <div className="mt-3">
          <AddNodeDialog
            trigger={
              <Button size="sm" variant="outline">
                <Upload className="size-3.5" /> {t("item.uploadFile")}
              </Button>
            }
            projectId={item.projectId}
            parentId={item.id}
            parentType={item.itemType}
            lockMode="file"
            onCreated={onItemsRefetch}
          />
        </div>
      )}

      <SectionLabel>{t("item.tags")}</SectionLabel>
      <div className="flex flex-wrap gap-1.5">
        {item.tags.length > 0 ? (
          item.tags.map((tag) => (
            <TagPill key={tag} name={tag} onRemove={() => handleRemoveTag(tag)} />
          ))
        ) : (
          <Hint>{t("item.noTags")}</Hint>
        )}
      </div>
      <AddTagRow onAdd={handleAddTag} className="mt-2" />

      <SectionLabel>{t("item.properties")}</SectionLabel>
      {item.itemType === "part" ? (
        <PartPropertyForm item={item} onChanged={refreshAfterAction} />
      ) : (
        <PropertyEditor
          itemId={item.id}
          properties={item.properties}
          locked={isLocked(item) || !ownerEditable}
          lockedHint={!ownerEditable && !isLocked(item) ? t("item.ownerLockedHint") : undefined}
          onChanged={refreshAfterAction}
        />
      )}

      {item.itemType === "assembly" && (
        <>
          <div className="flex items-end justify-between gap-2">
            <SectionLabel>{t("item.bom")}</SectionLabel>
            <div className="mb-1 flex items-center gap-3 text-[12.5px]">
              <a className="text-primary hover:underline" href={api.bomCsvUrl(item.id)} download>
                {t("item.downloadCsv")}
              </a>
              <a
                className="text-primary hover:underline"
                href={api.bomAggregatedCsvUrl(item.id)}
                download
              >
                {t("item.downloadCsvAggregated")}
              </a>
            </div>
          </div>
          {bomEntries.length > 0 ? (
            <DndContext
              sensors={dragSensors}
              collisionDetection={closestCenter}
              onDragStart={(e) => setActiveChildId(String(e.active.id))}
              onDragEnd={handleBomDragEnd}
              onDragCancel={() => setActiveChildId(null)}
            >
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead className="w-10">{t("item.colPosition")}</TableHead>
                    <TableHead>{t("common.name")}</TableHead>
                    <TableHead className="text-right">{t("common.quantity")}</TableHead>
                    <TableHead>{t("common.material")}</TableHead>
                    <TableHead>{t("common.manufacturer")}</TableHead>
                    <TableHead>{t("item.colOrderNumber1")}</TableHead>
                    <TableHead>{t("item.colOrderNumber2")}</TableHead>
                    <TableHead className="w-8" />
                  </TableRow>
                </TableHeader>
                <TableBody>
                  <SortableContext items={bomEntries.map((e) => e.item.id)} strategy={verticalListSortingStrategy}>
                    {bomEntries.map(({ item: child, quantity, position }) => (
                      <SortableBomRow
                        key={child.id}
                        parentId={item.id}
                        child={child}
                        quantity={quantity}
                        position={position}
                        nestedEntries={nestedByTopPosition.get(position) ?? []}
                        onSelectChild={onSelectChild}
                        onItemsRefetch={onItemsRefetch}
                        setBomError={setBomError}
                        disabled={!ownerEditable}
                      />
                    ))}
                  </SortableContext>
                </TableBody>
              </Table>
              <DragOverlay>
                {activeChildId
                  ? (() => {
                      const dragged = bomEntries.find((e) => e.item.id === activeChildId)
                      return dragged ? <BomDragPreview item={dragged.item} /> : null
                    })()
                  : null}
              </DragOverlay>
            </DndContext>
          ) : (
            <Hint>{t("item.noPartsInAssembly")}</Hint>
          )}
          <FormError>{bomError}</FormError>
        </>
      )}

      {item.itemType === "folder" && (
        <>
          <SectionLabel>{t("item.files")}</SectionLabel>
          {attachedFiles.length > 0 ? (
            <ul className="flex flex-col gap-1">
              {attachedFiles.map((file) => (
                <li key={file.id} className="flex items-center justify-between gap-2 text-[13px]">
                  <span className="truncate">{file.fileName}</span>
                  {file.filePath ? (
                    <span className="flex shrink-0 items-center gap-2">
                      {previewKindOf(file.fileName) && (
                        <Button size="icon-sm" variant="ghost" onClick={() => setPreviewingFileId(file.id)} aria-label={t("common.preview")}>
                          <Eye className="size-3.5 text-muted-foreground" />
                        </Button>
                      )}
                      <a className="text-primary hover:underline" href={api.fileDownloadUrl(file.id)} download>
                        {t("common.download")}
                      </a>
                    </span>
                  ) : (
                    <span className="shrink-0 text-muted-foreground">{t("item.noFile")}</span>
                  )}
                </li>
              ))}
            </ul>
          ) : (
            <Hint>{t("item.noFilesAdded")}</Hint>
          )}
          {previewingFileId &&
            (() => {
              const file = attachedFiles.find((f) => f.id === previewingFileId)
              const kind = file ? previewKindOf(file.fileName) : null
              if (!file || !kind) return null
              return (
                <PreviewDialog
                  open
                  onOpenChange={(open) => !open && setPreviewingFileId(null)}
                  fileName={file.fileName}
                  url={api.fileDownloadUrl(file.id)}
                  kind={kind}
                />
              )
            })()}
        </>
      )}

      {(item.itemType === "part" || item.itemType === "assembly" || item.itemType === "file") && (
        <>
          <SectionLabel>{t("item.attachments")}</SectionLabel>
          <AttachmentsPanel
            itemId={item.id}
            locked={isLocked(item) || !ownerEditable}
            lockedHint={!ownerEditable && !isLocked(item) ? t("item.ownerLockedHint") : undefined}
            onChanged={refreshAfterAction}
          />
        </>
      )}

      {(item.itemType === "part" || item.itemType === "assembly") && (
        <ItemHistoryPanel itemId={item.id} refreshSignal={historyRefreshSignal} />
      )}
    </div>
  )
}

// Jeden wiersz bezpośredniego dziecka w BOM-ie (przeciągalny przez uchwyt) razem z jego
// zagłębionymi wpisami (tylko do odczytu, bez własnego uchwytu — podążają za rodzicem,
// bo grupowanie po position/path[0] jest liczone na nowo przy każdym renderze).
function SortableBomRow({
  parentId,
  child,
  quantity,
  position,
  nestedEntries,
  onSelectChild,
  onItemsRefetch,
  setBomError,
  disabled = false,
}: {
  parentId: string
  child: Item
  quantity: number
  position: number
  nestedEntries: BomEntry[]
  onSelectChild?: (id: string, parentId: string | null | undefined) => void
  onItemsRefetch: () => void | Promise<void>
  setBomError: (message: string | null) => void
  disabled?: boolean
}) {
  const { t } = useLanguage()
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id: child.id,
    disabled,
  })

  return (
    <Fragment>
      <TableRow
        ref={setNodeRef}
        style={{ transform: CSS.Transform.toString(transform), transition }}
        className={isDragging ? "relative z-10 opacity-40" : undefined}
      >
        <TableCell className="text-muted-foreground">
          <div className="flex items-center gap-1">
            <span
              {...attributes}
              {...listeners}
              className={
                disabled
                  ? "text-muted-foreground/30"
                  : "cursor-grab touch-none text-muted-foreground/50 hover:text-muted-foreground active:cursor-grabbing"
              }
              aria-label={t("item.dragToReorderAria")}
            >
              <GripVertical className="size-3.5" />
            </span>
            <BomPositionCell
              parentId={parentId}
              childId={child.id}
              position={position}
              onChanged={onItemsRefetch}
              onError={setBomError}
              disabled={disabled}
            />
          </div>
        </TableCell>
        <TableCell>{itemDisplayLabel(child)}</TableCell>
        <TableCell className="text-right">
          <BomQuantityCell
            parentId={parentId}
            childId={child.id}
            quantity={quantity}
            onChanged={onItemsRefetch}
            disabled={disabled}
          />
        </TableCell>
        <TableCell>{bomPropertyOrDash(child.properties, "material")}</TableCell>
        <TableCell>{bomPropertyOrDash(child.properties, "manufacturer")}</TableCell>
        <TableCell>{bomPropertyOrDash(child.properties, "orderNumber")}</TableCell>
        <TableCell>{bomPropertyOrDash(child.properties, "orderNumber2")}</TableCell>
        <TableCell>
          {onSelectChild && (
            <Button
              type="button"
              size="icon-xs"
              onClick={() => onSelectChild(child.id, parentId)}
              aria-label={t("item.goToItemAria")}
              title={t("item.goToItemAria")}
            >
              <ArrowUpRight />
            </Button>
          )}
        </TableCell>
      </TableRow>
      {nestedEntries.map((entry) => (
        <TableRow key={entry.itemId + bomPositionLabel(entry.path)} className="text-muted-foreground">
          <TableCell>
            <div className="flex items-center gap-1">
              <span className="size-3.5 shrink-0" />
              <Input
                type="text"
                disabled
                value={bomPositionLabel(entry.path)}
                className="h-7 w-12 text-center text-[13px]"
              />
            </div>
          </TableCell>
          <TableCell style={{ paddingLeft: (entry.depth - 1) * 16 }}>
            {entry.itemNumber !== null
              ? `${entry.itemNumberPrefix ?? ""}${entry.itemNumber} (${entry.fileName})`
              : entry.fileName}
          </TableCell>
          <TableCell className="text-right">{entry.quantity}</TableCell>
          <TableCell>{bomPropertyOrDash(entry.properties, "material")}</TableCell>
          <TableCell>{bomPropertyOrDash(entry.properties, "manufacturer")}</TableCell>
          <TableCell>{bomPropertyOrDash(entry.properties, "orderNumber")}</TableCell>
          <TableCell>{bomPropertyOrDash(entry.properties, "orderNumber2")}</TableCell>
          <TableCell>
            {onSelectChild && (
              // parentId=undefined (nie null!): prawdziwy bezpośredni rodzic tego zagłębionego
              // wpisu to jakieś POD-złożenie, nie to złożenie — z undefined "Usuń ze struktury"
              // po prostu się nie pokaże po przejściu tam, zamiast błędnie odpiąć element od
              // NIEWŁAŚCIWEGO rodzica albo błędnie schować go jako "korzeń".
              <Button
                type="button"
                size="icon-xs"
                onClick={() => onSelectChild(entry.itemId, undefined)}
                aria-label={t("item.goToItemAria")}
                title={t("item.goToItemAria")}
              >
                <ArrowUpRight />
              </Button>
            )}
          </TableCell>
        </TableRow>
      ))}
    </Fragment>
  )
}

// Podgląd przeciąganego wiersza — pokazywany przez DragOverlay poza tabelą (portal),
// więc to zwykły "chip", nie <tr> (wiersz tabeli poza <table> renderowałby się źle).
function BomDragPreview({ item }: { item: Item }) {
  return (
    <div className="flex items-center gap-1.5 rounded-md border bg-popover px-2.5 py-1.5 text-sm shadow-md">
      <GripVertical className="size-3.5 shrink-0 text-muted-foreground" />
      <span className="truncate">{itemDisplayLabel(item)}</span>
    </div>
  )
}

function BomQuantityCell({
  parentId,
  childId,
  quantity,
  onChanged,
  disabled = false,
}: {
  parentId: string
  childId: string
  quantity: number
  onChanged: () => void | Promise<void>
  disabled?: boolean
}) {
  const initial = String(quantity)
  const [value, setValue] = useState(initial)
  useEffect(() => setValue(initial), [initial])

  async function save() {
    const parsed = Number(value)
    if (!Number.isFinite(parsed) || parsed <= 0) {
      setValue(initial)
      return
    }
    if (value === initial) return
    await api.addChild(parentId, childId, parsed)
    await onChanged()
  }

  return (
    <Input
      type="text"
      inputMode="decimal"
      value={value}
      disabled={disabled}
      onChange={(e) => setValue(e.target.value)}
      onBlur={save}
      onKeyDown={(e) => {
        if (e.key === "Enter") (e.target as HTMLInputElement).blur()
      }}
      className="h-7 w-16 text-right text-[13px]"
    />
  )
}

// L.p. wpisywane wprost — musi być dodatnią liczbą całkowitą i unikalną w tym BOM-ie
// (walidację unikalności robi backend, tu tylko odsyłamy błąd wyżej przez onError).
function BomPositionCell({
  parentId,
  childId,
  position,
  onChanged,
  onError,
  disabled = false,
}: {
  parentId: string
  childId: string
  position: number
  onChanged: () => void | Promise<void>
  onError: (message: string | null) => void
  disabled?: boolean
}) {
  const { t } = useLanguage()
  const initial = String(position)
  const [value, setValue] = useState(initial)
  useEffect(() => setValue(initial), [initial])

  async function save() {
    if (value === initial) return
    const parsed = Number(value)
    if (!Number.isInteger(parsed) || parsed <= 0) {
      setValue(initial)
      onError(t("item.positionInvalid"))
      return
    }
    try {
      onError(null)
      await api.setChildPosition(parentId, childId, parsed)
      await onChanged()
    } catch (err) {
      setValue(initial)
      onError(err instanceof Error ? err.message : t("item.positionFailed"))
    }
  }

  return (
    <Input
      type="text"
      inputMode="numeric"
      value={value}
      disabled={disabled}
      onChange={(e) => setValue(e.target.value)}
      onBlur={save}
      onKeyDown={(e) => {
        if (e.key === "Enter") (e.target as HTMLInputElement).blur()
      }}
      className="h-7 w-12 text-center text-[13px]"
    />
  )
}

export { ItemDetailPanel }
