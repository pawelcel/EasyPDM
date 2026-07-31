import { useEffect, useState } from "react"
import { GripVertical, Upload } from "lucide-react"

import { api } from "@/api/client"
import { isLocked, itemDisplayLabel, itemTypeLabel, type Item } from "@/api/types"
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
function bomPropertyOrDash(item: Item, key: string): string {
  const value = item.properties[key]
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
  const [draggedChildId, setDraggedChildId] = useState<string | null>(null)

  async function handleBomDrop(targetChildId: string) {
    const draggedId = draggedChildId
    setDraggedChildId(null)
    if (!draggedId || draggedId === targetChildId) return

    const ids = bomEntries.map((e) => e.item.id)
    const fromIndex = ids.indexOf(draggedId)
    const toIndex = ids.indexOf(targetChildId)
    if (fromIndex === -1 || toIndex === -1) return

    const reordered = [...ids]
    reordered.splice(fromIndex, 1)
    reordered.splice(toIndex, 0, draggedId)

    setBomError(null)
    try {
      await api.reorderChildren(item.id, reordered)
      await onItemsRefetch()
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
          <SectionLabel>BOM</SectionLabel>
          {bomEntries.length > 0 ? (
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
                {bomEntries.map(({ item: child, quantity, position }) => (
                  <TableRow
                    key={child.id}
                    onDragOver={(e) => e.preventDefault()}
                    onDrop={() => handleBomDrop(child.id)}
                    className={draggedChildId === child.id ? "opacity-50" : undefined}
                  >
                    <TableCell className="text-muted-foreground">
                      <div className="flex items-center gap-1">
                        <span
                          draggable
                          onDragStart={() => setDraggedChildId(child.id)}
                          onDragEnd={() => setDraggedChildId(null)}
                          className="cursor-grab text-muted-foreground/50 hover:text-muted-foreground"
                          aria-label="Przeciągnij, żeby zmienić kolejność"
                        >
                          <GripVertical className="size-3.5" />
                        </span>
                        <BomPositionCell
                          parentId={item.id}
                          childId={child.id}
                          position={position}
                          onChanged={onItemsRefetch}
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
                        parentId={item.id}
                        childId={child.id}
                        quantity={quantity}
                        onChanged={onItemsRefetch}
                      />
                    </TableCell>
                    <TableCell>{bomPropertyOrDash(child, "material")}</TableCell>
                    <TableCell>{bomPropertyOrDash(child, "manufacturer")}</TableCell>
                    <TableCell>{bomPropertyOrDash(child, "orderNumber")}</TableCell>
                    <TableCell>{bomPropertyOrDash(child, "orderNumber2")}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
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
