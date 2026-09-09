import { useSyncExternalStore } from "react"

// Makro SolidWorks (EasyPDMUpload.bas) otwiera przeglądarkę na
// "?drawingTicket=...&candidates=id1,id2,..." gdy wgrywa rysunek (.SLDDRW) i dopasowanie po
// drzewie widoków znalazło KILKA różnych elementów, do których mógłby pasować (np. widok
// złożenia obok widoku szczegółowego pojedynczej części) — nie potrafi samo rozstrzygnąć, więc
// przekazuje decyzję tutaj zamiast zgadywać. Ten sam wzorzec modułowego stanu co
// pending-create-ticket.ts, osobny moduł (nie trzeci tryb tamtego) bo kształt UI jest inny:
// znany z góry, krótki zbiór kandydatów, żadnego wyszukiwania.

type PendingDrawingTicket = {
  ticket: string
  candidateItemIds: string[]
} | null

function readFromUrl(): PendingDrawingTicket {
  const params = new URLSearchParams(window.location.search)
  const ticket = params.get("drawingTicket")
  if (!ticket) return null
  const candidatesRaw = params.get("candidates") ?? ""
  const candidateItemIds = candidatesRaw.split(",").filter((id) => id.length > 0)
  window.history.replaceState(null, "", window.location.pathname)
  if (candidateItemIds.length === 0) return null
  return { ticket, candidateItemIds }
}

// Czytane RAZ, przy pierwszym imporcie tego modułu -- ta sama semantyka singletona modułów ES
// co pending-create-ticket.ts.
let current: PendingDrawingTicket = readFromUrl()
const listeners = new Set<() => void>()

function getSnapshot(): PendingDrawingTicket {
  return current
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

// Wołane po udanym zgłoszeniu wyboru (PendingDrawingTicketBanner) -- bilet znika z ekranu.
function clearPendingDrawingTicket() {
  if (current === null) return
  current = null
  listeners.forEach((listener) => listener())
}

function usePendingDrawingTicket(): PendingDrawingTicket {
  return useSyncExternalStore(subscribe, getSnapshot)
}

export { clearPendingDrawingTicket, usePendingDrawingTicket }
