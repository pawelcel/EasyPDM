import { ArrowUpRight } from "lucide-react"

import { itemDisplayLabel, revisionLabel, type ClientVerificationSummary } from "@/api/types"
import { Button } from "@/components/ui/button"
import { Hint } from "@/components/ui/hint"
import { SectionLabel } from "@/components/ui/section-label"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { useLanguage } from "@/i18n/use-language"

// Jedna tabela = jeden wynik weryfikacji. Trzy osobne zestawienia zamiast jednej listy z
// kolumną "wynik": przy przeglądzie projektu pytanie brzmi "co jeszcze wisi u klienta"
// albo "co wróciło do poprawy", a nie "jaki wynik ma ta konkretna część" — na to ostatnie
// odpowiada już znacznik w drzewku.
function VerificationTable({
  label,
  rows,
  emptyHint,
  onSelectItem,
}: {
  label: string
  rows: ClientVerificationSummary[]
  emptyHint: string
  onSelectItem?: (itemId: string) => void
}) {
  const { t } = useLanguage()

  return (
    <div className="mt-2">
      <SectionLabel>
        {label} ({rows.length})
      </SectionLabel>
      {rows.length === 0 ? (
        <Hint>{emptyHint}</Hint>
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>{t("common.name")}</TableHead>
              <TableHead className="w-28">{t("clientVerification.entryRevision")}</TableHead>
              <TableHead className="w-36">{t("clientVerification.entryDate")}</TableHead>
              <TableHead className="w-12" />
            </TableRow>
          </TableHeader>
          <TableBody>
            {rows.map((row) => {
              // Wpis dotyczy STARSZEJ rewizji niż ta, którą element ma teraz — wynik jest, ale
              // nie mówi nic o aktualnej wersji, więc rewizja dostaje ostrzegawczy kolor.
              const stale = row.revisionNumber !== row.itemRevisionNumber
              return (
                <TableRow key={row.itemId}>
                  <TableCell className="truncate">
                    {itemDisplayLabel({
                      fileName: row.fileName,
                      itemNumber: row.itemNumber,
                      itemNumberPrefix: row.itemNumberPrefix,
                      itemNumberLabel: row.itemNumberLabel,
                      recordName: row.recordName,
                    })}
                  </TableCell>
                  <TableCell className={stale ? "text-destructive" : undefined}>
                    {row.revisionNumber !== null
                      ? `${t("item.revisionShort")} ${revisionLabel(row.revisionNumber)}`
                      : "—"}
                    {stale && row.itemRevisionNumber !== null && (
                      <span className="text-muted-foreground">
                        {" → "}
                        {revisionLabel(row.itemRevisionNumber)}
                      </span>
                    )}
                  </TableCell>
                  <TableCell className="text-muted-foreground">
                    {new Date(row.createdAt).toLocaleString("pl-PL")}
                  </TableCell>
                  <TableCell>
                    {/* Ten sam przycisk co w "Gdzie używane" (UsedInPanel) -- to jedyny
                        wzorzec skoku do elementu w projekcie, więc ma wyglądać identycznie. */}
                    {onSelectItem && (
                      <Button
                        type="button"
                        size="icon-xs"
                        onClick={() => onSelectItem(row.itemId)}
                        aria-label={t("item.goToItemAria")}
                        title={t("item.goToItemAria")}
                      >
                        <ArrowUpRight />
                      </Button>
                    )}
                  </TableCell>
                </TableRow>
              )
            })}
          </TableBody>
        </Table>
      )}
    </div>
  )
}

// Zestawienie weryfikacji klienta dla CAŁEGO projektu, rozbite na trzy stany. Bez tego
// odpowiedź na "co jeszcze czeka u klienta" wymagała klikania element po elemencie —
// znacznik w drzewku pokazuje stan pojedynczej pozycji, nie obraz całości.
//
// Widać tu WYŁĄCZNIE elementy, które mają już jakiś wpis weryfikacji. Wydana Część bez ani
// jednego wpisu celowo nie trafia do żadnej z tabel i NIE brakuje tu czwartej, "bez
// weryfikacji": nie każdy wydany element jedzie do klienta, więc taka lista pokazywałaby
// głównie pozycje, których nikt nigdy nie zamierzał wysyłać — czyli szum zamiast obrazu
// tego, co faktycznie jest w obiegu.
function ClientVerificationOverview({
  summaries,
  onSelectItem,
}: {
  summaries: ClientVerificationSummary[]
  // Brak = przycisk przejścia się nie pokazuje (np. tam, gdzie nie ma drzewka do skoku).
  onSelectItem?: (itemId: string) => void
}) {
  const { t } = useLanguage()

  if (summaries.length === 0) return null

  const byNewestFirst = (a: ClientVerificationSummary, b: ClientVerificationSummary) =>
    b.createdAt.localeCompare(a.createdAt)
  const pending = summaries.filter((s) => s.result === null).sort(byNewestFirst)
  const verified = summaries.filter((s) => s.result === "zweryfikowany").sort(byNewestFirst)
  const needsWork = summaries.filter((s) => s.result === "do_poprawy").sort(byNewestFirst)

  return (
    <>
      <SectionLabel>{t("clientVerification.overviewLabel")}</SectionLabel>
      {/* Kolejność nieprzypadkowa: najpierw to, co wymaga działania (do poprawy), potem to,
          na co się czeka (w trakcie), a zaakceptowane na końcu — tam nic już nie trzeba robić. */}
      <VerificationTable
        label={t("clientVerification.resultNeedsWork")}
        rows={needsWork}
        emptyHint={t("clientVerification.noneNeedsWork")}
        onSelectItem={onSelectItem}
      />
      <VerificationTable
        label={t("clientVerification.resultPending")}
        rows={pending}
        emptyHint={t("clientVerification.nonePending")}
        onSelectItem={onSelectItem}
      />
      <VerificationTable
        label={t("clientVerification.resultVerified")}
        rows={verified}
        emptyHint={t("clientVerification.noneVerified")}
        onSelectItem={onSelectItem}
      />
    </>
  )
}

export { ClientVerificationOverview }
