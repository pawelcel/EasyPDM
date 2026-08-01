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
import { GripVertical, Upload } from "lucide-react"

import { api } from "@/api/client"
import { bomPositionLabel, isLocked, itemDisplayLabel, itemTypeLabel, type BomEntry, type Item } from "@/api/types"
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
import { PartPropertyForm } from "@/features/items/part-property-form"
import { PropertyEditor } from "@/features/items/property-editor"
import { StatusControl } from "@/features/items/status-control"

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
}: {
  item: Item
  projectName?: string
  showHeader?: boolean
  childEntries?: { item: Item; quantity: number; position: number }[]
  onSelectChild?: (id: string) => void
  onItemsRefetch: () => void | Promise<void>
  onTagsRefetch: () => void | Promise<void>
  onRemoveFromStructure?: () => void | Promise<void>
  onDeleteCompletely?: () => void | Promise<void>
}) {
  const attachedFiles = childEntries.filter((c) => c.item.itemType === "file").map((c) => c.item)
  const bomEntries = childEntries.filter(
    (c) => c.item.itemType === "part" || c.item.itemType === "assembly"
  )
  const modified = item.modifiedAt ? new Date(item.modifiedAt).toLocaleString("pl-PL") : "—"
  const typeLabel = itemTypeLabel(item)

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

  // Wystarczy raz przy wejściu w ten element (ItemDetailPanel ma key={selectedItem.id}
  // u wywołującego, więc zmiana zaznaczenia i tak remontuje panel) — poza tym dociągamy
  // ponownie tylko po zmianie L.p. bezpośrednich dzieci (drag&drop albo ręczny wpis),
  // bo grupowanie zagłębionych wpisów po path[0] opiera się na aktualnych "position".
  useEffect(() => {
    if (item.itemType !== "assembly") return
    refetchNestedBom()
  }, [item.itemType, refetchNestedBom])

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
      refetchNestedBom()
    } catch (err) {
      setBomError(err instanceof Error ? err.message : "Nie udało się zmienić kolejności.")
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
      {(onRemoveFromStructure || onDeleteCompletely) && (
        <div className="mb-3 flex gap-1.5 border-b pb-3">
          {onRemoveFromStructure && (
            <Button size="sm" variant="outline" onClick={onRemoveFromStructure}>
              Usuń ze struktury
            </Button>
          )}
          {onDeleteCompletely && (
            <Button size="sm" variant="destructive" onClick={onDeleteCompletely}>
              Usuń całkowicie
            </Button>
          )}
        </div>
      )}

      {showHeader && (
        <>
          <div className="text-[15px] font-semibold">{itemDisplayLabel(item)}</div>
          <div className="text-[12.5px] text-muted-foreground">
            {typeLabel} · zmodyfikowano {modified}
            {projectName ? ` · ${projectName}` : ""}
          </div>
        </>
      )}

      {(item.itemType === "part" || item.itemType === "assembly") && (
        <div className="mt-2">
          <StatusControl item={item} onChanged={onItemsRefetch} />
        </div>
      )}

      {item.itemType === "file" &&
        (item.filePath ? (
          <a
            className="mt-3 inline-block text-[13px] text-primary hover:underline"
            href={api.fileDownloadUrl(item.id)}
            download
          >
            ⬇ Pobierz plik
          </a>
        ) : (
          <Hint>Plik nie został jeszcze przesłany.</Hint>
        ))}

      {item.itemType === "folder" && (
        <div className="mt-3">
          <AddNodeDialog
            trigger={
              <Button size="sm" variant="outline">
                <Upload className="size-3.5" /> Wgraj plik
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

      <SectionLabel>Tagi</SectionLabel>
      <div className="flex flex-wrap gap-1.5">
        {item.tags.length > 0 ? (
          item.tags.map((tag) => (
            <TagPill key={tag} name={tag} onRemove={() => handleRemoveTag(tag)} />
          ))
        ) : (
          <Hint>brak tagów</Hint>
        )}
      </div>
      <AddTagRow onAdd={handleAddTag} />

      <SectionLabel>Właściwości</SectionLabel>
      {item.itemType === "part" ? (
        <PartPropertyForm item={item} onChanged={onItemsRefetch} />
      ) : (
        <PropertyEditor
          itemId={item.id}
          properties={item.properties}
          locked={isLocked(item)}
          onChanged={onItemsRefetch}
        />
      )}

      {item.itemType === "assembly" && (
        <>
          <div className="flex items-end justify-between gap-2">
            <SectionLabel>BOM</SectionLabel>
            <div className="mb-1 flex items-center gap-3 text-[12.5px]">
              <a className="text-primary hover:underline" href={api.bomCsvUrl(item.id)} download>
                ⬇ Pobierz CSV
              </a>
              <a
                className="text-primary hover:underline"
                href={api.bomAggregatedCsvUrl(item.id)}
                download
              >
                ⬇ Pobierz CSV (zsumowany)
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
                    <TableHead className="w-10">L.p.</TableHead>
                    <TableHead>Nazwa</TableHead>
                    <TableHead className="text-right">Ilość</TableHead>
                    <TableHead>Materiał</TableHead>
                    <TableHead>Producent</TableHead>
                    <TableHead>Numer zamówieniowy 1</TableHead>
                    <TableHead>Numer zamówieniowy 2</TableHead>
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
                        refetchNestedBom={refetchNestedBom}
                        setBomError={setBomError}
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
            <Hint>brak części w złożeniu</Hint>
          )}
          <FormError>{bomError}</FormError>
        </>
      )}

      {item.itemType === "folder" && (
        <>
          <SectionLabel>Pliki</SectionLabel>
          {attachedFiles.length > 0 ? (
            <ul className="flex flex-col gap-1">
              {attachedFiles.map((file) => (
                <li key={file.id} className="flex items-center justify-between gap-2 text-[13px]">
                  <span className="truncate">{file.fileName}</span>
                  {file.filePath ? (
                    <a
                      className="shrink-0 text-primary hover:underline"
                      href={api.fileDownloadUrl(file.id)}
                      download
                    >
                      ⬇ Pobierz
                    </a>
                  ) : (
                    <span className="shrink-0 text-muted-foreground">brak pliku</span>
                  )}
                </li>
              ))}
            </ul>
          ) : (
            <Hint>brak dodanych plików</Hint>
          )}
        </>
      )}

      {(item.itemType === "part" || item.itemType === "assembly" || item.itemType === "file") && (
        <>
          <SectionLabel>Załączniki</SectionLabel>
          <AttachmentsPanel itemId={item.id} locked={isLocked(item)} onChanged={onItemsRefetch} />
        </>
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
  refetchNestedBom,
  setBomError,
}: {
  parentId: string
  child: Item
  quantity: number
  position: number
  nestedEntries: BomEntry[]
  onSelectChild?: (id: string) => void
  onItemsRefetch: () => void | Promise<void>
  refetchNestedBom: () => void
  setBomError: (message: string | null) => void
}) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id: child.id,
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
              className="cursor-grab touch-none text-muted-foreground/50 hover:text-muted-foreground active:cursor-grabbing"
              aria-label="Przeciągnij, żeby zmienić kolejność"
            >
              <GripVertical className="size-3.5" />
            </span>
            <BomPositionCell
              parentId={parentId}
              childId={child.id}
              position={position}
              onChanged={async () => {
                await onItemsRefetch()
                refetchNestedBom()
              }}
              onError={setBomError}
            />
          </div>
        </TableCell>
        <TableCell>
          {onSelectChild ? (
            <button
              type="button"
              onClick={() => onSelectChild(child.id)}
              className="text-left text-primary hover:underline"
            >
              {itemDisplayLabel(child)}
            </button>
          ) : (
            itemDisplayLabel(child)
          )}
        </TableCell>
        <TableCell className="text-right">
          <BomQuantityCell
            parentId={parentId}
            childId={child.id}
            quantity={quantity}
            onChanged={onItemsRefetch}
          />
        </TableCell>
        <TableCell>{bomPropertyOrDash(child.properties, "material")}</TableCell>
        <TableCell>{bomPropertyOrDash(child.properties, "manufacturer")}</TableCell>
        <TableCell>{bomPropertyOrDash(child.properties, "orderNumber")}</TableCell>
        <TableCell>{bomPropertyOrDash(child.properties, "orderNumber2")}</TableCell>
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
            {entry.itemNumber !== null ? `${entry.itemNumber} (${entry.fileName})` : entry.fileName}
          </TableCell>
          <TableCell className="text-right">{entry.quantity}</TableCell>
          <TableCell>{bomPropertyOrDash(entry.properties, "material")}</TableCell>
          <TableCell>{bomPropertyOrDash(entry.properties, "manufacturer")}</TableCell>
          <TableCell>{bomPropertyOrDash(entry.properties, "orderNumber")}</TableCell>
          <TableCell>{bomPropertyOrDash(entry.properties, "orderNumber2")}</TableCell>
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
}: {
  parentId: string
  childId: string
  quantity: number
  onChanged: () => void | Promise<void>
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
}: {
  parentId: string
  childId: string
  position: number
  onChanged: () => void | Promise<void>
  onError: (message: string | null) => void
}) {
  const initial = String(position)
  const [value, setValue] = useState(initial)
  useEffect(() => setValue(initial), [initial])

  async function save() {
    if (value === initial) return
    const parsed = Number(value)
    if (!Number.isInteger(parsed) || parsed <= 0) {
      setValue(initial)
      onError("L.p. musi być liczbą całkowitą większą od zera.")
      return
    }
    try {
      onError(null)
      await api.setChildPosition(parentId, childId, parsed)
      await onChanged()
    } catch (err) {
      setValue(initial)
      onError(err instanceof Error ? err.message : "Nie udało się zmienić L.p.")
    }
  }

  return (
    <Input
      type="text"
      inputMode="numeric"
      value={value}
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
