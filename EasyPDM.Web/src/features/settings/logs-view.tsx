import { useEffect, useMemo, useRef, useState } from "react"

import { api } from "@/api/client"
import type { LogFile } from "@/api/types"
import { Button } from "@/components/ui/button"
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
import { useLanguage } from "@/i18n/use-language"

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  const units = ["KB", "MB", "GB"]
  let value = bytes
  let unitIndex = -1
  do {
    value /= 1024
    unitIndex++
  } while (value >= 1024 && unitIndex < units.length - 1)
  return `${value.toFixed(1)} ${units[unitIndex]}`
}

function formatDate(date: string): string {
  return new Date(`${date}T00:00:00`).toLocaleDateString("pl-PL", {
    weekday: "short",
    year: "numeric",
    month: "long",
    day: "numeric",
  })
}

function lineClassName(line: string): string {
  if (line.includes("[ERR]") || line.includes("[CRT]")) return "text-destructive"
  if (line.includes("[WRN]")) return "text-amber-600 dark:text-amber-400"
  return "text-foreground"
}

function LogsView() {
  const { t } = useLanguage()
  const [files, setFiles] = useState<LogFile[] | null>(null)
  const [selectedDate, setSelectedDate] = useState("")
  const [lines, setLines] = useState<string[]>([])
  const [totalLines, setTotalLines] = useState(0)
  const [truncated, setTruncated] = useState(false)
  const [filter, setFilter] = useState("")
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState("")
  const [loadError, setLoadError] = useState(false)

  useEffect(() => {
    api
      .getLogFiles()
      .then((result) => {
        setFiles(result)
        if (result.length > 0) setSelectedDate(result[0].date)
      })
      .catch(() => setLoadError(true))
  }, [])

  // Licznik żądań — "Odśwież" i zmiana daty w Select mogą odpalić dwa GET-y niemal
  // jednocześnie; bez tego strażnika odpowiedź, która wróci PÓŹNIEJ (niekoniecznie ta,
  // której użytkownik czeka jako ostatniej), mogłaby nadpisać zawartość nowszym-a-już-
  // nieaktualnym wynikiem dla innej daty.
  const loadRequestId = useRef(0)

  async function loadContent(date: string) {
    if (!date) return
    const requestId = ++loadRequestId.current
    setLoading(true)
    setError("")
    try {
      const content = await api.getLogContent(date)
      if (loadRequestId.current !== requestId) return
      setLines(content.lines)
      setTotalLines(content.totalLines)
      setTruncated(content.truncated)
    } catch (err) {
      if (loadRequestId.current !== requestId) return
      setError(err instanceof Error ? err.message : t("logs.loadFailed"))
    } finally {
      if (loadRequestId.current === requestId) setLoading(false)
    }
  }

  useEffect(() => {
    loadContent(selectedDate)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedDate])

  const filteredLines = useMemo(() => {
    if (!filter.trim()) return lines
    const needle = filter.trim().toLowerCase()
    return lines.filter((line) => line.toLowerCase().includes(needle))
  }, [lines, filter])

  if (loadError) {
    return (
      <div className="mx-auto max-w-4xl">
        <h2 className="mb-4 text-lg font-semibold tracking-tight">{t("settings.logs")}</h2>
        <Hint>{t("database.loadError")}</Hint>
      </div>
    )
  }

  if (files === null) return null

  return (
    <div className="mx-auto max-w-4xl">
      <h2 className="mb-4 text-lg font-semibold tracking-tight">{t("settings.logs")}</h2>

      <div className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
        {files.length === 0 ? (
          <Hint>{t("logs.empty")}</Hint>
        ) : (
          <>
            <div className="flex flex-wrap items-end gap-3">
              <div className="flex flex-col gap-1">
                <Label>{t("logs.dayLabel")}</Label>
                <Select value={selectedDate} onValueChange={(v) => setSelectedDate(v ?? "")}>
                  <SelectTrigger className="min-w-56">
                    <SelectValue>
                      {(v: string) => {
                        const file = files.find((f) => f.date === v)
                        return file ? `${formatDate(file.date)} · ${formatBytes(file.sizeBytes)}` : v
                      }}
                    </SelectValue>
                  </SelectTrigger>
                  <SelectContent>
                    {files.map((f) => (
                      <SelectItem key={f.date} value={f.date}>
                        {formatDate(f.date)} · {formatBytes(f.sizeBytes)}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>

              <Input
                value={filter}
                onChange={(e) => setFilter(e.target.value)}
                placeholder={t("logs.filterPlaceholder")}
                className="min-w-52 flex-1"
              />

              <Button variant="outline" onClick={() => loadContent(selectedDate)} disabled={loading}>
                {t("common.refresh")}
              </Button>

              <a href={api.logDownloadUrl(selectedDate)} download>
                <Button variant="outline">{t("logs.downloadButton")}</Button>
              </a>
            </div>

            {truncated && (
              <p className="mt-2 text-[12.5px] text-muted-foreground">
                {t("logs.truncatedHint", { shown: lines.length, total: totalLines })}
              </p>
            )}

            <pre className="mt-3 max-h-[60vh] overflow-auto rounded-md bg-muted p-3 text-[11.5px] leading-relaxed whitespace-pre-wrap">
              {filteredLines.length === 0 ? (
                <span className="text-muted-foreground">{t("logs.noMatchingLines")}</span>
              ) : (
                filteredLines.map((line, i) => (
                  <div key={i} className={lineClassName(line)}>
                    {line}
                  </div>
                ))
              )}
            </pre>

            <FormError className="mt-2">{error}</FormError>
          </>
        )}
      </div>
    </div>
  )
}

export { LogsView }
