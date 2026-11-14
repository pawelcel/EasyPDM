import { useCallback, useEffect, useState } from "react"

import { ApiError, api } from "@/api/client"
import { itemTypeLabelKey, projectLabel, type BinEntry, type Item, type Project } from "@/api/types"
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
import { Label } from "@/components/ui/label"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { useLanguage } from "@/i18n/use-language"

// The recycle bin (BinEndpoints.cs). An admin sees every deleted item; anyone else sees only
// what they deleted. Restore puts an item back in the folder it was in; when that folder is gone
// the server answers needsFolder and this page asks where to put it. Purge is admins only.

const ROOT = "__root__"

function formatWhen(iso: string): string {
  return new Date(iso).toLocaleString("en-PK", {
    year: "numeric",
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
  })
}

function needsFolderAnswer(err: unknown): boolean {
  if (!(err instanceof ApiError) || err.status !== 409) return false
  try {
    return JSON.parse(err.message).needsFolder === true
  } catch {
    return false
  }
}

function errorText(err: unknown, fallback: string): string {
  if (!(err instanceof Error) || !err.message) return fallback
  try {
    const parsed = JSON.parse(err.message)
    return typeof parsed.message === "string" ? parsed.message : err.message
  } catch {
    return err.message
  }
}

function PlaceDialog({
  entry,
  onClose,
  onRestored,
}: {
  entry: BinEntry
  onClose: () => void
  onRestored: () => void
}) {
  const { t } = useLanguage()
  const [projects, setProjects] = useState<Project[]>([])
  const [projectId, setProjectId] = useState(entry.projectGone ? "" : (entry.projectId ?? ""))
  const [folders, setFolders] = useState<Item[]>([])
  const [folderId, setFolderId] = useState(ROOT)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState("")

  useEffect(() => {
    api.getProjects().then(setProjects).catch(() => setProjects([]))
  }, [])

  useEffect(() => {
    setFolderId(ROOT)
    if (!projectId) {
      setFolders([])
      return
    }
    api
      .getItems({ projectId })
      .then((items) => setFolders(items.filter((i) => i.itemType === "folder")))
      .catch(() => setFolders([]))
  }, [projectId])

  async function restore() {
    setPending(true)
    setError("")
    try {
      await api.restoreBinEntry(entry.id, folderId === ROOT ? { projectId } : { folderId })
      onRestored()
    } catch (err) {
      setError(errorText(err, t("bin.restoreFailed")))
    } finally {
      setPending(false)
    }
  }

  return (
    <Dialog open onOpenChange={(next) => !next && !pending && onClose()}>
      <DialogContent showCloseButton={!pending}>
        <DialogHeader>
          <DialogTitle>{t("bin.placeTitle", { name: entry.label })}</DialogTitle>
          <DialogDescription>{t("bin.placeDescription")}</DialogDescription>
        </DialogHeader>
        <div className="space-y-3">
          <div className="space-y-1.5">
            <Label>{t("bin.placeProject")}</Label>
            <Select value={projectId || "none"} onValueChange={(v) => setProjectId(v === "none" ? "" : (v as string))}>
              <SelectTrigger className="w-full">
                <SelectValue>
                  {(v: string) => {
                    const p = projects.find((x) => x.id === v)
                    return p ? projectLabel(p) : t("bin.placeChooseProject")
                  }}
                </SelectValue>
              </SelectTrigger>
              <SelectContent>
                {projects
                  .filter((p) => !p.closed)
                  .map((p) => (
                    <SelectItem key={p.id} value={p.id}>
                      {projectLabel(p)}
                    </SelectItem>
                  ))}
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-1.5">
            <Label>{t("bin.placeFolder")}</Label>
            <Select value={folderId} onValueChange={(v) => setFolderId(v as string)} disabled={!projectId}>
              <SelectTrigger className="w-full">
                <SelectValue>
                  {(v: string) =>
                    v === ROOT ? t("bin.placeRoot") : (folders.find((f) => f.id === v)?.fileName ?? v)
                  }
                </SelectValue>
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={ROOT}>{t("bin.placeRoot")}</SelectItem>
                {folders.map((f) => (
                  <SelectItem key={f.id} value={f.id}>
                    {f.fileName}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        </div>
        {error && <FormError>{error}</FormError>}
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={pending}>
            {t("common.cancel")}
          </Button>
          <Button onClick={restore} disabled={pending || !projectId}>
            {t("bin.restore")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function BinView({ isAdmin }: { isAdmin: boolean }) {
  const { t } = useLanguage()
  const [entries, setEntries] = useState<BinEntry[] | null>(null)
  const [loadError, setLoadError] = useState(false)
  const [error, setError] = useState("")
  const [busyId, setBusyId] = useState("")
  const [placing, setPlacing] = useState<BinEntry | null>(null)
  const [purging, setPurging] = useState<BinEntry | null>(null)
  const [emptying, setEmptying] = useState(false)
  const [confirmPending, setConfirmPending] = useState(false)
  const [confirmError, setConfirmError] = useState("")

  const load = useCallback(() => {
    api
      .getBin()
      .then((result) => {
        setEntries(result)
        setLoadError(false)
      })
      .catch(() => setLoadError(true))
  }, [])

  useEffect(load, [load])

  async function restore(entry: BinEntry) {
    if (entry.needsFolder) {
      setPlacing(entry)
      return
    }
    setBusyId(entry.id)
    setError("")
    try {
      await api.restoreBinEntry(entry.id)
      load()
    } catch (err) {
      if (needsFolderAnswer(err)) setPlacing(entry)
      else setError(errorText(err, t("bin.restoreFailed")))
    } finally {
      setBusyId("")
    }
  }

  async function confirmPurge() {
    setConfirmPending(true)
    setConfirmError("")
    try {
      if (emptying) await api.emptyBin()
      else if (purging) await api.purgeBinEntry(purging.id)
      setPurging(null)
      setEmptying(false)
      load()
    } catch (err) {
      setConfirmError(errorText(err, t("bin.purgeFailed")))
    } finally {
      setConfirmPending(false)
    }
  }

  function typeLabel(entry: BinEntry): string {
    const key = itemTypeLabelKey({ itemType: entry.itemType as Item["itemType"], fileType: null })
    return key ? t(key) : t("bin.file")
  }

  if (loadError) return <FormError>{t("bin.loadFailed")}</FormError>
  if (entries === null) return <Hint>{t("common.loading")}</Hint>

  return (
    <div className="space-y-4">
      <div className="flex items-start justify-between gap-4">
        <div>
          <h2 className="text-lg font-semibold">{t("bin.title")}</h2>
          <p className="text-sm text-muted-foreground">{t(isAdmin ? "bin.descriptionAdmin" : "bin.descriptionUser")}</p>
        </div>
        {isAdmin && entries.length > 0 && (
          <Button variant="destructive" size="sm" onClick={() => setEmptying(true)}>
            {t("bin.empty")}
          </Button>
        )}
      </div>

      {error && <FormError>{error}</FormError>}

      {entries.length === 0 ? (
        <Hint>{t("bin.none")}</Hint>
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>{t("bin.item")}</TableHead>
              <TableHead>{t("bin.wasIn")}</TableHead>
              <TableHead>{t("bin.deletedBy")}</TableHead>
              <TableHead>{t("bin.deletedAt")}</TableHead>
              <TableHead className="w-48" />
            </TableRow>
          </TableHeader>
          <TableBody>
            {entries.map((entry) => (
              <TableRow key={entry.id}>
                <TableCell>
                  <div className="font-medium">{entry.label}</div>
                  <div className="text-xs text-muted-foreground">
                    {typeLabel(entry)}
                    {entry.itemCount > 1 && ` · ${t("bin.withMore", { count: entry.itemCount - 1 })}`}
                  </div>
                </TableCell>
                <TableCell>
                  <div>{entry.projectName ?? t("bin.noProject")}</div>
                  {entry.location && <div className="text-xs text-muted-foreground">{entry.location}</div>}
                  {entry.needsFolder && <div className="text-xs text-amber-600 dark:text-amber-400">{t("bin.folderGone")}</div>}
                </TableCell>
                <TableCell>{entry.deletedByName}</TableCell>
                <TableCell className="whitespace-nowrap">{formatWhen(entry.deletedAt)}</TableCell>
                <TableCell className="space-x-2 text-right">
                  <Button size="sm" variant="outline" disabled={busyId === entry.id} onClick={() => restore(entry)}>
                    {t("bin.restore")}
                  </Button>
                  {entry.canPurge && (
                    <Button size="sm" variant="destructive" onClick={() => setPurging(entry)}>
                      {t("bin.purge")}
                    </Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}

      {placing && (
        <PlaceDialog
          entry={placing}
          onClose={() => setPlacing(null)}
          onRestored={() => {
            setPlacing(null)
            load()
          }}
        />
      )}

      <ConfirmDialog
        open={!!purging || emptying}
        title={emptying ? t("bin.emptyTitle") : t("bin.purgeTitle")}
        description={
          emptying ? t("bin.emptyDescription", { count: entries.length }) : t("bin.purgeDescription", { name: purging?.label ?? "" })
        }
        confirmLabel={emptying ? t("bin.empty") : t("bin.purge")}
        variant="destructive"
        pending={confirmPending}
        error={confirmError}
        onConfirm={confirmPurge}
        onCancel={() => {
          setPurging(null)
          setEmptying(false)
          setConfirmError("")
        }}
      />
    </div>
  )
}

export { BinView }
