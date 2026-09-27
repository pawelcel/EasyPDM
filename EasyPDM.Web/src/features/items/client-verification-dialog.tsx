import { useCallback, useEffect, useRef, useState } from "react"
import { Paperclip, Trash2, Upload } from "lucide-react"

import { api, ApiError } from "@/api/client"
import { revisionLabel, type ClientVerification, type ClientVerificationResult } from "@/api/types"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { ConfirmDialog } from "@/components/ui/confirm-dialog"
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { FormError } from "@/components/ui/form-error"
import { Hint } from "@/components/ui/hint"
import { Label } from "@/components/ui/label"
import { SectionLabel } from "@/components/ui/section-label"
import { Textarea } from "@/components/ui/textarea"
import { useAuth } from "@/features/auth/use-auth"
import { useLanguage } from "@/i18n/use-language"

// Okno weryfikacji klienta dla JEDNEJ Części/Złożenia w JEDNYM projekcie: historia
// dotychczasowych rund (wynik + komentarz + dowody) i formularz kolejnego wpisu.
// Świadomie bez edycji istniejących wpisów — to zapis ustaleń z klientem, a nie notatnik:
// pomyłkę prostuje się kolejnym wpisem, a administrator może usunąć ten błędny.
function ClientVerificationDialog({
  projectId,
  itemId,
  itemLabel,
  open,
  onOpenChange,
  onChanged,
}: {
  projectId: string
  itemId: string
  itemLabel: string
  open: boolean
  onOpenChange: (open: boolean) => void
  // Wywoływane po każdej zmianie listy — rodzic odświeża znaczniki w drzewku i panelu.
  onChanged?: () => void | Promise<void>
}) {
  const { t } = useLanguage()
  const { user } = useAuth()
  const isAdmin = user?.role === "admin"
  const fileInputRef = useRef<HTMLInputElement>(null)

  const [entries, setEntries] = useState<ClientVerification[]>([])
  const [loading, setLoading] = useState(true)
  // Domyślnie ŻADEN wynik — brak zaznaczenia to pełnoprawny stan "w trakcie weryfikacji"
  // (rzecz poszła do klienta, czekamy). Wybór wyniku ma być świadomą decyzją, a nie czymś,
  // co użytkownik zastaje już podjęte za siebie.
  const [result, setResult] = useState<ClientVerificationResult | null>(null)
  const [comment, setComment] = useState("")
  const [files, setFiles] = useState<File[]>([])
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState("")
  const [confirmingDeleteId, setConfirmingDeleteId] = useState<string | null>(null)
  const [deletePending, setDeletePending] = useState(false)
  const [deleteError, setDeleteError] = useState<string | null>(null)

  const refetch = useCallback(async () => {
    try {
      setEntries(await api.getClientVerifications(projectId, itemId))
    } catch {
      setEntries([])
    } finally {
      setLoading(false)
    }
  }, [projectId, itemId])

  useEffect(() => {
    if (!open) return
    setLoading(true)
    refetch()
  }, [open, refetch])

  function resetForm() {
    setResult(null)
    setComment("")
    setFiles([])
    setError("")
  }

  async function handleSubmit() {
    setSubmitting(true)
    setError("")
    try {
      const formData = new FormData()
      // Brak wyniku = pole w ogóle nie leci; backend czyta to jako "w trakcie weryfikacji".
      if (result) formData.append("result", result)
      if (comment.trim()) formData.append("comment", comment.trim())
      for (const file of files) formData.append("files", file)

      await api.addClientVerification(projectId, itemId, formData)
      resetForm()
      await refetch()
      await onChanged?.()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t("clientVerification.addFailed"))
    } finally {
      setSubmitting(false)
    }
  }

  async function confirmDelete() {
    if (!confirmingDeleteId) return
    setDeletePending(true)
    setDeleteError(null)
    try {
      await api.deleteClientVerification(confirmingDeleteId)
      setConfirmingDeleteId(null)
      await refetch()
      await onChanged?.()
    } catch (err) {
      setDeleteError(err instanceof ApiError ? err.message : t("clientVerification.deleteFailed"))
    } finally {
      setDeletePending(false)
    }
  }

  return (
    <>
      <Dialog
        open={open}
        onOpenChange={(next) => {
          onOpenChange(next)
          if (!next) resetForm()
        }}
      >
        <DialogContent className="sm:max-w-lg">
          <DialogHeader>
            <DialogTitle>{t("clientVerification.title")}</DialogTitle>
          </DialogHeader>

          <div className="text-[12.5px] text-muted-foreground">{itemLabel}</div>
          <Hint>{t("clientVerification.scopeHint")}</Hint>

          <div className="flex flex-col gap-2">
            <SectionLabel>{t("clientVerification.newEntry")}</SectionLabel>

            <Label htmlFor="client-verification-comment">{t("clientVerification.commentLabel")}</Label>
            <Textarea
              id="client-verification-comment"
              value={comment}
              onChange={(e) => setComment(e.target.value)}
              placeholder={t("clientVerification.commentPlaceholder")}
              rows={2}
            />

            <input
              ref={fileInputRef}
              type="file"
              multiple
              className="hidden"
              onChange={(e) => {
                const picked = Array.from(e.target.files ?? [])
                e.target.value = ""
                if (picked.length > 0) setFiles((prev) => [...prev, ...picked])
              }}
            />
            <div className="flex flex-wrap items-center gap-1.5">
              <Button size="sm" variant="outline" onClick={() => fileInputRef.current?.click()}>
                <Upload className="size-3.5" /> {t("clientVerification.addAttachment")}
              </Button>
              {files.map((file, index) => (
                <Badge key={`${file.name}-${index}`} variant="secondary">
                  {file.name}
                  <button
                    type="button"
                    aria-label={t("common.delete")}
                    onClick={() => setFiles((prev) => prev.filter((_, i) => i !== index))}
                  >
                    <Trash2 className="size-3" />
                  </button>
                </Badge>
              ))}
            </div>

            {/* Wynik na samym dole: wpis wypełnia się od tego, CO się wydarzyło (komentarz,
                dowody), a rozstrzygnięcie jest tego podsumowaniem — nie odwrotnie. Wybór jest
                opcjonalny i działa jak przełącznik: ponowne kliknięcie aktywnego odznacza go,
                więc da się wrócić do "w trakcie weryfikacji" bez zamykania okna. */}
            <Label>{t("clientVerification.resultLabel")}</Label>
            <div className="flex flex-wrap items-center gap-1.5">
              <Button
                size="sm"
                variant={result === "zweryfikowany" ? "default" : "outline"}
                onClick={() => setResult((prev) => (prev === "zweryfikowany" ? null : "zweryfikowany"))}
              >
                {t("clientVerification.resultVerified")}
              </Button>
              <Button
                size="sm"
                variant={result === "do_poprawy" ? "default" : "outline"}
                onClick={() => setResult((prev) => (prev === "do_poprawy" ? null : "do_poprawy"))}
              >
                {t("clientVerification.resultNeedsWork")}
              </Button>
              {result === null && (
                <span className="text-[12.5px] text-muted-foreground">
                  {t("clientVerification.resultPendingHint")}
                </span>
              )}
            </div>

            <FormError>{error}</FormError>
          </div>

          <div className="flex flex-col gap-2">
            <SectionLabel>{t("clientVerification.historyLabel")}</SectionLabel>
            {loading ? (
              <Hint>{t("common.loading")}</Hint>
            ) : entries.length === 0 ? (
              <Hint>{t("clientVerification.noEntries")}</Hint>
            ) : (
              <div className="flex max-h-64 flex-col gap-2 overflow-y-auto">
                {entries.map((entry) => (
                  <div key={entry.id} className="rounded-lg bg-muted/30 p-2 ring-1 ring-foreground/10">
                    <div className="flex items-start justify-between gap-2">
                      <Badge
                        variant={
                          entry.result === "zweryfikowany"
                            ? "default"
                            : entry.result === "do_poprawy"
                              ? "destructive"
                              : "secondary"
                        }
                      >
                        {t(
                          entry.result === "zweryfikowany"
                            ? "clientVerification.resultVerified"
                            : entry.result === "do_poprawy"
                              ? "clientVerification.resultNeedsWork"
                              : "clientVerification.resultPending"
                        )}
                      </Badge>
                      <div className="text-right text-[12px] text-muted-foreground">
                        <div>{new Date(entry.createdAt).toLocaleString("pl-PL")}</div>
                        <div>
                          {entry.createdBy ?? "—"}
                          {entry.revisionNumber !== null &&
                            ` · ${t("item.revisionShort")} ${revisionLabel(entry.revisionNumber)}`}
                        </div>
                      </div>
                    </div>
                    {entry.comment && (
                      <p className="mt-1 text-sm whitespace-pre-wrap">{entry.comment}</p>
                    )}
                    {entry.attachments.length > 0 && (
                      <div className="mt-1 flex flex-wrap gap-1.5">
                        {entry.attachments.map((attachment) => (
                          <a
                            key={attachment.id}
                            href={api.clientVerificationAttachmentUrl(attachment.id)}
                            className="inline-flex items-center gap-1 text-[12.5px] text-primary underline-offset-4 hover:underline"
                          >
                            <Paperclip className="size-3" />
                            {attachment.fileName}
                          </a>
                        ))}
                      </div>
                    )}
                    {isAdmin && (
                      <Button
                        size="icon-xs"
                        variant="ghost"
                        className="mt-1"
                        aria-label={t("clientVerification.deleteEntry")}
                        onClick={() => {
                          setDeleteError(null)
                          setConfirmingDeleteId(entry.id)
                        }}
                      >
                        <Trash2 className="size-3.5 text-muted-foreground" />
                      </Button>
                    )}
                  </div>
                ))}
              </div>
            )}
          </div>

          <DialogFooter>
            <Button variant="outline" onClick={() => onOpenChange(false)}>
              {t("common.close")}
            </Button>
            <Button onClick={handleSubmit} disabled={submitting}>
              {submitting ? t("common.saving") : t("clientVerification.addEntry")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {confirmingDeleteId && (
        <ConfirmDialog
          open
          title={t("clientVerification.deleteEntry")}
          description={t("clientVerification.deleteConfirmDescription")}
          confirmLabel={t("common.delete")}
          variant="destructive"
          onConfirm={confirmDelete}
          onCancel={() => setConfirmingDeleteId(null)}
          pending={deletePending}
          error={deleteError}
        />
      )}
    </>
  )
}

export { ClientVerificationDialog }
