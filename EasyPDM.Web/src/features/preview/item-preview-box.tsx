import { useEffect, useRef, useState } from "react"

import { api } from "@/api/client"
import type { Item } from "@/api/types"
import { Button } from "@/components/ui/button"
import { useLanguage } from "@/i18n/use-language"
import { previewKindOf } from "@/lib/file-preview"

import { PdfPreview } from "@/features/preview/pdf-preview"

interface PreviewSource {
  fileName: string
  url: string
}

// Złożenie/Część nie mają własnego pliku (mają go dopiero załączniki) — Plik ma dokładnie
// jeden. Dla załączników bierzemy TE oznaczone jawnie jako rolę "pdf"/"image" (slot PDF i
// zrzut modelu) — nie zgadujemy po rozszerzeniu, żeby było jednoznaczne, który plik zasila
// podgląd. Panel Załączników pilnuje, żeby na rolę przypadał najwyżej jeden załącznik (nowy
// zastępuje stary), więc szukanie pierwszego pasującego wystarczy.
//
// Model 3D pokazujemy jako GOTOWY OBRAZEK (rola "image", PNG zrobiony przez makro CAD przy
// wysyłce), a nie renderując STEP w przeglądarce. Podgląd i tak był nieruchomy — jedno
// renderer.render(), bez obracania — więc pobieranie bryły, teselacja przez OpenCascade w
// WebAssembly i liczenie krawędzi dla każdej bryły były płacone przy KAŻDYM otwarciu
// elementu, u każdego użytkownika, po to, żeby dostać nieruchomy obraz. Sam STEP wgrywa się
// bez zmian (rola "step") — jest do pobrania, po prostu nie służy już do wyświetlania.
function usePreviewSources(item: Item, refreshSignal: number): { pdf: PreviewSource | null; image: PreviewSource | null } {
  const [attachmentSources, setAttachmentSources] = useState<{ pdf: PreviewSource | null; image: PreviewSource | null }>({
    pdf: null,
    image: null,
  })
  // Do wykrycia PRAWDZIWEJ zmiany elementu (w odróżnieniu od samego odświeżenia
  // refreshSignal na TYM SAMYM elemencie) — zob. reset stanu niżej.
  const prevItemIdRef = useRef(item.id)

  useEffect(() => {
    // Zerujemy od razu przy zmianie elementu — inaczej box pokazywałby przez chwilę
    // podgląd POPRZEDNIEGO elementu, dopóki nowe api.getAttachments() nie wróci. Tylko
    // przy faktycznej zmianie item.id (nie przy zwykłym odświeżeniu po wgraniu/usunięciu
    // załącznika na TYM SAMYM elemencie — tam podgląd ma zostać widoczny do czasu
    // odświeżenia, bez zbędnego mignięcia).
    if (prevItemIdRef.current !== item.id) {
      setAttachmentSources({ pdf: null, image: null })
      prevItemIdRef.current = item.id
    }
    if (item.itemType !== "part" && item.itemType !== "assembly") return
    let cancelled = false
    api.getAttachments(item.id).then((attachments) => {
      if (cancelled) return
      const pdfAttachment = attachments.find((a) => a.role === "pdf")
      const imageAttachment = attachments.find((a) => a.role === "image")
      setAttachmentSources({
        pdf: pdfAttachment ? { fileName: pdfAttachment.fileName, url: api.attachmentDownloadUrl(pdfAttachment.id) } : null,
        image: imageAttachment
          ? { fileName: imageAttachment.fileName, url: api.attachmentDownloadUrl(imageAttachment.id) }
          : null,
      })
    })
    return () => {
      cancelled = true
    }
    // refreshSignal celowo w zależnościach — rośnie po każdej akcji w AttachmentsPanel
    // (wgranie/usunięcie pliku PDF/STEP), żeby box odświeżył się bez ponownego
    // zaznaczania elementu (zob. historyRefreshSignal w item-detail-panel.tsx).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [item.id, item.itemType, refreshSignal])

  if (item.itemType === "file" && item.filePath) {
    const kind = previewKindOf(item.fileName)
    const source = kind ? { fileName: item.fileName, url: api.fileDownloadUrl(item.id) } : null
    // Element typu Plik niesie JEDEN plik — podgląd ma więc tylko wtedy, gdy to PDF.
    // Wgrany ręcznie STEP nie ma zrzutu (robi go makro przy wysyłce z CAD-a), a renderowania
    // w przeglądarce już nie ma.
    return { pdf: kind === "pdf" ? source : null, image: null }
  }

  return attachmentSources
}

// Miniaturowy podgląd u góry panelu właściwości — domyślnie model (zrzut z CAD-a), z
// przełącznikiem na rysunek PDF (2D). Dla Części/Złożenia box jest widoczny ZAWSZE (nawet bez
// wgranego pliku) — brak pliku dla wybranego trybu pokazuje podpowiedź "wgraj w Załącznikach"
// zamiast całkiem znikać, żeby użytkownik od razu widział, gdzie i co dodać.
function ItemPreviewBox({ item, refreshSignal = 0 }: { item: Item; refreshSignal?: number }) {
  const { t } = useLanguage()
  const { pdf, image } = usePreviewSources(item, refreshSignal)
  const [mode, setMode] = useState<"2d" | "3d">("3d")

  useEffect(() => setMode("3d"), [item.id])

  const isAttachmentDriven = item.itemType === "part" || item.itemType === "assembly"
  if (!isAttachmentDriven && !pdf && !image) return null

  const active = mode === "2d" ? pdf : image
  const missingHint = mode === "2d" ? t("preview.missingPdfHint") : t("preview.missingImageHint")

  return (
    <div className="flex w-[32rem] shrink-0 flex-col gap-1.5">
      <div className="h-[22rem] overflow-hidden rounded-xl bg-muted/30 ring-1 ring-foreground/10">
        {active ? (
          mode === "2d" ? (
            <PdfPreview url={active.url} />
          ) : (
            // object-contain, bo zrzut ma proporcje okna CAD-a, a box jest stały — przycięcie
            // ucięłoby część modelu, a rozciągnięcie zniekształciło go.
            <img src={active.url} alt={active.fileName} className="h-full w-full object-contain" />
          )
        ) : (
          <div className="flex h-full items-center justify-center px-6 text-center text-sm text-muted-foreground">
            {missingHint}
          </div>
        )}
      </div>
      {isAttachmentDriven && (
        <div className="flex justify-center gap-1.5">
          <Button size="sm" variant={mode === "2d" ? "secondary" : "outline"} onClick={() => setMode("2d")}>
            2D
          </Button>
          <Button size="sm" variant={mode === "3d" ? "secondary" : "outline"} onClick={() => setMode("3d")}>
            3D
          </Button>
        </div>
      )}
    </div>
  )
}

export { ItemPreviewBox }
