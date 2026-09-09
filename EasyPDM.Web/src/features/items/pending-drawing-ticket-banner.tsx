import { useEffect, useState } from "react"

import { api, ApiError } from "@/api/client"
import { itemDisplayLabel, type Item } from "@/api/types"
import { Button } from "@/components/ui/button"
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { FormError } from "@/components/ui/form-error"
import { Hint } from "@/components/ui/hint"
import { clearPendingDrawingTicket, usePendingDrawingTicket } from "@/features/items/pending-drawing-ticket"
import { useLanguage } from "@/i18n/use-language"

// Popup widoczny na każdym ekranie, dopóki czeka bilet "wybór elementu dla rysunku
// SolidWorks" (zob. pending-drawing-ticket.ts) -- w odróżnieniu od PendingTicketBanner
// (nowy element / dogranie po wyszukiwaniu) tu kandydatów jest z góry znana, krótka lista
// (id-y wprost z URL-a), więc zamiast wyszukiwarki wystarczy prosta lista do zaznaczenia.
//
// W ODRÓŻNIENIU od PendingTicketBanner (celowo nieodrzucalny, bo tam ZAWSZE da się coś
// wybrać/utworzyć) TUTAJ jest przycisk Anuluj -- kandydatów może się okazać zero (np. id-y z
// URL-a już nie istnieją) albo żaden z pokazanych nie jest tym właściwym, więc musi być
// jakieś wyjście z okna. Anuluj NIE woła żadnego API -- po prostu zamyka popup lokalnie;
// makro po drugiej stronie samo w końcu przestanie czekać (WaitForTicket ma własny timeout
// i Escape), dokładnie tak samo jak przy zwykłym anulowaniu tworzenia nowego elementu.
function PendingDrawingTicketBanner() {
  const { t } = useLanguage()
  const pendingTicket = usePendingDrawingTicket()
  const [candidates, setCandidates] = useState<Item[]>([])
  const [loading, setLoading] = useState(true)
  const [itemId, setItemId] = useState("")
  const [exportPdf, setExportPdf] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState("")

  useEffect(() => {
    if (!pendingTicket) return
    setLoading(true)
    setError("")
    Promise.all(
      pendingTicket.candidateItemIds.map((id) => api.getItem(id).catch(() => null))
    ).then((results) => {
      const found = results.filter((item): item is Item => item !== null)
      setCandidates(found)
      setItemId(found[0]?.id ?? "")
      setLoading(false)
    })
    setExportPdf(false)
  }, [pendingTicket])

  if (!pendingTicket) return null

  async function confirmResolve() {
    if (!itemId) return
    setSubmitting(true)
    setError("")
    try {
      await api.resolveDrawingTicket(pendingTicket!.ticket, itemId, exportPdf)
      clearPendingDrawingTicket()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t("app.pendingDrawingTicketFailed"))
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Dialog open>
      <DialogContent showCloseButton={false}>
        <DialogHeader>
          <DialogTitle>{t("app.pendingDrawingTicketTitle")}</DialogTitle>
        </DialogHeader>

        {loading ? (
          <Hint>{t("common.loading")}</Hint>
        ) : candidates.length === 0 ? (
          <Hint>{t("app.pendingDrawingTicketNoCandidates")}</Hint>
        ) : (
          <div className="flex flex-col gap-2">
            {/* Ten podpis ma sens TYLKO obok faktycznej listy do wyboru -- osobno, nad
                stanem ładowania albo komunikatem "nic nie znaleziono", wyglądał jak
                sprzeczność ("znalazło kilka" tuż nad "nie znaleziono żadnego"). */}
            <Hint>{t("app.pendingDrawingTicketHint")}</Hint>
            <div className="flex flex-col gap-1.5">
              {candidates.map((c) => (
                <label key={c.id} className="flex cursor-pointer items-center gap-2 text-sm">
                  <input
                    type="radio"
                    name="drawing-ticket-item"
                    checked={itemId === c.id}
                    onChange={() => setItemId(c.id)}
                    className="size-3.5 shrink-0 accent-primary"
                  />
                  {itemDisplayLabel(c)}
                </label>
              ))}
            </div>

            <label className="flex cursor-pointer items-center gap-2 text-sm">
              <input
                type="checkbox"
                checked={exportPdf}
                onChange={(e) => setExportPdf(e.target.checked)}
                className="size-3.5 shrink-0 accent-primary"
              />
              {t("addNode.exportPdfOptional")}
            </label>

            <FormError>{error}</FormError>
          </div>
        )}

        <DialogFooter>
          <Button variant="outline" onClick={clearPendingDrawingTicket} disabled={submitting}>
            {t("common.cancel")}
          </Button>
          <Button onClick={confirmResolve} disabled={!itemId || submitting || loading}>
            {submitting ? t("common.saving") : t("common.ok")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export { PendingDrawingTicketBanner }
