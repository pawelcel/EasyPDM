import { useEffect, useRef, useState } from "react"
import { Trash2, Upload } from "lucide-react"

import { api } from "@/api/client"
import type { Attachment } from "@/api/types"
import { Button } from "@/components/ui/button"
import { Hint } from "@/components/ui/hint"
import { useLanguage } from "@/i18n/use-language"

function formatSize(size: number | null): string {
  if (size === null) return ""
  if (size < 1024) return `${size} B`
  if (size < 1024 * 1024) return `${(size / 1024).toFixed(1)} KB`
  return `${(size / (1024 * 1024)).toFixed(1)} MB`
}

// Załączniki (pliki "z zewnątrz", np. CAD) — osobny mechanizm od struktury drzewa:
// zarządzany wyłącznie tutaj, nie da się ich dodać/usunąć przez lewą stronę.
function AttachmentsPanel({
  itemId,
  locked = false,
  lockedHint,
  onChanged,
}: {
  itemId: string
  locked?: boolean
  lockedHint?: string
  onChanged?: () => void | Promise<void>
}) {
  const { t } = useLanguage()
  const [attachments, setAttachments] = useState<Attachment[]>([])
  const [uploading, setUploading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)

  async function refetch() {
    setAttachments(await api.getAttachments(itemId))
  }

  useEffect(() => {
    refetch()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [itemId])

  async function handleFileSelected(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    e.target.value = ""
    if (!file) return
    setError(null)
    setUploading(true)
    try {
      const formData = new FormData()
      formData.append("file", file)
      await api.uploadAttachment(itemId, formData)
      await refetch()
      await onChanged?.()
    } catch (err) {
      setError(err instanceof Error ? err.message : t("item.uploadAttachmentFailed"))
    } finally {
      setUploading(false)
    }
  }

  async function handleDelete(attachment: Attachment) {
    setError(null)
    try {
      await api.deleteAttachment(attachment.id)
      await refetch()
      await onChanged?.()
    } catch (err) {
      setError(err instanceof Error ? err.message : t("item.deleteAttachmentFailed"))
    }
  }

  return (
    <div>
      {locked && <Hint>{lockedHint ?? t("item.attachmentsLockedHint")}</Hint>}
      {error && <p className="text-[12.5px] text-destructive">{error}</p>}

      {attachments.length > 0 ? (
        <ul className="flex flex-col gap-1">
          {attachments.map((attachment) => (
            <li
              key={attachment.id}
              className="flex items-center justify-between gap-2 text-[13px]"
            >
              <a
                className="truncate text-primary hover:underline"
                href={api.attachmentDownloadUrl(attachment.id)}
                download
              >
                {attachment.fileName}
              </a>
              <div className="flex shrink-0 items-center gap-2">
                <span className="text-muted-foreground">{formatSize(attachment.fileSize)}</span>
                <Button
                  size="icon-sm"
                  variant="ghost"
                  onClick={() => handleDelete(attachment)}
                  disabled={locked}
                  aria-label={t("common.deleteNamed", { name: attachment.fileName })}
                >
                  <Trash2 className="size-3.5 text-muted-foreground" />
                </Button>
              </div>
            </li>
          ))}
        </ul>
      ) : (
        <Hint>{t("item.noAttachments")}</Hint>
      )}

      <div className="mt-2">
        <input
          ref={fileInputRef}
          type="file"
          className="hidden"
          onChange={handleFileSelected}
          disabled={locked || uploading}
        />
        <Button
          size="sm"
          variant="outline"
          disabled={locked || uploading}
          onClick={() => fileInputRef.current?.click()}
        >
          <Upload className="size-3.5" /> {uploading ? t("common.uploading") : t("item.addAttachment")}
        </Button>
      </div>
    </div>
  )
}

export { AttachmentsPanel }
