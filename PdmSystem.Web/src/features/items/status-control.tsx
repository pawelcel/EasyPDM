import { useState } from "react"

import { api } from "@/api/client"
import { STATUS_LABELS, type Item, type ItemStatus } from "@/api/types"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { ConfirmDialog } from "@/components/ui/confirm-dialog"

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

  async function confirmChange() {
    if (!pending) return
    await api.setStatus(item.id, pending)
    setPending(null)
    await onChanged()
  }

  const isRevisionBump = status === "wydany" && pending === "w_pracy"
  const current = item.revisionNumber ?? 1

  return (
    <div className="flex flex-wrap items-center gap-2">
      <Badge variant={BADGE_VARIANT[status]}>{STATUS_LABELS[status]}</Badge>
      {item.revisionNumber !== null && (
        <span className="text-[12.5px] text-muted-foreground">rev. {item.revisionNumber}</span>
      )}
      <div className="flex gap-1.5">
        {NEXT_STATUSES[status].map((next) => (
          <Button key={next} size="sm" variant="outline" onClick={() => setPending(next)}>
            → {STATUS_LABELS[next]}
          </Button>
        ))}
      </div>

      {pending && (
        <ConfirmDialog
          open
          title="Zmiana statusu"
          description={
            isRevisionBump
              ? `Zmiana statusu z "Wydany" na "W pracy" podniesie numer rewizji z ${current} na ${current + 1}.`
              : `Zmienić status z "${STATUS_LABELS[status]}" na "${STATUS_LABELS[pending]}"?`
          }
          confirmLabel="Zmień status"
          onConfirm={confirmChange}
          onCancel={() => setPending(null)}
        />
      )}
    </div>
  )
}

export { StatusControl }
