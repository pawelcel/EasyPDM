import { useEffect, useMemo, useState, type ReactElement } from "react"
import { Pencil, Trash2 } from "lucide-react"

import { api, ApiError } from "@/api/client"
import type { Process } from "@/api/types"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { ConfirmDialog } from "@/components/ui/confirm-dialog"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
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
import { SuggestInput } from "@/components/ui/suggest-input"
import { Textarea } from "@/components/ui/textarea"
import { useProcesses } from "@/features/processes/use-processes"
import { useLanguage } from "@/i18n/use-language"

type ProcessBody = { name: string; description: string | null; forParts: boolean; forAssemblies: boolean }

function ProcessesView() {
  const { t } = useLanguage()
  const { processes, refetch } = useProcesses()
  const [confirmingDeleteId, setConfirmingDeleteId] = useState<number | null>(null)
  const [deletingPending, setDeletingPending] = useState(false)
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const confirmingDelete = processes.find((p) => p.id === confirmingDeleteId) ?? null

  // Filtr listy: tekst po nazwie i opisie oraz to, czego obróbka dotyczy — przy kilku
  // wariantach tej samej nazwy ("Hartowanie") lista szybko robi się długa.
  const [query, setQuery] = useState("")
  const [appliesTo, setAppliesTo] = useState<"all" | "parts" | "assemblies">("all")
  const visible = processes.filter((p) => {
    const q = query.trim().toLocaleLowerCase()
    const matchesText =
      !q || p.name.toLocaleLowerCase().includes(q) || (p.description ?? "").toLocaleLowerCase().includes(q)
    const matchesTarget = appliesTo === "all" || (appliesTo === "parts" ? p.forParts : p.forAssemblies)
    return matchesText && matchesTarget
  })

  async function confirmDelete() {
    if (confirmingDeleteId === null) return
    setDeletingPending(true)
    setDeleteError(null)
    try {
      await api.removeProcess(confirmingDeleteId)
      setConfirmingDeleteId(null)
      await refetch()
    } catch (err) {
      setDeleteError(err instanceof Error ? err.message : t("process.deleteFailed"))
    } finally {
      setDeletingPending(false)
    }
  }

  return (
    <div className="mx-auto max-w-3xl">
      <h2 className="mb-4 text-lg font-semibold tracking-tight">{t("nav.processes")}</h2>

      <div className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
        <div className="mb-3 flex flex-wrap items-center gap-2">
          <ProcessDialog
            trigger={<Button>{t("process.addButton")}</Button>}
            title={t("process.addTitle")}
            processes={processes}
            onSubmit={async (body) => {
              await api.addProcess(body)
              await refetch()
            }}
          />
          <Input
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder={t("process.searchPlaceholder")}
            className="min-w-48 flex-1"
          />
          <Select value={appliesTo} onValueChange={(v) => setAppliesTo(v as typeof appliesTo)}>
            <SelectTrigger className="min-w-36">
              <SelectValue>
                {(v: string) =>
                  v === "parts" ? t("process.forParts") : v === "assemblies" ? t("process.forAssemblies") : t("process.filterAll")
                }
              </SelectValue>
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">{t("process.filterAll")}</SelectItem>
              <SelectItem value="parts">{t("process.forParts")}</SelectItem>
              <SelectItem value="assemblies">{t("process.forAssemblies")}</SelectItem>
            </SelectContent>
          </Select>
        </div>

        {visible.length > 0 ? (
          <ul className="flex flex-col gap-0.5">
            {visible.map((p) => (
              <li
                key={p.id}
                className="flex items-center justify-between gap-2 rounded-md px-2 py-1.5 text-sm hover:bg-accent"
              >
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-1.5">
                    <span className="truncate font-medium">{p.name}</span>
                    {p.forParts && <Badge variant="secondary">{t("process.forParts")}</Badge>}
                    {p.forAssemblies && <Badge variant="secondary">{t("process.forAssemblies")}</Badge>}
                  </div>
                  {p.description && (
                    <div className="text-[12.5px] whitespace-pre-line text-muted-foreground">{p.description}</div>
                  )}
                </div>
                <div className="flex shrink-0 items-center gap-0.5">
                  <ProcessDialog
                    trigger={
                      <Button size="icon-xs" variant="ghost" aria-label={t("common.editNamed", { name: p.name })}>
                        <Pencil className="size-3.5 text-muted-foreground" />
                      </Button>
                    }
                    title={t("process.editTitle")}
                    initial={p}
                    processes={processes}
                    onSubmit={async (body) => {
                      await api.updateProcess(p.id, body)
                      await refetch()
                    }}
                  />
                  <Button
                    size="icon-xs"
                    variant="ghost"
                    aria-label={t("common.deleteNamed", { name: p.name })}
                    onClick={() => setConfirmingDeleteId(p.id)}
                  >
                    <Trash2 className="size-3.5 text-muted-foreground" />
                  </Button>
                </div>
              </li>
            ))}
          </ul>
        ) : (
          <Hint>{processes.length === 0 ? t("process.emptyAll") : t("process.emptyFiltered")}</Hint>
        )}
      </div>

      {confirmingDelete && (
        <ConfirmDialog
          open
          title={t("common.deleteNamed", { name: confirmingDelete.name })}
          description={t("process.deleteConfirmDescription", { name: confirmingDelete.name })}
          confirmLabel={t("common.delete")}
          variant="destructive"
          onConfirm={confirmDelete}
          onCancel={() => setConfirmingDeleteId(null)}
          pending={deletingPending}
          error={deleteError}
        />
      )}
    </div>
  )
}

// Okno dodawania i edycji. Nazwa jest polem dynamicznym: w trakcie pisania podpowiada
// obróbki z katalogu, żeby było widać, co już jest. Okno zawsze zapisuje TĘ obróbkę, którą się
// w nim wpisuje, i ta sama nazwa może wystąpić kilka razy — "Hartowanie" do 50 HRC i
// "Hartowanie" indukcyjne to ten sam proces z inną specyfikacją, a odróżnia je opis.
function ProcessDialog({
  trigger,
  title,
  initial,
  processes,
  onSubmit,
}: {
  trigger: ReactElement
  title: string
  initial?: Process
  processes: Process[]
  onSubmit: (body: ProcessBody) => Promise<void>
}) {
  const { t } = useLanguage()
  const [open, setOpen] = useState(false)
  const [name, setName] = useState(initial?.name ?? "")
  const [description, setDescription] = useState(initial?.description ?? "")
  const [forParts, setForParts] = useState(initial?.forParts ?? true)
  const [forAssemblies, setForAssemblies] = useState(initial?.forAssemblies ?? false)
  const [error, setError] = useState("")

  // Inna obróbka z katalogu o tej samej nazwie (bez względu na wielkość liter) — nie błąd, tylko
  // przypomnienie, że opis będzie jedynym, co je odróżni.
  const sameName = useMemo(() => {
    const key = name.trim().toLocaleLowerCase()
    return key ? (processes.find((p) => p.id !== initial?.id && p.name.toLocaleLowerCase() === key) ?? null) : null
  }, [initial, name, processes])

  function reset() {
    setName(initial?.name ?? "")
    setDescription(initial?.description ?? "")
    setForParts(initial?.forParts ?? true)
    setForAssemblies(initial?.forAssemblies ?? false)
    setError("")
  }

  useEffect(reset, [initial]) // eslint-disable-line react-hooks/exhaustive-deps

  async function submit() {
    const trimmed = name.trim()
    if (!trimmed) {
      setError(t("process.nameRequired"))
      return
    }
    if (!forParts && !forAssemblies) {
      setError(t("process.appliesRequired"))
      return
    }
    setError("")
    try {
      await onSubmit({ name: trimmed, description: description.trim() || null, forParts, forAssemblies })
      setOpen(false)
      reset()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t("process.saveFailed"))
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next)
        if (!next) reset()
      }}
    >
      <DialogTrigger render={trigger} />
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
        </DialogHeader>

        <div className="flex flex-col gap-2">
          <Label htmlFor="process-name">{t("process.nameLabel")}</Label>
          <SuggestInput
            id="process-name"
            value={name}
            onChange={setName}
            suggestions={processes.filter((p) => p.id !== initial?.id).map((p) => p.name)}
            placeholder={t("process.namePlaceholder")}
            newValueHint={initial ? undefined : t("process.newHint")}
            nearDuplicateHint={false}
          />
          {sameName && <Hint>{t("process.sameNameHint", { name: sameName.name })}</Hint>}

          <Label htmlFor="process-description">{t("process.descriptionLabel")}</Label>
          <Textarea
            id="process-description"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            rows={3}
            placeholder={t("process.descriptionPlaceholder")}
          />

          <Label>{t("process.appliesTo")}</Label>
          <div className="flex gap-4 text-sm">
            <label className="flex cursor-pointer items-center gap-2">
              <input
                type="checkbox"
                checked={forParts}
                onChange={(e) => setForParts(e.target.checked)}
                className="size-3.5 shrink-0 accent-primary"
              />
              {t("process.forParts")}
            </label>
            <label className="flex cursor-pointer items-center gap-2">
              <input
                type="checkbox"
                checked={forAssemblies}
                onChange={(e) => setForAssemblies(e.target.checked)}
                className="size-3.5 shrink-0 accent-primary"
              />
              {t("process.forAssemblies")}
            </label>
          </div>

          <FormError>{error}</FormError>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => setOpen(false)}>
            {t("common.cancel")}
          </Button>
          <Button onClick={submit}>{initial ? t("common.save") : t("common.add")}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export { ProcessesView }
