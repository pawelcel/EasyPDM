import { useEffect, useState } from "react"

import { api } from "@/api/client"
import { revisionLabel, STATUS_LABELS, type Item, type ItemStatus, type RevisionComment } from "@/api/types"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { ConfirmDialog } from "@/components/ui/confirm-dialog"
import { Label } from "@/components/ui/label"
import { Textarea } from "@/components/ui/textarea"

const NEXT_STATUSES: Record<ItemStatus, ItemStatus[]> = {
  w_pracy: ["sprawdzany"],
  sprawdzany: ["w_pracy", "wydany"],
  wydany: ["w_pracy"],
}

const BADGE_VARIANT: Record<ItemStatus, "secondary" | "outline" | "default"> = {
  w_pracy: "secondary",
  sprawdzany: "outline",
  wydany: "default",
}

function StatusControl({
  item,
  onChanged,
}: {
  item: Item
  onChanged: () => void | Promise<void>
}) {
  const status = item.status ?? "w_pracy"
  const [pending, setPending] = useState<ItemStatus | null>(null)
  const [comment, setComment] = useState("")
  const [revisions, setRevisions] = useState<RevisionComment[]>([])

  useEffect(() => {
    api.getRevisionComments(item.id).then(setRevisions).catch(() => setRevisions([]))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [item.id, item.revisionNumber])

  async function confirmChange() {
    if (!pending) return
    await api.setStatus(item.id, pending, isRevisionBump ? comment.trim() : undefined)
    setPending(null)
    setComment("")
    await onChanged()
  }

  const isRevisionBump = status === "wydany" && pending === "w_pracy"
  const current = item.revisionNumber ?? 1

  return (
    <div className="flex flex-wrap items-center gap-2">
      <Badge variant={BADGE_VARIANT[status]}>{STATUS_LABELS[status]}</Badge>
      {item.revisionNumber !== null && (
        <span className="text-[12.5px] text-muted-foreground">rev. {revisionLabel(item.revisionNumber)}</span>
      )}
      <div className="flex gap-1.5">
        {NEXT_STATUSES[status].map((next) => (
          <Button
            key={next}
            size="sm"
            variant="outline"
            onClick={() => {
              setComment("")
              setPending(next)
            }}
          >
            → {STATUS_LABELS[next]}
          </Button>
        ))}
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
          title="Zmiana statusu"
          description={
            isRevisionBump ? (
              <div className="flex flex-col gap-2">
                <p>
                  {`Zmiana statusu z "Wydany" na "W pracy" podniesie rewizję z ${revisionLabel(current)} na ${revisionLabel(current + 1)}.`}
                </p>
                <div className="flex flex-col gap-1">
                  <Label htmlFor="revision-comment">Komentarz do rewizji (opcjonalnie)</Label>
                  <Textarea
                    id="revision-comment"
                    value={comment}
                    onChange={(e) => setComment(e.target.value)}
                    rows={3}
                    placeholder="Co zmieniło się w tej rewizji…"
                  />
                </div>
              </div>
            ) : (
              `Zmienić status z "${STATUS_LABELS[status]}" na "${STATUS_LABELS[pending]}"?`
            )
          }
          confirmLabel="Zmień status"
          onConfirm={confirmChange}
          onCancel={() => {
            setPending(null)
            setComment("")
          }}
        />
      )}
    </div>
  )
}

export { StatusControl }
