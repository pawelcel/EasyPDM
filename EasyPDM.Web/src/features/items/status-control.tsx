import { useEffect, useState } from "react"

import { api } from "@/api/client"
import { revisionLabel, STATUS_LABEL_KEYS, type Item, type ItemStatus, type RevisionComment } from "@/api/types"
import { Button } from "@/components/ui/button"
import { buttonVariants } from "@/components/ui/button-variants"
import { ConfirmDialog } from "@/components/ui/confirm-dialog"
import { Label } from "@/components/ui/label"
import { Textarea } from "@/components/ui/textarea"
import { useLanguage } from "@/i18n/use-language"
import { cn } from "@/lib/utils"

const NEXT_STATUSES: Record<ItemStatus, ItemStatus[]> = {
  w_pracy: ["sprawdzany"],
  sprawdzany: ["w_pracy", "wydany"],
  wydany: ["w_pracy"],
}

// Stała kolejność wszystkich statusów -- przyciski renderowane są zawsze w tym samym
// układzie (bieżący jako odznaka, reszta jako przyciski), żeby nic nie zmieniało pozycji
// przy zmianie statusu. Nieosiągalne stąd bezpośrednio przejścia (np. w_pracy -> wydany)
// są nadal widoczne, tylko wyszarzone/nieaktywne.
const ALL_STATUSES: ItemStatus[] = ["w_pracy", "sprawdzany", "wydany"]

function StatusControl({
  item,
  disabled = false,
  onChanged,
}: {
  item: Item
  disabled?: boolean
  onChanged: () => void | Promise<void>
}) {
  const { t } = useLanguage()
  const status = item.status ?? "w_pracy"
  const [pending, setPending] = useState<ItemStatus | null>(null)
  const [comment, setComment] = useState("")
  const [revisions, setRevisions] = useState<RevisionComment[]>([])
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    api.getRevisionComments(item.id).then(setRevisions).catch(() => setRevisions([]))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [item.id, item.revisionNumber])

  async function confirmChange() {
    if (!pending) return
    setSubmitting(true)
    setError(null)
    try {
      await api.setStatus(item.id, pending, isRevisionBump ? comment.trim() : undefined)
      setPending(null)
      setComment("")
      await onChanged()
    } catch (err) {
      setError(err instanceof Error ? err.message : t("item.statusChangeFailed"))
    } finally {
      setSubmitting(false)
    }
  }

  const isRevisionBump = status === "wydany" && pending === "w_pracy"
  const current = item.revisionNumber ?? 1

  return (
    <div className="flex flex-wrap items-center gap-2">
      <div className="flex gap-1.5">
        {ALL_STATUSES.map((s) =>
          s === status ? (
            <span key={s} className={cn(buttonVariants({ variant: "default", size: "sm" }))}>
              {t(STATUS_LABEL_KEYS[s])}
            </span>
          ) : (
            <Button
              key={s}
              size="sm"
              variant="outline"
              disabled={disabled || !NEXT_STATUSES[status].includes(s)}
              onClick={() => {
                setComment("")
                setError(null)
                setPending(s)
              }}
            >
              → {t(STATUS_LABEL_KEYS[s])}
            </Button>
          )
        )}
      </div>

      {revisions.length > 0 && (
        <div className="flex w-full flex-col gap-0.5 text-[12.5px] text-muted-foreground">
          {revisions.map((r) => (
            <div key={r.revisionNumber}>
              <span className="font-medium">rev. {revisionLabel(r.revisionNumber)}:</span> {r.comment}
            </div>
          ))}
        </div>
      )}

      {pending && (
        <ConfirmDialog
          open
          title={t("item.statusChangeTitle")}
          description={
            isRevisionBump ? (
              <div className="flex flex-col gap-2">
                <p>
                  {t("item.revisionBumpNotice", {
                    statusFrom: t("status.wydany"),
                    statusTo: t("status.w_pracy"),
                    from: revisionLabel(current),
                    to: revisionLabel(current + 1),
                  })}
                </p>
                <div className="flex flex-col gap-1">
                  <Label htmlFor="revision-comment">{t("item.revisionCommentLabel")}</Label>
                  <Textarea
                    id="revision-comment"
                    value={comment}
                    onChange={(e) => setComment(e.target.value)}
                    rows={3}
                    placeholder={t("item.revisionCommentPlaceholder")}
                  />
                </div>
              </div>
            ) : (
              t("item.statusChangeConfirmText", {
                statusFrom: t(STATUS_LABEL_KEYS[status]),
                statusTo: t(STATUS_LABEL_KEYS[pending]),
              })
            )
          }
          confirmLabel={t("item.statusChangeConfirm")}
          onConfirm={confirmChange}
          onCancel={() => {
            setPending(null)
            setComment("")
          }}
          pending={submitting}
          error={error}
        />
      )}
    </div>
  )
}

export { StatusControl }
