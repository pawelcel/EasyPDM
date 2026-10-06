import { useCallback, useEffect, useRef, useState } from "react"
import { Check, ChevronRight, Loader2, X } from "lucide-react"

import { api } from "@/api/client"
import type { TransferProgress } from "@/api/types"
import { announceTransferFinished } from "@/features/transfer/transfer-finished-event"
import { Button } from "@/components/ui/button"
import { useLanguage } from "@/i18n/use-language"

// Stały, krótki okres odpytywania. Pierwsze podejście dobierało go dynamicznie (1 s w
// trakcie biegu, rzadziej w spoczynku) przez setTimeout planujący sam siebie — i okazało się
// kruche: pomiar na żywo pokazał JEDNO odpytanie na 9 sekund zamiast czterech, bo wystarczy
// jedno przemontowanie komponentu, żeby łańcuch się urwał i nigdy nie wznowił. setInterval
// nie ma tej właściwości: tyka niezależnie od tego, co się dzieje z zawartością.
//
// To ten sam wzorzec, co w use-notifications.ts, tylko znacznie częstszy — bo tam chodzi o
// dzwonek, a tu o pasek postępu, który ma się odhaczać na oczach użytkownika. Koszt jednego
// odpytania to odczyt ze słownika w pamięci i kilkadziesiąt bajtów odpowiedzi, bez dotykania
// bazy, więc nawet kilkanaście otwartych kart nic nie kosztuje.
const POLL_INTERVAL_MS = 1_500
// Po zakończeniu lista zostaje tylko na moment z kompletem ptaszków — tyle, żeby dało się
// zobaczyć, że domknęła się w całości. Potem znika sama, bez klikania.
//
// Było 20 s i to było za długo (zgłoszone z praktyki: „znika na końcu, ale długo"). Tyle
// trzeba było czekać, zanim panel przestał zasłaniać róg aplikacji, mimo że nic się już nie
// działo. Krótko można, odkąd trwałym śladem po biegu jest raport w powiadomieniach: lista
// nie musi już być jedynym miejscem, w którym widać, co poszło.
const KEEP_FINISHED_MS = 3_000

function useTransferProgress() {
  const [progress, setProgress] = useState<TransferProgress | null>(null)

  const refetch = useCallback(async () => {
    try {
      const result = await api.getTransferProgress()
      setProgress(result?.progress ?? null)
    } catch {
      // Cicho: panel postępu nie ma jak pokazać własnej awarii i nie jest wart przerywania
      // czegokolwiek. Zostaje przy ostatnim znanym stanie do kolejnego udanego odpytania.
    }
  }, [])

  useEffect(() => {
    refetch()
    const id = setInterval(refetch, POLL_INTERVAL_MS)
    return () => clearInterval(id)
  }, [refetch])

  return { progress, refetch }
}

function StatusIcon({ status }: { status: TransferProgress["entries"][number]["status"] }) {
  if (status === "done")
    return <Check className="size-4 shrink-0 text-[#49c17d]" strokeWidth={3} />
  if (status === "skipped")
    return <Check className="size-4 shrink-0 text-muted-foreground" strokeWidth={3} />
  if (status === "failed")
    return <X className="size-4 shrink-0 text-destructive" strokeWidth={3} />
  if (status === "active")
    return <Loader2 className="size-4 shrink-0 animate-spin text-primary" />
  return <ChevronRight className="size-4 shrink-0 text-muted-foreground/40" />
}

// Lista plików, które makro CAD ma przesłać, odhaczana w trakcie pracy. Przyklejona do
// prawej krawędzi, bo jest towarzyszem tego, co dzieje się w CAD-zie, a nie częścią widoku,
// na którym użytkownik akurat stoi — ma być widoczna niezależnie od tego, czy patrzy na
// drzewo projektu, czy na panel elementu.
function TransferProgressPanel() {
  const { t } = useLanguage()
  const { progress, refetch } = useTransferProgress()
  const [dismissed, setDismissed] = useState(false)
  const activeRef = useRef<HTMLLIElement | null>(null)

  // Nowy bieg kasuje wcześniejsze zamknięcie panelu ręką — inaczej raz zamknięty panel
  // nie pokazałby się już przy następnej wysyłce.
  const startedAt = progress?.startedAt
  useEffect(() => setDismissed(false), [startedAt])

  // Zakończona lista znika sama po chwili. Przy okazji budzimy dzwonek: na koniec biegu
  // serwer zapisuje raport jako powiadomienie, a czekanie na kolejne odpytanie dzwonka
  // (30 s) znaczyłoby, że raport pojawia się długo po tym, jak lista zniknęła z ekranu.
  useEffect(() => {
    if (!progress?.finished) return
    announceTransferFinished()
    const id = window.setTimeout(() => setDismissed(true), KEEP_FINISHED_MS)
    return () => window.clearTimeout(id)
  }, [progress?.finished, startedAt])

  // Aktywna pozycja jest przewijana do widoku sama. Przy 80 plikach po kilkunastu
  // odhaczeniach wyjechalaby poza kadr i patrzyloby sie na odhaczony poczatek listy zamiast
  // na to, co trwa. "nearest" zamiast "center": gdy lista i tak miesci sie w calosci,
  // wysrodkowywanie szarpaloby panelem bez powodu.
  const activeKey = progress?.entries.find((e) => e.status === "active")?.key
  useEffect(() => {
    activeRef.current?.scrollIntoView({ block: "nearest", behavior: "smooth" })
  }, [activeKey])

  if (!progress || dismissed) return null

  const failed = progress.entries.filter((e) => e.status === "failed").length
  const pct = progress.total > 0 ? Math.round((progress.done / progress.total) * 100) : 0

  async function close() {
    setDismissed(true)
    try {
      await api.dismissTransferProgress()
      await refetch()
    } catch {
      // Zamknięcie jest lokalne i tak — wpis i tak wygaśnie sam po stronie serwera.
    }
  }

  // z-[60], czyli PONAD oknami dialogowymi (Dialog i jego przyciemnienie siedzą na z-50).
  // Przy remisie wygrywa to, co renderuje się później w DOM, a dialogi idą przez portal — panel
  // lądował więc pod ich przyciemnieniem i pod backdrop-blur, czyli był nieczytelny dokładnie
  // wtedy, kiedy jest najbardziej potrzebny: makro wysyła złożenie, co komponent otwiera okno
  // "Żądanie z makra CAD", a użytkownik chce w tej chwili widzieć, ile jeszcze zostało.
  return (
    <div className="fixed right-4 top-20 z-[60] w-[22rem] overflow-hidden rounded-xl border border-border bg-card shadow-xl">
      <div className="flex items-center justify-between gap-2 border-b border-border px-4 py-3">
        <div className="min-w-0">
          <div className="truncate text-sm font-semibold">
            {t(progress.kind === "upload" ? "transfer.uploading" : "transfer.downloading")}
          </div>
          <div className="text-xs text-muted-foreground">
            {t("transfer.counter", { done: progress.done, total: progress.total })}
            {failed > 0 && <span className="text-destructive"> · {t("transfer.failed", { count: failed })}</span>}
          </div>
        </div>
        <Button size="sm" variant="ghost" onClick={close} aria-label={t("common.close")}>
          <X className="size-4" />
        </Button>
      </div>

      <div className="h-1 w-full bg-muted">
        <div
          className={`h-full transition-[width] duration-300 ${failed > 0 ? "bg-destructive" : "bg-primary"}`}
          style={{ width: `${pct}%` }}
        />
      </div>

      {/* Lista bywa długa (złożenie potrafi mieć kilkadziesiąt komponentów), więc własne
          przewijanie zamiast rozpychania panelu na całą wysokość ekranu. */}
      <ul className="max-h-[22rem] overflow-y-auto px-2 py-2">
        {progress.entries.map((entry) => (
          <li
            key={entry.key}
            ref={entry.status === "active" ? activeRef : undefined}
            className={`flex items-center gap-2 rounded-md px-2 py-1.5 text-[13px] ${
              entry.status === "active" ? "bg-primary/10" : ""
            }`}
          >
            <StatusIcon status={entry.status} />
            <span
              className={`truncate ${
                entry.status === "done"
                  ? "text-muted-foreground line-through decoration-muted-foreground/40"
                  : entry.status === "skipped"
                    ? "text-muted-foreground/70"
                    : entry.status === "failed"
                      ? "text-destructive"
                      : ""
              }`}
              title={entry.label}
            >
              {entry.label}
            </span>
          </li>
        ))}
      </ul>
    </div>
  )
}

export { TransferProgressPanel }
