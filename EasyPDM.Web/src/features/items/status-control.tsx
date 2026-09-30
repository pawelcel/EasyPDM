import { useEffect, useState } from "react"

import { api } from "@/api/client"
import {
  revisionLabel,
  STATUS_LABEL_KEYS,
  type BomBlockedReason,
  type Item,
  type ItemStatus,
  type RevisionComment,
  type StatusPrecheck,
} from "@/api/types"
import { Button } from "@/components/ui/button"
import { buttonVariants } from "@/components/ui/button-variants"
import { ConfirmDialog } from "@/components/ui/confirm-dialog"
import { Label } from "@/components/ui/label"
import { Textarea } from "@/components/ui/textarea"
import { useLanguage } from "@/i18n/use-language"
import { cn } from "@/lib/utils"

const NEXT_STATUSES: Record<ItemStatus, ItemStatus[]> = {
  w_pracy: ["sprawdzany"],
  sprawdzany: ["w_pracy", "wydany"],
  wydany: ["w_pracy", "anulowana"],
  anulowana: ["w_pracy"],
}

// Stała kolejność wszystkich statusów -- przyciski renderowane są zawsze w tym samym
// układzie (bieżący jako odznaka, reszta jako przyciski), żeby nic nie zmieniało pozycji
// przy zmianie statusu. Nieosiągalne stąd bezpośrednio przejścia (np. w_pracy -> wydany)
// są nadal widoczne, tylko wyszarzone/nieaktywne. "Anulowana" wybieralna wyłącznie z
// "wydany" — z każdego innego statusu ten przycisk jest więc zawsze wyszarzony.
const ALL_STATUSES: ItemStatus[] = ["w_pracy", "sprawdzany", "wydany", "anulowana"]

// Kod powodu z API -> klucz tłumaczenia. "as const satisfies" zamiast zwykłej adnotacji
// Record<..., string>: t() przyjmuje wyłącznie literalne klucze, a "satisfies" i tak pilnuje,
// żeby każdy powód zwracany przez serwer miał tu swój tekst.
const BLOCKED_REASON_KEYS = {
  anulowana: "item.bomReasonCancelled",
  zablokowany: "item.bomReasonLocked",
  brak_dostepu: "item.bomReasonNoAccess",
} as const satisfies Record<BomBlockedReason, string>

function StatusControl({
  item,
  disabled = false,
  onChanged,
}: {
  item: Item
  disabled?: boolean
  onChanged: () => void | Promise<void>
}) {
  const { t } = useLanguage()
  const status = item.status ?? "w_pracy"
  const [pending, setPending] = useState<ItemStatus | null>(null)
  const [comment, setComment] = useState("")
  const [revisions, setRevisions] = useState<RevisionComment[]>([])
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  // Wynik sprawdzenia BOM-u dla wybranego przejścia. null = jeszcze nie wiadomo (trwa
  // zapytanie albo przejście w ogóle reguły nie dotyczy, zob. needsBomCheck).
  const [precheck, setPrecheck] = useState<StatusPrecheck | null>(null)
  const [checking, setChecking] = useState(false)

  useEffect(() => {
    api.getRevisionComments(item.id).then(setRevisions).catch(() => setRevisions([]))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [item.id, item.revisionNumber])

  // Reguła BOM-u dotyczy wyłącznie Złożeń i wyłącznie ruchu "w górę" — cofnięcie do pracy
  // czy anulowanie niczego od komponentów nie wymaga.
  function needsBomCheck(target: ItemStatus): target is "sprawdzany" | "wydany" {
    return item.itemType === "assembly" && (target === "sprawdzany" || target === "wydany")
  }

  async function startChange(target: ItemStatus) {
    setComment("")
    setError(null)
    setPrecheck(null)
    setPending(target)
    if (!needsBomCheck(target)) return

    setChecking(true)
    try {
      setPrecheck(await api.getStatusPrecheck(item.id, target))
    } catch {
      // Nieudane sprawdzenie nie blokuje próby — serwer i tak sprawdza to samo jeszcze raz
      // przy PATCH-u i odmówi z własnym komunikatem. Zostawiamy zwykłe okno potwierdzenia.
      setPrecheck(null)
    } finally {
      setChecking(false)
    }
  }

  async function confirmChange() {
    if (!pending) return
    setSubmitting(true)
    setError(null)
    try {
      await api.setStatus(
        item.id,
        pending,
        isRevisionBump ? comment.trim() : undefined,
        // Zgodę wysyłamy tylko wtedy, gdy okno faktycznie o nią zapytało.
        promotePending
      )
      setPending(null)
      setComment("")
      setPrecheck(null)
      await onChanged()
    } catch (err) {
      setError(err instanceof Error ? err.message : t("item.statusChangeFailed"))
    } finally {
      setSubmitting(false)
    }
  }

  // Trzy możliwe wyniki sprawdzenia, w kolejności ważności:
  //   blockedByAssemblies — są podzłożenia do ogarnięcia osobno; nic nie proponujemy,
  //   blockedByOther      — komponenty, których nie wolno tknąć (anulowane/cudze/bez dostępu),
  //   promotePending      — same Części, które można pociągnąć po zgodzie użytkownika.
  const blockedByAssemblies = (precheck?.subAssemblies.length ?? 0) > 0
  const blockedByOther = !blockedByAssemblies && (precheck?.blocked.length ?? 0) > 0
  const promotePending = !blockedByAssemblies && !blockedByOther && (precheck?.promotable.length ?? 0) > 0
  const labelsOf = (entries: { label: string }[]) => entries.map((e) => e.label).join(", ")

  // Przeszkoda trafia do slotu "error" ConfirmDialoga — razem z singleAckOnError zamienia to
  // parę Anuluj/Potwierdź w jedno "OK", bo tu nie ma czego potwierdzać. Ten sam zabieg, co
  // przy odmowie wydania złożenia z anulowanym elementem w BOM-ie.
  const blockingError = blockedByAssemblies
    ? t("item.bomSubAssembliesError")
    : blockedByOther
      ? t("item.bomBlockedError")
      : null

  const isRevisionBump = (status === "wydany" || status === "anulowana") && pending === "w_pracy"
  const current = item.revisionNumber ?? 1

  return (
    <div className="flex flex-wrap items-center gap-2">
      <div className="flex flex-wrap gap-1.5">
        {ALL_STATUSES.map((s) =>
          s === status ? (
            <span key={s} className={cn(buttonVariants({ variant: "default", size: "sm" }))}>
              {t(STATUS_LABEL_KEYS[s])}
            </span>
          ) : (
            <Button
              key={s}
              size="sm"
              variant="outline"
              disabled={disabled || !NEXT_STATUSES[status].includes(s)}
              onClick={() => void startChange(s)}
            >
              → {t(STATUS_LABEL_KEYS[s])}
            </Button>
          )
        )}
      </div>

      {revisions.length > 0 && (
        <div className="flex w-full flex-col gap-0.5 text-[12.5px] text-muted-foreground">
          {revisions.map((r) => (
            <div key={r.revisionNumber}>
              <span className="font-medium">rev. {r.revisionLabel}:</span> {r.comment}
            </div>
          ))}
        </div>
      )}

      {pending && (
        <ConfirmDialog
          open
          title={t("item.statusChangeTitle")}
          description={
            isRevisionBump ? (
              <div className="flex flex-col gap-2">
                <p>
                  {t("item.revisionBumpNotice", {
                    statusFrom: t(STATUS_LABEL_KEYS[status]),
                    statusTo: t("status.w_pracy"),
                    // "to" nie może przyjść z serwera -- to podgląd PRZED faktycznym bumpem,
                    // serwer jeszcze nic o tej rewizji nie wie.
                    from: item.revisionLabel ?? "A",
                    to: revisionLabel(current + 1),
                  })}
                </p>
                <div className="flex flex-col gap-1">
                  <Label htmlFor="revision-comment">{t("item.revisionCommentLabel")}</Label>
                  <Textarea
                    id="revision-comment"
                    value={comment}
                    onChange={(e) => setComment(e.target.value)}
                    rows={3}
                    placeholder={t("item.revisionCommentPlaceholder")}
                  />
                </div>
              </div>
            ) : blockedByAssemblies ? (
              // Podzłożenie ma własne zestawienie i własną regułę — nie ruszamy go przy
              // okazji, użytkownik musi przejść je osobno (i zobaczyć jego własne przeszkody).
              <div className="flex flex-col gap-2">
                <p>
                  {t("item.bomSubAssembliesText", {
                    statusTo: t(STATUS_LABEL_KEYS[pending]),
                    items: labelsOf(precheck!.subAssemblies),
                  })}
                </p>
              </div>
            ) : blockedByOther ? (
              <div className="flex flex-col gap-2">
                <p>{t("item.bomBlockedText", { statusTo: t(STATUS_LABEL_KEYS[pending]) })}</p>
                <ul className="list-disc pl-5">
                  {precheck!.blocked.map((b) => (
                    <li key={b.id}>
                      {b.label} — {t(BLOCKED_REASON_KEYS[b.reason])}
                    </li>
                  ))}
                </ul>
              </div>
            ) : promotePending ? (
              <div className="flex flex-col gap-2">
                <p>
                  {t("item.bomPromoteText", {
                    statusTo: t(STATUS_LABEL_KEYS[pending]),
                    items: labelsOf(precheck!.promotable),
                  })}
                </p>
                <p>{t("item.bomPromoteQuestion", { statusTo: t(STATUS_LABEL_KEYS[pending]) })}</p>
              </div>
            ) : (
              t("item.statusChangeConfirmText", {
                statusFrom: t(STATUS_LABEL_KEYS[status]),
                statusTo: t(STATUS_LABEL_KEYS[pending]),
              })
            )
          }
          confirmLabel={promotePending ? t("item.bomPromoteConfirm") : t("item.statusChangeConfirm")}
          onConfirm={confirmChange}
          onCancel={() => {
            setPending(null)
            setComment("")
            setPrecheck(null)
          }}
          pending={submitting || checking}
          error={error ?? blockingError}
          singleAckOnError
        />
      )}
    </div>
  )
}

export { StatusControl }
