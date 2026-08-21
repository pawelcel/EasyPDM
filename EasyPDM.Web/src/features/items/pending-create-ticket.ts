import { useSyncExternalStore } from "react"

// Makro CAD (EasyPDMUpload.FCMacro ALBO EasyPDMDownload.FCMacro) otwiera przeglądarkę na
// "?ticket=...&mode=create|download&name=..." zamiast pokazywać własny formularz/wyszukiwarkę
// — CELOWO bez projektu/rodzica/typu/wyboru elementu w URL-u: to PendingTicketBanner (widoczny
// na każdym ekranie, dopóki bilet czeka) pokazuje jawny wybór "Nowy element" / "Dodaj do
// istniejącego" (tryb "create") albo od razu wyszukiwarkę (tryb "download"). "Nowy element"
// otwiera samowystarczalny popup (AddNodeDialog bez z góry ustalonego projektu — sam pyta o
// projekt/rodzica) — bilet jest przekazywany do niego JAWNIE jako prop, nigdy nie jest
// dołączany "przy okazji" do jakiegokolwiek innego, niezwiązanego dodawania w aplikacji.
// Stan modułowy (zamiast propsów) tylko po to, żeby pasek w nagłówku (App.tsx) mógł się
// dowiedzieć o bilecie bez rodzica-do-dziecka przekazywania przez całe drzewo widoków.

type PendingTicketMode = "create" | "download"

type PendingTicket = {
  ticket: string
  // "create" (domyślny, brak parametru = wsteczna zgodność z istniejącymi linkami) — z
  // EasyPDMUpload.FCMacro. "download" — z EasyPDMDownload.FCMacro: banner tylko wskazuje
  // element do pobrania, bez opcji tworzenia nowego.
  mode: PendingTicketMode
  name?: string
  // Numer elementu, który wygląda jak już wysłany przez to samo makro wcześniej (etykieta
  // dokumentu pasuje do "numer (nazwa).REWIZJA") — tylko PODPOWIEDŹ do wyboru w
  // PendingTicketBanner przy dogrywaniu do istniejącego, wybór zawsze można zmienić.
  suggestedItemNumber?: number
} | null

function readFromUrl(): PendingTicket {
  const params = new URLSearchParams(window.location.search)
  const ticket = params.get("ticket")
  if (!ticket) return null
  const mode = params.get("mode") === "download" ? "download" : "create"
  const name = params.get("name") ?? undefined
  const suggestedItemNumberRaw = params.get("suggestedItemNumber")
  const suggestedItemNumber = suggestedItemNumberRaw ? Number(suggestedItemNumberRaw) : undefined
  window.history.replaceState(null, "", window.location.pathname)
  return {
    ticket,
    mode,
    name,
    suggestedItemNumber: Number.isFinite(suggestedItemNumber) ? suggestedItemNumber : undefined,
  }
}

// Czytane RAZ, przy pierwszym imporcie tego modułu (a więc raz na wczytanie strony) —
// każdy kolejny import w ramach tej samej sesji JS dostaje ten sam, już zainicjalizowany
// moduł (semantyka singletona modułów ES).
let current: PendingTicket = readFromUrl()
const listeners = new Set<() => void>()

function getSnapshot(): PendingTicket {
  return current
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

// Wołane PO udanym utworzeniu (AddNodeDialog) albo wskazaniu istniejącego/do pobrania
// elementu (PendingTicketBanner) — bilet znika z paska, cała operacja jest zamknięta.
function clearPendingCreateTicket() {
  if (current === null) return
  current = null
  listeners.forEach((listener) => listener())
}

function usePendingCreateTicket(): PendingTicket {
  return useSyncExternalStore(subscribe, getSnapshot)
}

export { clearPendingCreateTicket, usePendingCreateTicket }
