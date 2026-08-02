import { useEffect, useRef, useState } from "react"

import { api } from "@/api/client"
import type { StorageInfo } from "@/api/types"
import { Button } from "@/components/ui/button"
import { ConfirmDialog } from "@/components/ui/confirm-dialog"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { FormError } from "@/components/ui/form-error"
import { Hint } from "@/components/ui/hint"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { SectionLabel } from "@/components/ui/section-label"
import { useLanguage } from "@/i18n/use-language"

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  const units = ["KB", "MB", "GB", "TB"]
  let value = bytes
  let unitIndex = -1
  do {
    value /= 1024
    unitIndex++
  } while (value >= 1024 && unitIndex < units.length - 1)
  return `${value.toFixed(1)} ${units[unitIndex]}`
}

function StorageSettingsView() {
  const { t } = useLanguage()
  const [info, setInfo] = useState<StorageInfo | null>(null)
  const [newPath, setNewPath] = useState("")
  const [error, setError] = useState("")
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [moving, setMoving] = useState(false)
  const [result, setResult] = useState("")

  async function refetch() {
    const data = await api.getStorageInfo()
    setInfo(data)
    setNewPath(data.path)
  }

  useEffect(() => {
    refetch()
  }, [])

  function requestMove() {
    const trimmed = newPath.trim()
    setError("")
    setResult("")
    if (!trimmed) {
      setError(t("storage.pathEmpty"))
      return
    }
    if (info && trimmed === info.path) {
      setError(t("storage.pathUnchanged"))
      return
    }
    setConfirmOpen(true)
  }

  async function performMove(migrateExisting: boolean) {
    setMoving(true)
    setError("")
    try {
      const res = await api.moveStorage(newPath.trim(), migrateExisting)
      setConfirmOpen(false)
      setResult(
        migrateExisting
          ? t("storage.movedWithFiles", { count: res.migratedFiles })
          : t("storage.movedNoFiles")
      )
      await refetch()
    } catch (err) {
      setConfirmOpen(false)
      setError(err instanceof Error ? err.message : t("storage.moveFailed"))
    } finally {
      setMoving(false)
    }
  }

  if (!info) return null

  return (
    <div className="mx-auto max-w-2xl">
      <h2 className="mb-4 text-lg font-semibold tracking-tight">{t("settings.storage")}</h2>

      <div className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
        <SectionLabel>{t("storage.currentLocation")}</SectionLabel>
        <p className="text-sm">{info.path}</p>
        <p className="text-[12.5px] text-muted-foreground">
          {info.fileCount} {info.fileCount === 1 ? t("storage.fileSingular") : t("storage.filePlural")} ·{" "}
          {formatBytes(info.totalSizeBytes)}
        </p>

        <SectionLabel>{t("storage.newLocation")}</SectionLabel>
        <div className="flex flex-col gap-2">
          <Label htmlFor="storage-new-path">{t("storage.pathLabel")}</Label>
          <Input
            id="storage-new-path"
            value={newPath}
            onChange={(e) => setNewPath(e.target.value)}
            placeholder={t("storage.pathPlaceholder")}
          />
          <div>
            <Button onClick={requestMove} disabled={moving}>
              {t("storage.changeLocationButton")}
            </Button>
          </div>
          <FormError>{error}</FormError>
          {result && <Hint>{result}</Hint>}
        </div>

        <SectionLabel>{t("storage.backupLabel")}</SectionLabel>
        <Hint>{t("storage.backupHint")}</Hint>
        <div className="mt-2">
          <a href={api.backupUrl()} download>
            <Button variant="outline">{t("storage.downloadBackupButton")}</Button>
          </a>
        </div>

        <RestoreSection />
      </div>

      <Dialog open={confirmOpen} onOpenChange={(next) => !moving && setConfirmOpen(next)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{t("storage.moveDialogTitle")}</DialogTitle>
            <DialogDescription>
              {t("storage.moveDialogDescription", { from: info.path, to: newPath.trim() })}
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setConfirmOpen(false)} disabled={moving}>
              {t("common.cancel")}
            </Button>
            <Button variant="outline" onClick={() => performMove(false)} disabled={moving}>
              {t("storage.moveDialogSkip")}
            </Button>
            <Button onClick={() => performMove(true)} disabled={moving}>
              {t("storage.moveDialogConfirm")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}

function RestoreSection() {
  const { t } = useLanguage()
  const fileInputRef = useRef<HTMLInputElement>(null)
  const [file, setFile] = useState<File | null>(null)
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [restoring, setRestoring] = useState(false)
  const [error, setError] = useState("")
  const [result, setResult] = useState<{ success: boolean; warnings: string; filesRestored: number } | null>(null)

  async function performRestore() {
    if (!file) return
    setRestoring(true)
    setError("")
    try {
      const res = await api.restoreBackup(file)
      setConfirmOpen(false)
      setResult(res)
    } catch (err) {
      setConfirmOpen(false)
      setError(err instanceof Error ? err.message : t("storage.restoreFailed"))
    } finally {
      setRestoring(false)
    }
  }

  return (
    <>
      <SectionLabel>{t("storage.restoreLabel")}</SectionLabel>
      {result ? (
        <div className="flex flex-col gap-2">
          <Hint>
            {result.success ? t("storage.restoreSuccess") : t("storage.restoreWarnings")}{" "}
            {t("storage.restoreDetail", { count: result.filesRestored })}
          </Hint>
          {result.warnings && (
            <pre className="max-h-32 overflow-auto rounded-md bg-muted p-2 text-[11px] whitespace-pre-wrap text-muted-foreground">
              {result.warnings}
            </pre>
          )}
          <div>
            <Button onClick={() => window.location.reload()}>{t("storage.reloadPageButton")}</Button>
          </div>
        </div>
      ) : (
        <div className="flex flex-col gap-2">
          <Hint>{t("storage.restoreHint")}</Hint>
          <input
            ref={fileInputRef}
            type="file"
            accept=".zip"
            className="hidden"
            onChange={(e) => setFile(e.target.files?.[0] ?? null)}
          />
          <div className="flex items-center gap-2">
            <Button variant="outline" onClick={() => fileInputRef.current?.click()} disabled={restoring}>
              {t("storage.chooseZipButton")}
            </Button>
            {file && <span className="text-[12.5px] text-muted-foreground">{file.name}</span>}
          </div>
          <div>
            <Button
              variant="destructive"
              onClick={() => setConfirmOpen(true)}
              disabled={!file || restoring}
            >
              {t("storage.restoreButton")}
            </Button>
          </div>
          <FormError>{error}</FormError>
        </div>
      )}

      <ConfirmDialog
        open={confirmOpen}
        title={t("storage.restoreConfirmTitle")}
        description={t("storage.restoreConfirmDescription", { name: file?.name ?? "" })}
        confirmLabel={t("storage.restoreConfirmButton")}
        variant="destructive"
        onConfirm={performRestore}
        onCancel={() => setConfirmOpen(false)}
      />
    </>
  )
}

export { StorageSettingsView }
