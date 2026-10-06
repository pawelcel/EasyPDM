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
import { askForCadFormNotices } from "@/features/items/cad-form-notice"
import { clearPendingCreateTicket, usePendingCreateTicket } from "@/features/items/pending-create-ticket"
import { useLanguage } from "@/i18n/use-language"

type Screen = "choice" | "attach" | "duplicate"

// Popup widoczny na każdym ekranie, dopóki czeka bilet z makra CAD (zob.
// pending-create-ticket.ts) — CAŁY przepływ dzieje się w jednym oknie modalnym, spójnie z
// resztą aplikacji (ten sam wygląd co AddNodeDialog), bez żadnych swobodnie pływających
// przycisków w nagłówku. Dwa tryby:
//   "create" (EasyPDMUpload.FCMacro) — JAWNY wybór między trzema przyciskami:
//     "Nowy element" zamyka ten popup i od razu otwiera AddNodeDialog (bez z góry ustalonego
//       projektu — sam pyta o projekt i opcjonalnie rodzica jako pierwszy krok, więc NIE
//       trzeba wcześniej nawigować w panelu po lewej) — bilet przekazywany JAWNIE jako prop,
//       więc żadne INNE "Dodaj" w aplikacji nigdy przypadkiem go nie "połknie".
//     "Duplikuj" — wyszukiwarka wskazuje ŹRÓDŁOWY element, potem otwiera ten sam AddNodeDialog
//       co "Nowy element", tylko wstępnie wypełniony jego właściwościami (rodzaj/materiał/
//       producent/numery/norma/masa) — BEZ kopiowania żadnych plików, dalej można je tu
//       edytować przed zapisem. To zwykłe tworzenie nowego elementu, tylko podpowiedziane.
//     "Dograj do istniejącego" — wyszukiwarka + checkbox STEP tutaj, w tym popupie.
//   "download" (EasyPDMDownload.FCMacro) — jedyna możliwa akcja, więc wyszukiwarka widoczna
//     OD RAZU, bez checkboksa STEP (nieistotny przy pobieraniu) i bez wyboru trybu.
// "Dograj do istniejącego" i "download" to backendowo ta sama operacja — POST
// /create-tickets/{ticket}/attach-existing nic nie tworzy, tylko wskazuje makru, o który
// element chodzi. "Duplikuj" tej operacji w ogóle nie woła — to zwykłe POST /nodes z
// ticketem (jak "Nowy element"), tylko z properties przepisanymi ze źródła.
// Popup jest celowo NIEODRZUCALNY (brak X, Escape/kliknięcie w tło nic nie robi) — makro
// czeka po drugiej stronie, przypadkowe zamknięcie zostawiłoby je zawieszone.
function PendingTicketBanner() {
  const { t } = useLanguage()
  const pendingTicket = usePendingCreateTicket()
  const isDownload = pendingTicket?.mode === "download"
  const [screen, setScreen] = useState<Screen>("choice")
  const [creating, setCreating] = useState(false)
  const [duplicateSource, setDuplicateSource] = useState<Item | null>(null)
  const [items, setItems] = useState<Item[]>([])
  const [itemId, setItemId] = useState("")
  const [exportStep, setExportStep] = useState(true)
  const [exportPdf, setExportPdf] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState("")

  const pickingItem = screen === "attach" || screen === "duplicate"

  useEffect(() => {
    if (!pickingItem) return
    api.getItems({}).then(setItems)
  }, [pickingItem])

  useEffect(() => {
    setCreating(false)
    setDuplicateSource(null)
    setItemId("")
    setError("")
    setScreen(pendingTicket?.mode === "download" ? "attach" : "choice")
  }, [pendingTicket])

  const candidates = items.filter((i) => i.itemType === "part" || i.itemType === "assembly")

  // Podpowiedź z makra (etykieta lokalnego dokumentu wygląda jak już wysłany element) —
  // zaznaczana automatycznie przy pierwszym pokazaniu wyszukiwarki "Dograj do istniejącego",
  // wybór zawsze można zmienić. NIE dotyczy "Duplikuj" — tam wybieramy ŹRÓDŁO do skopiowania,
  // a nie "ten sam element co lokalny plik". Hooki muszą się wywoływać bezwarunkowo (przed
  // ewentualnym "if (!pendingTicket) return null" niżej) — stąd opcjonalne łańcuchowanie
  // zamiast wczesnego returna wewnątrz efektu.
  useEffect(() => {
    if (screen === "attach" && !itemId && pendingTicket?.suggestedItemNumber !== undefined) {
      const match = candidates.find((i) => i.itemNumber === pendingTicket.suggestedItemNumber)
      if (match) setItemId(match.id)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [screen, candidates.length])

  if (!pendingTicket) return null

  async function confirmAttach() {
    if (!itemId) {
      setError(t("addNode.selectItemRequired"))
      return
    }
    setSubmitting(true)
    setError("")
    try {
      await api.attachExistingToTicket(
        pendingTicket!.ticket,
        itemId,
        isDownload ? undefined : exportStep,
        isDownload ? undefined : exportPdf
      )
      void api.clearCadRequest(pendingTicket!.ticket)
      clearPendingCreateTicket()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t("addNode.addFailed"))
    } finally {
      setSubmitting(false)
    }
  }

  function confirmDuplicate() {
    const source = candidates.find((c) => c.id === itemId)
    if (!source) {
      setError(t("addNode.selectItemRequired"))
      return
    }
    setDuplicateSource(source)
    setCreating(true)
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

            {screen === "choice" && (
              <div className="flex flex-wrap gap-2">
                {/* Pytanie o zgodę na powiadomienia systemowe musi paść Z KLIKNIĘCIA —
                    przeglądarki odrzucają je zadane w tle. To pierwsze kliknięcie w całym
                    przepływie CAD-owym, a kolejne komponenty tego samego biegu już z tej
                    zgody korzystają. Zob. cad-form-notice.ts. */}
                <Button size="sm" onClick={() => { askForCadFormNotices(); setCreating(true) }}>
                  {t("app.pendingTicketCreateNewButton")}
                </Button>
                <Button size="sm" variant="outline" onClick={() => { askForCadFormNotices(); setScreen("duplicate") }}>
                  {t("app.pendingTicketDuplicateButton")}
                </Button>
                <Button size="sm" variant="outline" onClick={() => { askForCadFormNotices(); setScreen("attach") }}>
                  {t("app.pendingTicketAttachExistingButton")}
                </Button>
              </div>
            )}

            {pickingItem && (
              <div className="flex flex-col gap-2">
                {screen === "duplicate" && <Hint>{t("addNode.duplicateSourceHint")}</Hint>}

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

                {screen === "attach" && !isDownload && (
                  <>
                    <label className="flex cursor-pointer items-center gap-2 text-sm">
                      <input
                        type="checkbox"
                        checked={exportStep}
                        onChange={(e) => setExportStep(e.target.checked)}
                        className="size-3.5 shrink-0 accent-primary"
                      />
                      {t("addNode.exportStepOptional")}
                    </label>
                    <label className="flex cursor-pointer items-center gap-2 text-sm">
                      <input
                        type="checkbox"
                        checked={exportPdf}
                        onChange={(e) => setExportPdf(e.target.checked)}
                        className="size-3.5 shrink-0 accent-primary"
                      />
                      {t("addNode.exportPdfOptional")}
                    </label>
                  </>
                )}

                <FormError>{error}</FormError>
              </div>
            )}

            {pickingItem && (
              <DialogFooter>
                {!(screen === "attach" && isDownload) && (
                  <Button
                    variant="outline"
                    onClick={() => setScreen("choice")}
                    disabled={submitting}
                  >
                    {t("common.cancel")}
                  </Button>
                )}
                {screen === "duplicate" ? (
                  <Button onClick={confirmDuplicate} disabled={!itemId}>
                    {t("app.pendingTicketDuplicateButton")}
                  </Button>
                ) : (
                  <Button onClick={confirmAttach} disabled={!itemId || submitting}>
                    {submitting ? t("common.saving") : t(isDownload ? "common.download" : "common.add")}
                  </Button>
                )}
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
          // Duplikat dziedziczy typ po źródle; nowy element bierze typ rozpoznany przez makro
          // z samego pliku CAD — inaczej okno startuje bez wyboru i złożenie łatwo powstaje
          // jako Część, przez co struktura BOM nie ma się do czego podpiąć.
          initialMode={
            duplicateSource?.itemType === "assembly"
              ? "assembly"
              : duplicateSource
                ? "part"
                : pendingTicket.cadItemType
          }
          // Duplikat ma własne właściwości skopiowane ze źródła i one mają pierwszeństwo —
          // użytkownik świadomie wybrał "skopiuj z istniejącego". Materiał z dokumentu CAD
          // wchodzi tylko tam, gdzie nie ma czego kopiować, czyli przy nowym elemencie.
          initialProperties={
            duplicateSource?.properties ??
            (pendingTicket.material ? { material: pendingTicket.material } : undefined)
          }
          // Materiał z dokumentu CAD jest do obejrzenia, nie do zmiany: po wysyłce makro
          // zapisuje go z właściwości EasyPDM_Material i nadpisałoby tu zrobiony wybór.
          // Duplikat ma materiał skopiowany ze źródła, czyli wybrany świadomie — ten zostaje
          // edytowalny.
          materialLocked={!duplicateSource && !!pendingTicket.material}
          documentSize={pendingTicket.documentSize}
          ticket={pendingTicket.ticket}
          onOpenChange={(open) => {
            // Zamknięte bez utworzenia (Anuluj/X/Escape) — wraca do wyboru Nowy/Duplikuj/
            // Istniejący zamiast zostawiać ticket w martwym stanie. Po udanym utworzeniu
            // bilet już nie istnieje (onCreated poniżej go czyści), więc ten efekt się nie
            // uruchomi.
            if (!open) {
              setCreating(false)
              setDuplicateSource(null)
              setScreen("choice")
            }
          }}
          onCreated={async () => {
            // Prośba znika też z serwera — inaczej po odświeżeniu strony ta sama karta
            // podjęłaby ją ponownie i pokazała formularz dla komponentu, który już powstał.
            void api.clearCadRequest(pendingTicket.ticket)
            void api.clearCadRequest(pendingTicket.ticket)
      clearPendingCreateTicket()
          }}
        />
      )}
    </>
  )
}

export { PendingTicketBanner }
