import { useEffect, useState } from "react"

import { api } from "@/api/client"
import { revisionLabel, STATUS_LABEL_KEYS, type HistoryEntry, type ItemStatus } from "@/api/types"
import { Hint } from "@/components/ui/hint"
import { SectionLabel } from "@/components/ui/section-label"
import { useLanguage } from "@/i18n/use-language"

// Historia Części/Złożenia — połączona chronologicznie z kilku źródeł po stronie backendu
// (utworzenie, zmiany statusu, rewizje z komentarzem, dodanie/usunięcie załącznika,
// zablokowanie/zwolnienie właściciela). Tylko do odczytu — nie da się tu niczego edytować
// ani usunąć.
function ItemHistoryPanel({
  itemId,
  refreshSignal,
}: {
  itemId: string
  // Rośnie za każdym razem, gdy rodzic wykona akcję mogącą dopisać nowy wpis do historii
  // (status, blokada/zwolnienie, właściwości, załączniki) — sam "itemId" się wtedy nie
  // zmienia, więc bez tego panel nie wiedziałby, kiedy doczytać świeże dane.
  refreshSignal?: number
}) {
  const { t } = useLanguage()
  const [entries, setEntries] = useState<HistoryEntry[]>([])

  useEffect(() => {
    // Dwie akcje wykonane szybko po sobie (np. blokada, zaraz potem zmiana właściwości)
    // odpalają dwa równoległe GET-y — bez tego flagą starsze (wolniejsze) żądanie mogłoby
    // odpowiedzieć PO nowszym i nadpisać świeże dane nieaktualną listą.
    let cancelled = false
    api
      .getItemHistory(itemId)
      .then((result) => {
        if (!cancelled) setEntries(result)
      })
      .catch(() => {
        if (!cancelled) setEntries([])
      })
    return () => {
      cancelled = true
    }
  }, [itemId, refreshSignal])

  function statusLabel(status: ItemStatus | null): string {
    return status ? t(STATUS_LABEL_KEYS[status]) : "—"
  }

  function describe(entry: HistoryEntry): string {
    switch (entry.type) {
      case "created":
        return t("history.created")
      case "status":
        return t("history.statusChange", {
          from: statusLabel(entry.fromStatus),
          to: statusLabel(entry.toStatus),
        })
      case "revision": {
        const label = entry.revisionNumber !== null ? revisionLabel(entry.revisionNumber) : "?"
        return entry.comment
          ? t("history.revisionWithComment", { label, comment: entry.comment })
          : t("history.revision", { label })
      }
      case "attachment_added":
        return t("history.attachmentAdded", { fileName: entry.fileName ?? "" })
      case "attachment_removed":
        return t("history.attachmentRemoved", { fileName: entry.fileName ?? "" })
      case "owner_locked":
        return t("history.ownerLocked")
      case "owner_released":
        return t("history.ownerReleased")
    }
  }

  return (
    <>
      <SectionLabel>{t("history.title")}</SectionLabel>
      {entries.length === 0 ? (
        <Hint>{t("history.empty")}</Hint>
      ) : (
        // Wpisów historii z czasem przybywa dużo (każda zmiana statusu/właściwości/
        // załącznika dopisuje kolejny) — bez ograniczenia wysokości lista rozpychała panel
        // i robiła się nieczytelna. max-h ~5 wierszy + przewijanie w środku; entries już
        // przychodzi z backendu najnowsze-pierwsze (ORDER BY at DESC), więc 5 widocznych
        // bez przewijania to zawsze 5 najnowszych.
        <ul className="flex max-h-[7.5rem] flex-col gap-1 overflow-y-auto">
          {entries.map((entry, index) => (
            <li key={index} className="text-[12.5px]">
              <span className="text-muted-foreground">
                {new Date(entry.at).toLocaleString("pl-PL")}
              </span>
              {" — "}
              {describe(entry)}
              {entry.userDisplayName && (
                <span className="text-muted-foreground"> ({entry.userDisplayName})</span>
              )}
            </li>
          ))}
        </ul>
      )}
    </>
  )
}

export { ItemHistoryPanel }
