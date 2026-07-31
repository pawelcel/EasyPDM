import { Upload } from "lucide-react"

import { api } from "@/api/client"
import { itemTypeLabel, type Item } from "@/api/types"
import { Button } from "@/components/ui/button"
import { Hint } from "@/components/ui/hint"
import { SectionLabel } from "@/components/ui/section-label"
import { TagPill } from "@/components/ui/tag-pill"
import { AddNodeDialog } from "@/features/items/add-node-dialog"
import { AddTagRow } from "@/features/tags/add-tag-row"
import { PartPropertyForm } from "@/features/items/part-property-form"
import { PropertyEditor } from "@/features/items/property-editor"

function ItemDetailPanel({
  item,
  projectName,
  showHeader = true,
  childItems = [],
  onItemsRefetch,
  onTagsRefetch,
  onRemoveFromStructure,
  onDeleteCompletely,
}: {
  item: Item
  projectName?: string
  showHeader?: boolean
  childItems?: Item[]
  onItemsRefetch: () => void | Promise<void>
  onTagsRefetch: () => void | Promise<void>
  onRemoveFromStructure?: () => void | Promise<void>
  onDeleteCompletely?: () => void | Promise<void>
}) {
  const attachedFiles = childItems.filter((c) => c.itemType === "file")
  const modified = item.modifiedAt ? new Date(item.modifiedAt).toLocaleString("pl-PL") : "—"
  const typeLabel = itemTypeLabel(item)

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
          <div className="text-[15px] font-semibold">
            {item.fileName}
            {item.itemNumber !== null && (
              <span className="ml-1.5 text-[12.5px] font-normal text-muted-foreground">
                #{item.itemNumber}
              </span>
            )}
          </div>
          <div className="text-[12.5px] text-muted-foreground">
            {typeLabel} · zmodyfikowano {modified}
            {projectName ? ` · ${projectName}` : ""}
          </div>
        </>
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

      {item.itemType !== "folder" && (
        <div className="mt-3">
          <AddNodeDialog
            trigger={
              <Button size="sm" variant="outline">
                <Upload className="size-3.5" /> Wgraj plik
              </Button>
            }
            projectId={item.projectId}
            parentId={item.id}
            existingItems={[]}
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
        <PropertyEditor itemId={item.id} properties={item.properties} onChanged={onItemsRefetch} />
      )}

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
    </div>
  )
}

export { ItemDetailPanel }
