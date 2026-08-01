import { useEffect, useState } from "react"

import { api } from "@/api/client"
import type { StorageInfo } from "@/api/types"
import { Button } from "@/components/ui/button"
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
      setError("Nowa ścieżka nie może być pusta.")
      return
    }
    if (info && trimmed === info.path) {
      setError("Nowa ścieżka jest taka sama jak obecna.")
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
          ? `Lokalizacja zmieniona, przeniesiono ${res.migratedFiles} plików.`
          : "Lokalizacja zmieniona dla nowych plików. Istniejące pliki zostały w poprzednim miejscu."
      )
      await refetch()
    } catch (err) {
      setConfirmOpen(false)
      setError(err instanceof Error ? err.message : "Nie udało się zmienić lokalizacji.")
    } finally {
      setMoving(false)
    }
  }

  if (!info) return null

  return (
    <div className="mx-auto max-w-2xl">
      <h2 className="mb-4 text-lg font-semibold tracking-tight">Magazyn plików</h2>

      <div className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
        <SectionLabel>Bieżąca lokalizacja</SectionLabel>
        <p className="text-sm">{info.path}</p>
        <p className="text-[12.5px] text-muted-foreground">
          {info.fileCount} {info.fileCount === 1 ? "plik" : "plików"} · {formatBytes(info.totalSizeBytes)}
        </p>

        <SectionLabel>Nowa lokalizacja</SectionLabel>
        <div className="flex flex-col gap-2">
          <Label htmlFor="storage-new-path">Ścieżka bezwzględna na dysku serwera</Label>
          <Input
            id="storage-new-path"
            value={newPath}
            onChange={(e) => setNewPath(e.target.value)}
            placeholder="np. /mnt/dysk/magazyn"
          />
          <div>
            <Button onClick={requestMove} disabled={moving}>
              Zmień lokalizację
            </Button>
          </div>
          <FormError>{error}</FormError>
          {result && <Hint>{result}</Hint>}
        </div>

        <SectionLabel>Kopia zapasowa</SectionLabel>
        <Hint>Pobiera jeden plik ZIP z bazą danych (pg_dump) i całym magazynem plików.</Hint>
        <div className="mt-2">
          <a href={api.backupUrl()} download>
            <Button variant="outline">⬇ Pobierz kopię zapasową</Button>
          </a>
        </div>
      </div>

      <Dialog open={confirmOpen} onOpenChange={(next) => !moving && setConfirmOpen(next)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Przenieść istniejące pliki?</DialogTitle>
            <DialogDescription>
              Zmieniasz lokalizację magazynu z „{info.path}” na „{newPath.trim()}”. Czy przenieść
              tam też już istniejące pliki (skopiuje je i zaktualizuje bazę), czy zostawić je w
              obecnym miejscu i przenieść tylko lokalizację dla nowo dodawanych plików?
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setConfirmOpen(false)} disabled={moving}>
              Anuluj
            </Button>
            <Button variant="outline" onClick={() => performMove(false)} disabled={moving}>
              Nie przenoś
            </Button>
            <Button onClick={() => performMove(true)} disabled={moving}>
              Przenieś pliki
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}

export { StorageSettingsView }
