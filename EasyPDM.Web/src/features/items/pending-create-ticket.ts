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
  // Typ dokumentu rozpoznany przez makro z samego pliku (.SLDASM/.iam/złożenie FreeCAD ->
  // "assembly", reszta -> "part"). Wstępnie zaznacza przycisk w oknie dodawania.
  //
  // Bez tego okno startowało bez żadnego wyboru i wystarczyło kliknąć "Część", żeby złożenie
  // powstało jako Część — a do Części nie da się nic podpiąć w strukturze (IsChildTypeAllowed),
  // więc makro zaraz potem dostawało 400 i cała struktura BOM nie powstawała. Zgłoszone z
  // praktyki: "nie zbudował struktury, tylko wszystko zapisał osobno".
  cadItemType?: "part" | "assembly"
  // Materiał odczytany z dokumentu CAD, podstawiany jako wartość pola Materiał w oknie
  // dodawania (tylko do odczytu — zob. MaterialField). Bez tego pole startuje puste, nie
  // wiadomo co wpisać, a po wysyłce materiał z CAD-a i tak się pojawia — jakby znikąd.
  material?: string
  // Rozmiar dokumentu CAD w bajtach, odczytany przez makro z pliku na dysku. Służy TYLKO do
  // ostrzeżenia przy opcji eksportu STEP: dla dużych modeli ten eksport robi sam CAD i potrafi
  // trwać bardzo długo, a użytkownik nie ma o tym skąd wiedzieć, zanim zatwierdzi okno.
  // Serwer tej liczby nie zna -- w tym momencie nic jeszcze nie zostało zapisane ani wysłane.
  documentSize?: number
} | null

function readFromUrl(): PendingTicket {
  const params = new URLSearchParams(window.location.search)
  const ticket = params.get("ticket")
  if (!ticket) return null
  const mode = params.get("mode") === "download" ? "download" : "create"
  const name = params.get("name") ?? undefined
  const suggestedItemNumberRaw = params.get("suggestedItemNumber")
  const suggestedItemNumber = suggestedItemNumberRaw ? Number(suggestedItemNumberRaw) : undefined
  // Identyfikator biegu makra. Zapamiętujemy go dla TEJ karty: dalsze komponenty tego samego
  // biegu makro zostawia na serwerze, a karta podejmuje wyłącznie prośby ze swoim runId.
  //
  // Bez tego wystarczyło, że to samo konto było zalogowane w przeglądarce na DRUGIM
  // komputerze — prośba z komputera A trafiała do karty na komputerze B i pokazywała formularz
  // komuś zupełnie innemu (zgłoszone z praktyki).
  const runId = params.get("runId")
  if (runId) rememberRunId(runId)
  const cadItemTypeRaw = params.get("itemType")
  const cadItemType = cadItemTypeRaw === "assembly" || cadItemTypeRaw === "part" ? cadItemTypeRaw : undefined
  const material = params.get("material") ?? undefined
  const documentSizeRaw = params.get("documentSize")
  const documentSize = documentSizeRaw ? Number(documentSizeRaw) : undefined
  window.history.replaceState(null, "", window.location.pathname)
  return {
    ticket,
    mode,
    name,
    suggestedItemNumber: Number.isFinite(suggestedItemNumber) ? suggestedItemNumber : undefined,
    cadItemType,
    material: material || undefined,
    documentSize: Number.isFinite(documentSize) && documentSize! > 0 ? documentSize : undefined,
  }
}

// Czytane RAZ, przy pierwszym imporcie tego modułu (a więc raz na wczytanie strony) —
// każdy kolejny import w ramach tej samej sesji JS dostaje ten sam, już zainicjalizowany
// moduł (semantyka singletona modułów ES).
// Przeżywa odświeżenie strony (sessionStorage), ale NIE przenosi się na inną kartę ani na
// inny komputer — dokładnie tego tu potrzeba. Każdy dostęp w try/catch: w trybie prywatnym
// albo przy zablokowanych danych witryny sessionStorage potrafi rzucić wyjątkiem.
const RUN_ID_KEY = "pdm_cad_run_id"

function rememberRunId(runId: string) {
  try {
    sessionStorage.setItem(RUN_ID_KEY, runId)
  } catch {
    // Brak pamięci sesji to nie błąd — po prostu ta karta nie podejmie dalszych próśb i
    // makro wróci do otwierania nowej na każdy komponent, czyli do zachowania sprzed zmiany.
  }
}

function currentRunId(): string | null {
  try {
    return sessionStorage.getItem(RUN_ID_KEY)
  } catch {
    return null
  }
}

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

// Drugi sposób, w jaki bilet może tu trafić: makro zostawia prośbę NA SERWERZE, a ta karta
// — już otwarta i mająca fokus — sama ją podejmuje (zob. CadRequestStore.cs i
// use-cad-requests.ts). Pierwszy sposób, czyli odczyt z adresu URL, wymaga nowej karty na
// każdy komponent, a każda kolejna karta otwiera się w tle, bo Windows blokuje przejmowanie
// fokusu — dlatego dotąd przed każdą trzeba było kliknąć "OK".
//
// Prośba z serwera jest ignorowana, gdy jakiś bilet już czeka: użytkownik ma przed sobą
// jeden formularz na raz, a makro i tak zgłasza komponenty pojedynczo.
function acceptPendingCreateTicket(next: NonNullable<PendingTicket>): boolean {
  if (current !== null) return false
  current = next
  listeners.forEach((listener) => listener())
  return true
}

function usePendingCreateTicket(): PendingTicket {
  return useSyncExternalStore(subscribe, getSnapshot)
}

export { acceptPendingCreateTicket, clearPendingCreateTicket, currentRunId, usePendingCreateTicket }
export type { PendingTicket }
