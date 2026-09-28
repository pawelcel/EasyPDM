import { useCallback, useEffect, useRef, useState } from "react"
import { Trash2, Upload } from "lucide-react"

import { api, ApiError } from "@/api/client"
import type { ProjectAttachment, ProjectAttachmentRole } from "@/api/types"
import { Button } from "@/components/ui/button"
import { ConfirmDialog } from "@/components/ui/confirm-dialog"
import { Hint } from "@/components/ui/hint"
import { SectionLabel } from "@/components/ui/section-label"
import { useLanguage } from "@/i18n/use-language"

// Jedna kategoria załączników projektu jako wyróżnione "okno" — ten sam kształt co sloty
// plików CAD przy elemencie (ramka + nagłówek + przycisk dodawania + lista plików), żeby
// dokumenty zlecenia czytało się tak samo jak dokumentację części.
function AttachmentSlot({
  label,
  attachments,
  emptyHint,
  onUpload,
  onDelete,
  uploading,
}: {
  label: string
  attachments: ProjectAttachment[]
  emptyHint: string
  onUpload: (file: File) => void | Promise<void>
  onDelete: (attachment: ProjectAttachment) => void
  uploading: boolean
}) {
  const { t } = useLanguage()
  const inputRef = useRef<HTMLInputElement>(null)

  return (
    <div className="flex-1 rounded-lg bg-muted/30 p-2 ring-1 ring-foreground/10">
      <div className="mb-1 flex items-center justify-between gap-2">
        <span className="text-[12.5px] font-medium text-muted-foreground uppercase">{label}</span>
        <input
          ref={inputRef}
          type="file"
          className="hidden"
          disabled={uploading}
          onChange={(e) => {
            const file = e.target.files?.[0]
            e.target.value = ""
            if (file) onUpload(file)
          }}
        />
        <Button size="sm" variant="outline" disabled={uploading} onClick={() => inputRef.current?.click()}>
          <Upload className="size-3.5" /> {t("common.add")}
        </Button>
      </div>

      {attachments.length > 0 ? (
        <ul className="flex flex-col gap-1">
          {attachments.map((attachment) => (
            <li key={attachment.id} className="flex items-center justify-between gap-2 text-[13px]">
              <a
                className="truncate text-primary hover:underline"
                href={api.projectAttachmentDownloadUrl(attachment.id)}
                download
              >
                {attachment.fileName}
              </a>
              <Button
                size="icon-xs"
                variant="ghost"
                aria-label={t("common.delete")}
                onClick={() => onDelete(attachment)}
              >
                <Trash2 className="size-3.5 text-muted-foreground" />
              </Button>
            </li>
          ))}
        </ul>
      ) : (
        <Hint>{emptyHint}</Hint>
      )}
    </div>
  )
}

// Dokumenty całego zlecenia: oferta, potwierdzenie przyjęcia zlecenia i wszystko inne, co
// przychodzi "do projektu" (korespondencja, ustalenia, specyfikacje klienta). Niezależne od
// załączników pojedynczych elementów — tamte opisują część, te opisują zlecenie.
function ProjectAttachmentsPanel({ projectId }: { projectId: string }) {
  const { t } = useLanguage()
  const [attachments, setAttachments] = useState<ProjectAttachment[]>([])
  const [uploading, setUploading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [confirmingDelete, setConfirmingDelete] = useState<ProjectAttachment | null>(null)
  const [deletePending, setDeletePending] = useState(false)
  const otherInputRef = useRef<HTMLInputElement>(null)

  const refetch = useCallback(async () => {
    try {
      setAttachments(await api.getProjectAttachments(projectId))
    } catch {
      setAttachments([])
    }
  }, [projectId])

  useEffect(() => {
    refetch()
  }, [refetch])

  async function upload(file: File, role: ProjectAttachmentRole | null) {
    setUploading(true)
    setError(null)
    try {
      const formData = new FormData()
      formData.append("file", file)
      if (role) formData.append("role", role)
      await api.uploadProjectAttachment(projectId, formData)
      await refetch()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t("projectAttachments.uploadFailed"))
    } finally {
      setUploading(false)
    }
  }

  async function confirmDelete() {
    if (!confirmingDelete) return
    setDeletePending(true)
    setError(null)
    try {
      await api.deleteProjectAttachment(confirmingDelete.id)
      setConfirmingDelete(null)
      await refetch()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t("projectAttachments.deleteFailed"))
    } finally {
      setDeletePending(false)
    }
  }

  // Najnowszy na górze — serwer zwraca rosnąco po dacie wysłania, a przy ofercie (która bywa
  // poprawiana i wysyłana ponownie) najbardziej interesuje aktualna wersja.
  const byNewestFirst = (a: ProjectAttachment, b: ProjectAttachment) =>
    b.uploadedAt.localeCompare(a.uploadedAt)
  const offers = attachments.filter((a) => a.role === "oferta").sort(byNewestFirst)
  const orders = attachments.filter((a) => a.role === "zlecenie").sort(byNewestFirst)
  const others = attachments.filter((a) => !a.role).sort(byNewestFirst)

  return (
    <>
      <SectionLabel>{t("projectAttachments.sectionLabel")}</SectionLabel>
      {error && <p className="text-[12.5px] text-destructive">{error}</p>}

      {/* Oferta i potwierdzenie zlecenia obok siebie — to para dokumentów otwierających
          zlecenie (co zaproponowaliśmy / co klient zamówił), więc czyta się je razem. */}
      <div className="flex flex-wrap gap-2">
        <AttachmentSlot
          label={t("projectAttachments.offer")}
          attachments={offers}
          emptyHint={t("projectAttachments.noOffer")}
          onUpload={(file) => upload(file, "oferta")}
          onDelete={setConfirmingDelete}
          uploading={uploading}
        />
        <AttachmentSlot
          label={t("projectAttachments.order")}
          attachments={orders}
          emptyHint={t("projectAttachments.noOrder")}
          onUpload={(file) => upload(file, "zlecenie")}
          onDelete={setConfirmingDelete}
          uploading={uploading}
        />
      </div>

      <div className="mt-2 flex items-center justify-between gap-2">
        <span className="text-[12.5px] font-medium text-muted-foreground uppercase">
          {t("projectAttachments.other")}
        </span>
        <input
          ref={otherInputRef}
          type="file"
          className="hidden"
          disabled={uploading}
          onChange={(e) => {
            const file = e.target.files?.[0]
            e.target.value = ""
            if (file) upload(file, null)
          }}
        />
        <Button size="sm" variant="outline" disabled={uploading} onClick={() => otherInputRef.current?.click()}>
          <Upload className="size-3.5" /> {t("common.add")}
        </Button>
      </div>
      {others.length > 0 ? (
        <ul className="flex flex-col gap-1">
          {others.map((attachment) => (
            <li key={attachment.id} className="flex items-center justify-between gap-2 text-[13px]">
              <a
                className="truncate text-primary hover:underline"
                href={api.projectAttachmentDownloadUrl(attachment.id)}
                download
              >
                {attachment.fileName}
              </a>
              <Button
                size="icon-xs"
                variant="ghost"
                aria-label={t("common.delete")}
                onClick={() => setConfirmingDelete(attachment)}
              >
                <Trash2 className="size-3.5 text-muted-foreground" />
              </Button>
            </li>
          ))}
        </ul>
      ) : (
        <Hint>{t("projectAttachments.noOther")}</Hint>
      )}

      {confirmingDelete && (
        <ConfirmDialog
          open
          title={t("projectAttachments.deleteTitle")}
          description={t("projectAttachments.deleteConfirmDescription", { name: confirmingDelete.fileName })}
          confirmLabel={t("common.delete")}
          variant="destructive"
          onConfirm={confirmDelete}
          onCancel={() => setConfirmingDelete(null)}
          pending={deletePending}
        />
      )}
    </>
  )
}

export { ProjectAttachmentsPanel }
