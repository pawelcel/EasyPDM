import { Loader2 } from "lucide-react"
import { useEffect, useRef, useState } from "react"

import { api } from "@/api/client"
import type { BackupFrequency, BackupSchedule, StorageInfo } from "@/api/types"
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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { SectionLabel } from "@/components/ui/section-label"
import type { TranslationKey } from "@/i18n/translations"
import { useLanguage } from "@/i18n/use-language"

const FREQUENCY_OPTIONS: { value: BackupFrequency; labelKey: TranslationKey }[] = [
  { value: "daily", labelKey: "backup.frequency.daily" },
  { value: "weekly", labelKey: "backup.frequency.weekly" },
  { value: "monthly", labelKey: "backup.frequency.monthly" },
]

const DAY_OF_WEEK_OPTIONS: { value: number; labelKey: TranslationKey }[] = [
  { value: 1, labelKey: "backup.dayOfWeek.monday" },
  { value: 2, labelKey: "backup.dayOfWeek.tuesday" },
  { value: 3, labelKey: "backup.dayOfWeek.wednesday" },
  { value: 4, labelKey: "backup.dayOfWeek.thursday" },
  { value: 5, labelKey: "backup.dayOfWeek.friday" },
  { value: 6, labelKey: "backup.dayOfWeek.saturday" },
  { value: 0, labelKey: "backup.dayOfWeek.sunday" },
]

function pad2(n: number): string {
  return n.toString().padStart(2, "0")
}

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
  const [loadError, setLoadError] = useState(false)

  async function refetch() {
    const data = await api.getStorageInfo()
    setInfo(data)
    setNewPath(data.path)
  }

  useEffect(() => {
    refetch().catch(() => setLoadError(true))
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

  if (loadError) {
    return (
      <div className="mx-auto max-w-2xl">
        <h2 className="mb-4 text-lg font-semibold tracking-tight">{t("settings.storage")}</h2>
        <Hint>{t("database.loadError")}</Hint>
      </div>
    )
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

        <AutoBackupSection />

        <DangerZoneSection />
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

function AutoBackupSection() {
  const { t } = useLanguage()
  const [schedule, setSchedule] = useState<BackupSchedule | null>(null)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState("")
  const [saved, setSaved] = useState(false)
  const [loadError, setLoadError] = useState(false)

  useEffect(() => {
    api.getBackupSchedule().then(setSchedule).catch(() => setLoadError(true))
  }, [])

  async function save(next: BackupSchedule) {
    // Zmiana pokazuje się w formularzu OD RAZU (optymistycznie), ale jeśli zapis się nie
    // powiedzie, wracamy do poprzedniego stanu — inaczej pole pokazywałoby wartość, której
    // serwer nigdy nie zaakceptował, i mogłaby "przy okazji" zapisać się dopiero przy
    // kolejnej, niepowiązanej zmianie.
    const previous = schedule
    setSchedule(next)
    setSaving(true)
    setError("")
    setSaved(false)
    try {
      const result = await api.updateBackupSchedule(next)
      setSchedule(result)
      setSaved(true)
    } catch (err) {
      setSchedule(previous)
      setError(err instanceof Error ? err.message : t("backup.saveFailed"))
    } finally {
      setSaving(false)
    }
  }

  if (loadError) return <Hint>{t("database.loadError")}</Hint>

  if (!schedule) return null

  const time = `${pad2(schedule.hour)}:${pad2(schedule.minute)}`

  function updateTime(value: string) {
    const [h, m] = value.split(":").map(Number)
    if (Number.isNaN(h) || Number.isNaN(m)) return
    save({ ...schedule!, hour: h, minute: m })
  }

  return (
    <>
      <SectionLabel>{t("backup.autoLabel")}</SectionLabel>
      <div className="flex flex-col gap-3">
        <label className="flex cursor-pointer items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={schedule.enabled}
            onChange={(e) => save({ ...schedule, enabled: e.target.checked })}
            disabled={saving}
            className="size-3.5 shrink-0 accent-primary"
          />
          {t("backup.enableToggle")}
        </label>

        <div className="flex flex-wrap items-end gap-3">
          <div className="flex flex-col gap-1">
            <Label>{t("backup.frequencyLabel")}</Label>
            <Select
              value={schedule.frequency}
              onValueChange={(v) => {
                const frequency = v as BackupFrequency
                save({
                  ...schedule,
                  frequency,
                  dayOfWeek: frequency === "weekly" ? (schedule.dayOfWeek ?? 1) : schedule.dayOfWeek,
                  dayOfMonth: frequency === "monthly" ? (schedule.dayOfMonth ?? 1) : schedule.dayOfMonth,
                })
              }}
            >
              <SelectTrigger disabled={saving} className="min-w-36">
                <SelectValue>
                  {(v: string) =>
                    t(FREQUENCY_OPTIONS.find((o) => o.value === v)!.labelKey)
                  }
                </SelectValue>
              </SelectTrigger>
              <SelectContent>
                {FREQUENCY_OPTIONS.map((o) => (
                  <SelectItem key={o.value} value={o.value}>
                    {t(o.labelKey)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          {schedule.frequency === "weekly" && (
            <div className="flex flex-col gap-1">
              <Label>{t("backup.dayOfWeekLabel")}</Label>
              <Select
                value={String(schedule.dayOfWeek ?? 1)}
                onValueChange={(v) => save({ ...schedule, dayOfWeek: Number(v) })}
              >
                <SelectTrigger disabled={saving} className="min-w-32">
                  <SelectValue>
                    {(v: string) =>
                      t(DAY_OF_WEEK_OPTIONS.find((o) => o.value === Number(v))!.labelKey)
                    }
                  </SelectValue>
                </SelectTrigger>
                <SelectContent>
                  {DAY_OF_WEEK_OPTIONS.map((o) => (
                    <SelectItem key={o.value} value={String(o.value)}>
                      {t(o.labelKey)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          )}

          {schedule.frequency === "monthly" && (
            <div className="flex flex-col gap-1">
              <Label htmlFor="backup-day-of-month">{t("backup.dayOfMonthLabel")}</Label>
              <Input
                id="backup-day-of-month"
                type="number"
                min={1}
                max={31}
                value={schedule.dayOfMonth ?? 1}
                disabled={saving}
                className="w-20"
                onChange={(e) => {
                  const day = Number(e.target.value)
                  if (day >= 1 && day <= 31) save({ ...schedule, dayOfMonth: day })
                }}
              />
            </div>
          )}

          <div className="flex flex-col gap-1">
            <Label htmlFor="backup-time">{t("backup.timeLabel")}</Label>
            <Input
              id="backup-time"
              type="time"
              value={time}
              disabled={saving}
              className="w-28"
              onChange={(e) => updateTime(e.target.value)}
            />
          </div>

          <div className="flex flex-col gap-1">
            <Label htmlFor="backup-retention">{t("backup.retentionLabel")}</Label>
            <Input
              id="backup-retention"
              type="number"
              min={1}
              max={365}
              value={schedule.retentionCount}
              disabled={saving}
              className="w-20"
              onChange={(e) => {
                const count = Number(e.target.value)
                if (count >= 1 && count <= 365) save({ ...schedule, retentionCount: count })
              }}
            />
          </div>
        </div>

        <Hint>
          {schedule.lastRunAt
            ? t("backup.lastRun", { when: new Date(schedule.lastRunAt).toLocaleString("pl-PL") })
            : t("backup.neverRun")}
        </Hint>
        <FormError>{error}</FormError>
        {saved && !error && <Hint>{t("backup.saved")}</Hint>}
      </div>
    </>
  )
}

function DangerZoneSection() {
  const { t } = useLanguage()
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [typedPhrase, setTypedPhrase] = useState("")
  const [clearing, setClearing] = useState(false)
  const [error, setError] = useState("")
  const [done, setDone] = useState(false)

  const phrase = t("storage.clearDatabasePhrase")

  function openConfirm() {
    setTypedPhrase("")
    setError("")
    setDone(false)
    setConfirmOpen(true)
  }

  async function performClear() {
    setClearing(true)
    setError("")
    try {
      await api.clearDatabase()
      setDone(true)
    } catch (err) {
      setError(err instanceof Error ? err.message : t("storage.clearDatabaseFailed"))
    } finally {
      setClearing(false)
    }
  }

  return (
    <>
      <SectionLabel>{t("storage.dangerZoneLabel")}</SectionLabel>
      <div className="flex flex-col gap-2">
        <Hint>{t("storage.clearDatabaseHint")}</Hint>
        <div>
          <Button variant="destructive" onClick={openConfirm}>
            {t("storage.clearDatabaseButton")}
          </Button>
        </div>
      </div>

      {/* Trzy fazy w JEDNYM oknie (potwierdzenie -> w trakcie -> zakończono), zamiast
          zamykać dialog i pokazywać wynik osobno w tle strony -- okno "w trakcie" i
          "zakończono" celowo NIE da się zamknąć inaczej niż przyciskiem OK (który od razu
          przeładowuje stronę), żeby po czyszczeniu nigdzie nie zostały żadne nieaktualne
          dane wciąż trzymane w pamięci przeglądarki (zaznaczenia, zbuforowane listy itp.). */}
      <Dialog open={confirmOpen} onOpenChange={(next) => !clearing && !done && setConfirmOpen(next)}>
        <DialogContent showCloseButton={!clearing && !done}>
          {done ? (
            <>
              <DialogHeader>
                <DialogTitle>{t("storage.clearDatabaseDoneTitle")}</DialogTitle>
                <DialogDescription>{t("storage.clearDatabaseDoneDescription")}</DialogDescription>
              </DialogHeader>
              <DialogFooter>
                <Button onClick={() => window.location.reload()}>{t("common.ok")}</Button>
              </DialogFooter>
            </>
          ) : clearing ? (
            <>
              <DialogHeader>
                <DialogTitle>{t("storage.clearDatabaseInProgressTitle")}</DialogTitle>
                <DialogDescription>{t("storage.clearDatabaseInProgressDescription")}</DialogDescription>
              </DialogHeader>
              <div className="flex items-center justify-center py-4">
                <Loader2 className="size-6 animate-spin text-muted-foreground" />
              </div>
            </>
          ) : (
            <>
              <DialogHeader>
                <DialogTitle>{t("storage.clearDatabaseConfirmTitle")}</DialogTitle>
                <DialogDescription>
                  {t("storage.clearDatabaseConfirmDescription", { phrase })}
                </DialogDescription>
              </DialogHeader>
              <div className="flex flex-col gap-2">
                <Label htmlFor="clear-database-phrase">
                  {t("storage.clearDatabasePhraseLabel", { phrase })}
                </Label>
                <Input
                  id="clear-database-phrase"
                  value={typedPhrase}
                  onChange={(e) => setTypedPhrase(e.target.value)}
                  autoComplete="off"
                />
                <FormError>{error}</FormError>
              </div>
              <DialogFooter>
                <Button variant="outline" onClick={() => setConfirmOpen(false)}>
                  {t("common.cancel")}
                </Button>
                <Button variant="destructive" onClick={performClear} disabled={typedPhrase !== phrase}>
                  {t("storage.clearDatabaseConfirmButton")}
                </Button>
              </DialogFooter>
            </>
          )}
        </DialogContent>
      </Dialog>
    </>
  )
}

export { StorageSettingsView }
