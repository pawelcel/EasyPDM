import { useCallback, useEffect, useState } from "react"
import { ArrowUpRight, Check, MessageSquareWarning, Star, Trash2 } from "lucide-react"

import { api } from "@/api/client"
import type { MyWork, MyWorkAssembly, ProgressEntry } from "@/api/types"
import { Button } from "@/components/ui/button"
import { FormError } from "@/components/ui/form-error"
import { Hint } from "@/components/ui/hint"
import { Input } from "@/components/ui/input"
import { PANEL_CARD } from "@/components/ui/panel-card"
import { Textarea } from "@/components/ui/textarea"
import { useLanguage } from "@/i18n/use-language"
import { cn } from "@/lib/utils"

// "My work" (main menu): the projects I take part in, the assemblies assigned to me in each, and
// their progress reports one under another. A project I lead shows all its assemblies that have
// people on them, and I accept each report or send it back with remarks. My latest report can be
// deleted for a while after writing it (Settings -> Users) and only until it is reviewed.
function MyWorkView({
  onOpenProject,
  onOpenItem,
}: {
  onOpenProject: (projectId: string) => void
  onOpenItem: (projectId: string, itemId: string) => void
}) {
  const { t } = useLanguage()
  const [work, setWork] = useState<MyWork | null>(null)
  const [error, setError] = useState("")

  const reload = useCallback(async () => {
    try {
      setWork(await api.getMyWork())
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    }
  }, [])

  useEffect(() => {
    void reload()
  }, [reload])

  if (!work) return error ? <FormError>{error}</FormError> : <Hint>{t("common.loading")}</Hint>

  return (
    <div className="mx-auto flex max-w-4xl flex-col gap-4">
      <h2 className="text-lg font-semibold tracking-tight">{t("myWork.title")}</h2>
      {work.projects.length === 0 && <Hint>{t("myWork.noProjects")}</Hint>}
      {work.projects.map((p) => (
        <div key={p.projectId} className={PANEL_CARD}>
          <div className="flex items-center gap-2">
            <div className="min-w-0 flex-1 truncate text-[15px] font-semibold">{p.projectName}</div>
            {p.isLead && (
              <span className="rounded-full bg-primary/15 px-2 py-0.5 text-[11.5px] font-medium text-primary">
                {t("myWork.youLead")}
              </span>
            )}
            <Button
              type="button"
              size="icon-xs"
              onClick={() => onOpenProject(p.projectId)}
              aria-label={t("client.goToProjectAria")}
              title={t("client.goToProjectAria")}
            >
              <ArrowUpRight />
            </Button>
          </div>
          {p.assemblies.length === 0 ? (
            <Hint>{p.isLead ? t("myWork.noAssembliesLead") : t("myWork.noAssemblies")}</Hint>
          ) : (
            <div className="mt-2 flex flex-col gap-3">
              {p.assemblies.map((a) => (
                <AssemblyProgress
                  key={a.itemId}
                  assembly={a}
                  onOpen={() => onOpenItem(p.projectId, a.itemId)}
                  isLead={p.isLead}
                  deleteMinutes={work.deleteMinutes}
                  onChanged={reload}
                />
              ))}
            </div>
          )}
        </div>
      ))}
    </div>
  )
}

function AssemblyProgress({
  assembly,
  isLead,
  deleteMinutes,
  onChanged,
  onOpen,
}: {
  assembly: MyWorkAssembly
  onOpen: () => void
  isLead: boolean
  deleteMinutes: number
  onChanged: () => Promise<void>
}) {
  const { t } = useLanguage()
  const [note, setNote] = useState("")
  const [percent, setPercent] = useState("")
  const [error, setError] = useState("")
  const latest = assembly.entries.length > 0 ? assembly.entries[assembly.entries.length - 1].percent : 0

  async function run(action: () => Promise<unknown>) {
    setError("")
    try {
      await action()
      await onChanged()
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    }
  }

  return (
    <div className="rounded-lg border p-3">
      {/* First line: the assembly itself -- number, name and revision -- which people refer to it
          by, and which opens it in its project; the progress bar on the right. Who works on it
          goes on the line below. */}
      <div className="flex items-center gap-3">
        <button
          type="button"
          onClick={onOpen}
          className="group flex min-w-0 items-baseline gap-2 rounded-md text-left hover:text-primary"
          title={t("myWork.openAssembly")}
        >
          {assembly.number && (
            <span className="rounded-md bg-primary/15 px-2 py-0.5 font-mono text-base font-bold text-primary">
              {assembly.number}
            </span>
          )}
          <span className="truncate text-base font-semibold group-hover:underline">{assembly.name}</span>
          {assembly.revisionLabel && (
            <span className="shrink-0 text-sm text-muted-foreground">
              {t("item.revisionShort")} {assembly.revisionLabel}
            </span>
          )}
          <ArrowUpRight className="size-4 shrink-0 self-center text-muted-foreground group-hover:text-primary" />
        </button>
        <div className="ml-auto flex shrink-0 items-center gap-2 text-[12.5px] text-muted-foreground">
          <div className="h-1.5 w-28 overflow-hidden rounded-full bg-muted">
            <div className="h-full rounded-full bg-primary" style={{ width: `${latest}%` }} />
          </div>
          {latest}%
        </div>
      </div>
      <div className="mt-1 flex flex-wrap gap-x-3 gap-y-0.5 text-[12.5px] text-muted-foreground">
        {assembly.assignees.map((u) => (
          <span key={u.userId} className="inline-flex items-center gap-1">
            {u.isLead && <Star className="size-3 fill-amber-400 text-amber-400" />}
            {u.name}
          </span>
        ))}
      </div>

      {assembly.entries.length === 0 ? (
        <Hint>{t("myWork.noEntries")}</Hint>
      ) : (
        <ul className="mt-2 flex flex-col gap-1.5">
          {assembly.entries.map((e) => (
            <EntryRow key={e.id} entry={e} isLead={isLead} onRun={run} />
          ))}
        </ul>
      )}

      {assembly.canReport && (
        <div className="mt-2 flex flex-col gap-1.5">
          <div className="flex items-start gap-2">
            <Textarea
              className="min-h-9 flex-1"
              rows={1}
              value={note}
              placeholder={t("myWork.notePlaceholder")}
              onChange={(e) => setNote(e.target.value)}
            />
            <div className="flex items-center gap-1">
              <Input
                className="w-20"
                type="number"
                min={0}
                max={100}
                value={percent}
                placeholder="%"
                aria-label={t("myWork.percent")}
                onChange={(e) => setPercent(e.target.value)}
              />
              <span className="text-sm text-muted-foreground">%</span>
            </div>
            <Button
              type="button"
              size="sm"
              disabled={note.trim() === "" || percent === ""}
              onClick={() =>
                run(async () => {
                  await api.addProgress(assembly.itemId, note, Number(percent))
                  setNote("")
                  setPercent("")
                })
              }
            >
              {t("myWork.addEntry")}
            </Button>
          </div>
          <Hint>{t("myWork.deleteHint", { minutes: deleteMinutes })}</Hint>
        </div>
      )}
      <FormError>{error}</FormError>
    </div>
  )
}

function EntryRow({
  entry,
  isLead,
  onRun,
}: {
  entry: ProgressEntry
  isLead: boolean
  onRun: (action: () => Promise<unknown>) => Promise<void>
}) {
  const { t } = useLanguage()
  const [commenting, setCommenting] = useState(false)
  const [comment, setComment] = useState("")
  const when = new Date(entry.createdAt).toLocaleString()

  return (
    <li
      className={cn(
        "rounded-md bg-muted/40 px-2.5 py-1.5 text-sm",
        entry.reviewStatus === "remarks" && "ring-1 ring-amber-500/40",
        entry.reviewStatus === "accepted" && "ring-1 ring-emerald-500/30"
      )}
    >
      <div className="flex items-start gap-2">
        <span className="w-12 shrink-0 font-medium tabular-nums">{entry.percent}%</span>
        <div className="min-w-0 flex-1">
          <div className="whitespace-pre-wrap">{entry.note}</div>
          <div className="text-[12px] text-muted-foreground">
            {entry.authorName} · {when}
          </div>
          {entry.reviewStatus && (
            <div
              className={cn(
                "mt-1 text-[12.5px]",
                entry.reviewStatus === "accepted" ? "text-emerald-600 dark:text-emerald-400" : "text-amber-600 dark:text-amber-400"
              )}
            >
              {entry.reviewStatus === "accepted" ? t("myWork.accepted") : t("myWork.remarks")}
              {entry.reviewComment && `: ${entry.reviewComment}`}
              <span className="text-muted-foreground"> — {entry.reviewerName}</span>
            </div>
          )}
        </div>
        <div className="flex shrink-0 items-center gap-1">
          {isLead && (
            <>
              <Button
                type="button"
                size="xs"
                variant="outline"
                onClick={() => onRun(() => api.reviewProgress(entry.id, "accepted", ""))}
              >
                <Check /> {t("myWork.accept")}
              </Button>
              <Button type="button" size="xs" variant="outline" onClick={() => setCommenting((v) => !v)}>
                <MessageSquareWarning /> {t("myWork.addRemarks")}
              </Button>
            </>
          )}
          {entry.canDelete && (
            <Button
              type="button"
              size="icon-xs"
              variant="ghost"
              aria-label={t("myWork.deleteEntry")}
              title={t("myWork.deleteEntry")}
              onClick={() => onRun(() => api.deleteProgress(entry.id))}
            >
              <Trash2 />
            </Button>
          )}
        </div>
      </div>
      {commenting && (
        <div className="mt-1.5 flex items-start gap-2">
          <Textarea
            className="min-h-9 flex-1"
            rows={2}
            value={comment}
            placeholder={t("myWork.remarksPlaceholder")}
            onChange={(e) => setComment(e.target.value)}
          />
          <Button
            type="button"
            size="sm"
            disabled={comment.trim() === ""}
            onClick={() =>
              onRun(async () => {
                await api.reviewProgress(entry.id, "remarks", comment)
                setCommenting(false)
                setComment("")
              })
            }
          >
            {t("myWork.sendRemarks")}
          </Button>
        </div>
      )}
    </li>
  )
}

export { MyWorkView }
