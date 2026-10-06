// Bieg makra właśnie się skończył. Panel postępu to wie natychmiast (odpytuje co 1,5 s),
// a dzwonek dopiero po swoich 30 s — a to na koniec biegu ląduje raport, na który człowiek
// właśnie czeka. Jedno zdarzenie na oknie zamiast wspólnego kontekstu: oba komponenty
// siedzą w zupełnie innych miejscach drzewa i poza tą jedną chwilą nie mają ze sobą nic
// wspólnego.
export const TRANSFER_FINISHED_EVENT = "pdm:transfer-finished"

export function announceTransferFinished() {
  window.dispatchEvent(new Event(TRANSFER_FINISHED_EVENT))
}
