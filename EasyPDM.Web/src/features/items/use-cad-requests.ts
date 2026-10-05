import { useEffect, useRef } from "react"

import { api } from "@/api/client"
import { acceptPendingCreateTicket, currentRunId } from "@/features/items/pending-create-ticket"

// Podejmowanie próśb, które makro CAD zostawia na serwerze zamiast otwierać nową kartę.
//
// Po co: wysyłając złożenie, makro potrzebuje formularza dla każdego nowego komponentu.
// Dotąd otwierało na to osobną kartę, a przed każdą musiało pokazać natywne okno "OK" —
// nie dla potwierdzenia, lecz dlatego, że Windows przepuszcza przejęcie fokusu tylko
// PIERWSZEMU programowemu otwarciu przeglądarki w danym biegu, a kolejne otwierają się po
// cichu w tle. Przy złożeniu na kilkadziesiąt części to kilkadziesiąt kliknięć i tyleż kart.
//
// Ta karta już jest otwarta i ma fokus, więc nie trzeba otwierać żadnej nowej: odpytujemy
// serwer i gdy czeka prośba, pokazujemy ten sam pasek i ten sam formularz co dotąd.
//
// Odpytywanie jest szybsze niż u powiadomień, bo po drugiej stronie stoi człowiek czekający
// na formularz — ale wolniejsze niż pasek postępu, bo prośba pojawia się raz na komponent,
// a nie co plik. Koszt to odczyt ze słownika w pamięci, bez dotykania bazy.
const POLL_INTERVAL_MS = 2_000

export function useCadRequests() {
  // Bilety, które ta karta już podjęła — bez tego ta sama prośba byłaby podejmowana przy
  // każdym odpytaniu, dopóki użytkownik nie wypełni formularza.
  const handled = useRef<Set<string>>(new Set())

  useEffect(() => {
    let cancelled = false

    async function tick() {
      try {
        const result = await api.getCadRequest()
        const request = result?.request
        if (cancelled || !request || handled.current.has(request.ticket)) return

        // Podejmujemy WYŁĄCZNIE prośby z tego samego biegu makra, który otworzył tę kartę.
        // Inaczej karta zalogowana tym samym kontem na innym komputerze przechwytywałaby
        // formularze z cudzej wysyłki — zdarzyło się w praktyce.
        const mine = currentRunId()
        if (!mine || request.runId !== mine) return

        handled.current.add(request.ticket)
        // Najpierw bierzemy na siebie, potem pokazujemy. Odwrotna kolejność znaczyłaby, że
        // przy nieudanym "take" użytkownik widzi formularz, a makro i tak otwiera kartę —
        // czyli dwa formularze na jeden komponent.
        await api.takeCadRequest(request.ticket)
        if (cancelled) return

        const accepted = acceptPendingCreateTicket({
          ticket: request.ticket,
          mode: request.mode === "download" ? "download" : "create",
          name: request.name ?? undefined,
          cadItemType: request.itemType === "assembly" || request.itemType === "part" ? request.itemType : undefined,
          material: request.material ?? undefined,
          documentSize: request.documentSize ?? undefined,
          suggestedItemNumber: request.suggestedItemNumber ?? undefined,
        })
        // Nie weszło, bo inny bilet jest w toku — zapominamy, że ją widzieliśmy, żeby
        // podjąć ją ponownie, gdy formularz się zwolni.
        if (!accepted) handled.current.delete(request.ticket)
      } catch {
        // Cicho: to jest wygoda, nie część wysyłki. Gdy serwer nie odpowiada, makro po
        // swoim czasie oczekiwania i tak wróci do otwierania karty.
      }
    }

    tick()
    const id = setInterval(tick, POLL_INTERVAL_MS)
    return () => {
      cancelled = true
      clearInterval(id)
    }
  }, [])
}
