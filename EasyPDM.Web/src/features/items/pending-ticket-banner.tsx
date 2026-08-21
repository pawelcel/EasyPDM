import { useEffect, useState } from "react"

import { api, ApiError } from "@/api/client"
import { itemDisplayLabel, type Item } from "@/api/types"
import { Button } from "@/components/ui/button"
import {
  Combobox,
  ComboboxContent,
  ComboboxEmpty,
  ComboboxInput,
  ComboboxItem,
  ComboboxList,
} from "@/components/ui/combobox"
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { FormError } from "@/components/ui/form-error"
import { Hint } from "@/components/ui/hint"
import { AddNodeDialog } from "@/features/items/add-node-dialog"
import { clearPendingCreateTicket, usePendingCreateTicket } from "@/features/items/pending-create-ticket"
import { useLanguage } from "@/i18n/use-language"

// Popup widoczny na każdym ekranie, dopóki czeka bilet z makra CAD (zob.
// pending-create-ticket.ts) — CAŁY przepływ dzieje się w jednym oknie modalnym, spójnie z
// resztą aplikacji (ten sam wygląd co AddNodeDialog), bez żadnych swobodnie pływających
// przycisków w nagłówku. Dwa tryby:
//   "create" (EasyPDMUpload.FCMacro) — JAWNY wybór między dwoma przyciskami:
//     "Nowy element" zamyka ten popup i od razu otwiera AddNodeDialog (bez z góry ustalonego
//       projektu — sam pyta o projekt i opcjonalnie rodzica jako pierwszy krok, więc NIE
//       trzeba wcześniej nawigować w panelu po lewej) — bilet przekazywany JAWNIE jako prop,
//       więc żadne INNE "Dodaj" w aplikacji nigdy przypadkiem go nie "połknie".
//     "Dograj do istniejącego elementu" — wyszukiwarka + checkbox STEP tutaj, w tym popupie.
//   "download" (EasyPDMDownload.FCMacro) — jedyna możliwa akcja, więc wyszukiwarka widoczna
//     OD RAZU, bez checkboksa STEP (nieistotny przy pobieraniu) i bez wyboru trybu.
// W obu trybach backend to ta sama operacja — POST /create-tickets/{ticket}/attach-existing
// nic nie tworzy, tylko wskazuje makru, o który element chodzi.
// Popup jest celowo NIEODRZUCALNY (brak X, Escape/kliknięcie w tło nic nie robi) — makro
// czeka po drugiej stronie, przypadkowe zamknięcie zostawiłoby je zawieszone.
function PendingTicketBanner() {
  const { t } = useLanguage()
  const pendingTicket = usePendingCreateTicket()
  const isDownload = pendingTicket?.mode === "download"
  const [expanded, setExpanded] = useState(false)
  const [creating, setCreating] = useState(false)
  const [items, setItems] = useState<Item[]>([])
  const [itemId, setItemId] = useState("")
  const [exportStep, setExportStep] = useState(true)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState("")

  const showPicker = expanded || isDownload

  useEffect(() => {
    if (!showPicker) return
    api.getItems({}).then(setItems)
  }, [showPicker])

  useEffect(() => {
    if (!pendingTicket) {
      setExpanded(false)
      setCreating(false)
      setItemId("")
      setError("")
    }
  }, [pendingTicket])

  const candidates = items.filter((i) => i.itemType === "part" || i.itemType === "assembly")

  // Podpowiedź z makra (etykieta lokalnego dokumentu wygląda jak już wysłany element) —
  // zaznaczana automatycznie przy pierwszym pokazaniu wyszukiwarki, wybór zawsze można zmienić.
  // Hooki muszą się wywoływać bezwarunkowo (przed ewentualnym "if (!pendingTicket) return
  // null" niżej) — stąd opcjonalne łańcuchowanie zamiast wczesnego returna wewnątrz efektu.
  useEffect(() => {
    if (showPicker && !itemId && pendingTicket?.suggestedItemNumber !== undefined) {
      const match = candidates.find((i) => i.itemNumber === pendingTicket.suggestedItemNumber)
      if (match) setItemId(match.id)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [showPicker, candidates.length])

  if (!pendingTicket) return null

  async function confirm() {
    if (!itemId) {
      setError(t("addNode.selectItemRequired"))
      return
    }
    setSubmitting(true)
    setError("")
    try {
      await api.attachExistingToTicket(pendingTicket!.ticket, itemId, isDownload ? undefined : exportStep)
      clearPendingCreateTicket()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t("addNode.addFailed"))
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <>
      {!creating && (
        <Dialog open>
          <DialogContent showCloseButton={false}>
            <DialogHeader>
              <DialogTitle>{t("app.pendingTicketDialogTitle")}</DialogTitle>
            </DialogHeader>

            <Hint>
              {t(isDownload ? "app.pendingDownloadTicketHint" : "app.pendingCreateTicketHint")}
              {pendingTicket.name ? ` (${pendingTicket.name})` : ""}
            </Hint>

            {!showPicker && (
              <div className="flex flex-wrap gap-2">
                <Button size="sm" onClick={() => setCreating(true)}>
                  {t("app.pendingTicketCreateNewButton")}
                </Button>
                <Button size="sm" variant="outline" onClick={() => setExpanded(true)}>
                  {t("app.pendingTicketAttachExistingButton")}
                </Button>
              </div>
            )}

            {showPicker && (
              <div className="flex flex-col gap-2">
                <Combobox
                  items={candidates.map((c) => c.id)}
                  value={itemId || null}
                  onValueChange={(v) => setItemId((v as string | null) ?? "")}
                  itemToStringLabel={(id: string) => {
                    const found = candidates.find((c) => c.id === id)
                    return found ? itemDisplayLabel(found) : ""
                  }}
                >
                  <ComboboxInput placeholder={t("part.searchPlaceholder")} />
                  <ComboboxContent>
                    <ComboboxEmpty>{t("addNode.noMatchingItems")}</ComboboxEmpty>
                    <ComboboxList>
                      {(id: string) => {
                        const found = candidates.find((c) => c.id === id)
                        return (
                          <ComboboxItem key={id} value={id}>
                            {found ? itemDisplayLabel(found) : ""}
                          </ComboboxItem>
                        )
                      }}
                    </ComboboxList>
                  </ComboboxContent>
                </Combobox>

                {!isDownload && (
                  <label className="flex cursor-pointer items-center gap-2 text-sm">
                    <input
                      type="checkbox"
                      checked={exportStep}
                      onChange={(e) => setExportStep(e.target.checked)}
                      className="size-3.5 shrink-0 accent-primary"
                    />
                    {t("addNode.exportStepOptional")}
                  </label>
                )}

                <FormError>{error}</FormError>
              </div>
            )}

            {showPicker && (
              <DialogFooter>
                {!isDownload && (
                  <Button variant="outline" onClick={() => setExpanded(false)} disabled={submitting}>
                    {t("common.cancel")}
                  </Button>
                )}
                <Button onClick={confirm} disabled={!itemId || submitting}>
                  {submitting ? t("common.saving") : t(isDownload ? "common.download" : "common.add")}
                </Button>
              </DialogFooter>
            )}
          </DialogContent>
        </Dialog>
      )}

      {creating && (
        <AddNodeDialog
          trigger={<span className="hidden" />}
          initialOpen
          initialName={pendingTicket.name}
          ticket={pendingTicket.ticket}
          onOpenChange={(open) => {
            // Zamknięte bez utworzenia (Anuluj/X/Escape) — wraca do wyboru Nowy/Istniejący
            // zamiast zostawiać ticket w martwym stanie. Po udanym utworzeniu bilet już nie
            // istnieje (onCreated poniżej go czyści), więc ten efekt się nie uruchomi.
            if (!open) setCreating(false)
          }}
          onCreated={async () => {
            clearPendingCreateTicket()
          }}
        />
      )}
    </>
  )
}

export { PendingTicketBanner }
