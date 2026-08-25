import { useEffect, useRef, useState } from "react"
import { Eye, Trash2, Upload } from "lucide-react"

import { api } from "@/api/client"
import type { Attachment } from "@/api/types"
import { Button } from "@/components/ui/button"
import { Hint } from "@/components/ui/hint"
import { PreviewDialog } from "@/features/preview/preview-dialog"
import { useLanguage } from "@/i18n/use-language"
import { previewKindOf } from "@/lib/file-preview"

function formatSize(size: number | null): string {
  if (size === null) return ""
  if (size < 1024) return `${size} B`
  if (size < 1024 * 1024) return `${(size / 1024).toFixed(1)} KB`
  return `${(size / (1024 * 1024)).toFixed(1)} MB`
}

function formatUploadedAt(uploadedAt: string | null): string {
  if (!uploadedAt) return ""
  return new Date(uploadedAt).toLocaleString("pl-PL")
}

// Miejsce "PDF"/"STEP" — jeden załącznik na rolę, widoczny osobno OD załączników
// ogólnych, żeby było od razu jasne, KTÓRY plik zasila stały podgląd 2D/3D w nagłówku
// panelu (ItemPreviewBox), zamiast szukać go wśród zwykłych załączników.
function RoleSlot({
  role,
  attachment,
  onUpload,
  onDelete,
  onPreview,
  disabled,
}: {
  role: "pdf" | "step"
  attachment: Attachment | undefined
  onUpload: (file: File) => void
  onDelete: () => void
  onPreview: () => void
  disabled: boolean
}) {
  const { t } = useLanguage()
  const inputRef = useRef<HTMLInputElement>(null)
  // "3D" zamiast "STEP" — slot przyjmuje STEP/IGES/STL, nie tylko dosłownie format STEP.
  // Krótka etykieta na przycisku, pełna lista formatów obok w nagłówku (za mało miejsca
  // na przycisku, żeby zmieściła się cała).
  const label = role === "pdf" ? "PDF" : "3D"
  const headerLabel = role === "pdf" ? "PDF" : "3D (STEP/IGES/STL)"

  return (
    <div className="flex-1 rounded-lg bg-muted/30 p-2 ring-1 ring-foreground/10">
      <div className="mb-1 flex items-center justify-between">
        <span className="text-[11px] font-medium text-muted-foreground uppercase">{headerLabel}</span>
        <input
          ref={inputRef}
          type="file"
          accept={role === "pdf" ? ".pdf" : ".step,.stp,.iges,.igs,.stl"}
          className="hidden"
          onChange={(e) => {
            const file = e.target.files?.[0]
            e.target.value = ""
            if (file) onUpload(file)
          }}
          disabled={disabled}
        />
        <Button
          size="sm"
          variant="outline"
          disabled={disabled}
          onClick={() => inputRef.current?.click()}
        >
          <Upload className="size-3.5" /> {label}
        </Button>
      </div>

      {attachment ? (
        <div className="flex items-center justify-between gap-1 text-[13px]">
          <a
            className="truncate text-primary hover:underline"
            href={api.attachmentDownloadUrl(attachment.id)}
            download
          >
            {attachment.fileName}
          </a>
          <div className="flex shrink-0 items-center gap-1">
            <Button size="icon-xs" variant="ghost" onClick={onPreview} aria-label={t("common.preview")}>
              <Eye className="size-3 text-muted-foreground" />
            </Button>
            <Button
              size="icon-xs"
              variant="ghost"
              onClick={onDelete}
              disabled={disabled}
              aria-label={t("common.deleteNamed", { name: attachment.fileName })}
            >
              <Trash2 className="size-3 text-muted-foreground" />
            </Button>
          </div>
        </div>
      ) : (
        <Hint>{t("item.noFile")}</Hint>
      )}
    </div>
  )
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
  const [previewing, setPreviewing] = useState<Attachment | null>(null)
  const genericInputRef = useRef<HTMLInputElement>(null)
  const cadInputRef = useRef<HTMLInputElement>(null)

  async function refetch() {
    setAttachments(await api.getAttachments(itemId))
  }

  useEffect(() => {
    refetch()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [itemId])

  async function uploadFile(file: File, role: "pdf" | "step" | "cad" | null) {
    setError(null)
    setUploading(true)
    try {
      const formData = new FormData()
      formData.append("file", file)
      if (role) formData.append("role", role)
      await api.uploadAttachment(itemId, formData)
      await refetch()
      await onChanged?.()
    } catch (err) {
      setError(err instanceof Error ? err.message : t("item.uploadAttachmentFailed"))
    } finally {
      setUploading(false)
    }
  }

  function handleGenericFileSelected(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    e.target.value = ""
    if (file) uploadFile(file, null)
  }

  // W odróżnieniu od PDF/3D (jeden załącznik na rolę, nowy zastępuje poprzedni) -- "cad"
  // się KUMULUJE: makro wysyła każdą rewizję pod unikalną nazwą, więc kolejne wysyłki nie
  // powinny kasować poprzednich (to zachowana historia rewizji, widoczna jako lista).
  function handleCadFileSelected(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    e.target.value = ""
    if (file) uploadFile(file, "cad")
  }

  // Na rolę przypada najwyżej jeden załącznik — nowy zastępuje poprzedni (usuwamy stary
  // PRZED wysłaniem nowego), żeby nigdy nie było niejednoznaczności, który plik jest "tym"
  // rysunkiem/modelem.
  async function handleRoleUpload(role: "pdf" | "step", file: File) {
    const existing = attachments.find((a) => a.role === role)
    if (existing) {
      try {
        await api.deleteAttachment(existing.id)
      } catch (err) {
        setError(err instanceof Error ? err.message : t("item.deleteAttachmentFailed"))
        return
      }
    }
    await uploadFile(file, role)
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

  const pdfAttachment = attachments.find((a) => a.role === "pdf")
  const stepAttachment = attachments.find((a) => a.role === "step")
  const cadAttachments = attachments.filter((a) => a.role === "cad")
  const genericAttachments = attachments.filter((a) => !a.role)

  return (
    <div>
      {locked && <Hint>{lockedHint ?? t("item.attachmentsLockedHint")}</Hint>}
      {error && <p className="text-[12.5px] text-destructive">{error}</p>}

      <div className="mb-3 rounded-lg bg-muted/30 p-2 ring-1 ring-foreground/10">
        <div className="mb-1 flex items-center justify-between">
          <span className="text-[11px] font-medium text-muted-foreground uppercase">
            {t("item.cadAttachments")}
          </span>
          <input
            ref={cadInputRef}
            type="file"
            className="hidden"
            onChange={handleCadFileSelected}
            disabled={locked || uploading}
          />
          <Button
            size="sm"
            variant="outline"
            disabled={locked || uploading}
            onClick={() => cadInputRef.current?.click()}
          >
            <Upload className="size-3.5" /> {t("common.add")}
          </Button>
        </div>

        {cadAttachments.length > 0 ? (
          <ul className="flex flex-col gap-1">
            {cadAttachments.map((attachment) => (
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
                <div className="flex shrink-0 items-center gap-1">
                  <span className="text-muted-foreground">
                    {formatUploadedAt(attachment.uploadedAt)}
                    {attachment.uploadedAt && attachment.fileSize !== null && " · "}
                    {formatSize(attachment.fileSize)}
                  </span>
                  {previewKindOf(attachment.fileName) && (
                    <Button
                      size="icon-xs"
                      variant="ghost"
                      onClick={() => setPreviewing(attachment)}
                      aria-label={t("common.preview")}
                    >
                      <Eye className="size-3 text-muted-foreground" />
                    </Button>
                  )}
                  <Button
                    size="icon-xs"
                    variant="ghost"
                    onClick={() => handleDelete(attachment)}
                    disabled={locked}
                    aria-label={t("common.deleteNamed", { name: attachment.fileName })}
                  >
                    <Trash2 className="size-3 text-muted-foreground" />
                  </Button>
                </div>
              </li>
            ))}
          </ul>
        ) : (
          <Hint>{t("item.noFile")}</Hint>
        )}
      </div>

      <div className="mb-3 flex gap-2">
        <RoleSlot
          role="pdf"
          attachment={pdfAttachment}
          onUpload={(file) => handleRoleUpload("pdf", file)}
          onDelete={() => pdfAttachment && handleDelete(pdfAttachment)}
          onPreview={() => pdfAttachment && setPreviewing(pdfAttachment)}
          disabled={locked || uploading}
        />
        <RoleSlot
          role="step"
          attachment={stepAttachment}
          onUpload={(file) => handleRoleUpload("step", file)}
          onDelete={() => stepAttachment && handleDelete(stepAttachment)}
          onPreview={() => stepAttachment && setPreviewing(stepAttachment)}
          disabled={locked || uploading}
        />
      </div>

      {genericAttachments.length > 0 ? (
        <ul className="flex flex-col gap-1">
          {genericAttachments.map((attachment) => (
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
                <span className="text-muted-foreground">
                  {formatUploadedAt(attachment.uploadedAt)}
                  {attachment.uploadedAt && attachment.fileSize !== null && " · "}
                  {formatSize(attachment.fileSize)}
                </span>
                {previewKindOf(attachment.fileName) && (
                  <Button
                    size="icon-sm"
                    variant="ghost"
                    onClick={() => setPreviewing(attachment)}
                    aria-label={t("common.preview")}
                  >
                    <Eye className="size-3.5 text-muted-foreground" />
                  </Button>
                )}
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
          ref={genericInputRef}
          type="file"
          className="hidden"
          onChange={handleGenericFileSelected}
          disabled={locked || uploading}
        />
        <Button
          size="sm"
          variant="outline"
          disabled={locked || uploading}
          onClick={() => genericInputRef.current?.click()}
        >
          <Upload className="size-3.5" /> {uploading ? t("common.uploading") : t("item.addAttachment")}
        </Button>
      </div>

      {previewing && (
        <PreviewDialog
          open
          onOpenChange={(open) => !open && setPreviewing(null)}
          fileName={previewing.fileName}
          url={api.attachmentDownloadUrl(previewing.id)}
          kind={previewKindOf(previewing.fileName)!}
        />
      )}
    </div>
  )
}

export { AttachmentsPanel }
