import { Suspense, lazy, useEffect, useState } from "react"

import { api } from "@/api/client"
import type { Item } from "@/api/types"
import { Button } from "@/components/ui/button"
import { useLanguage } from "@/i18n/use-language"
import { previewKindOf } from "@/lib/file-preview"

import { PdfPreview } from "@/features/preview/pdf-preview"

const StepPreview = lazy(() => import("@/features/preview/step-preview").then((m) => ({ default: m.StepPreview })))

interface PreviewSource {
  fileName: string
  url: string
}

// Złożenie/Część nie mają własnego pliku (mają go dopiero załączniki) — Plik ma dokładnie
// jeden. Dla załączników bierzemy TE oznaczone jawnie jako rolę "pdf"/"step" (sloty
// PDF/STEP w panelu Załączniki) — nie zgadujemy po rozszerzeniu, żeby było jednoznaczne,
// który plik zasila podgląd. Panel Załączników pilnuje, żeby na rolę przypadał najwyżej
// jeden załącznik (nowy zastępuje stary), więc szukanie pierwszego pasującego wystarczy.
function usePreviewSources(item: Item): { pdf: PreviewSource | null; step: PreviewSource | null } {
  const [attachmentSources, setAttachmentSources] = useState<{ pdf: PreviewSource | null; step: PreviewSource | null }>({
    pdf: null,
    step: null,
  })

  useEffect(() => {
    if (item.itemType !== "part" && item.itemType !== "assembly") return
    let cancelled = false
    api.getAttachments(item.id).then((attachments) => {
      if (cancelled) return
      const pdfAttachment = attachments.find((a) => a.role === "pdf")
      const stepAttachment = attachments.find((a) => a.role === "step")
      setAttachmentSources({
        pdf: pdfAttachment ? { fileName: pdfAttachment.fileName, url: api.attachmentDownloadUrl(pdfAttachment.id) } : null,
        step: stepAttachment ? { fileName: stepAttachment.fileName, url: api.attachmentDownloadUrl(stepAttachment.id) } : null,
      })
    })
    return () => {
      cancelled = true
    }
  }, [item.id, item.itemType])

  if (item.itemType === "file" && item.filePath) {
    const kind = previewKindOf(item.fileName)
    const source = kind ? { fileName: item.fileName, url: api.fileDownloadUrl(item.id) } : null
    return { pdf: kind === "pdf" ? source : null, step: kind === "step" ? source : null }
  }

  return attachmentSources
}

// Miniaturowy podgląd u góry panelu właściwości — domyślnie rysunek PDF (2D), z
// przełącznikiem na model STEP (3D). Dla Części/Złożenia box jest widoczny ZAWSZE (nawet
// bez wgranego pliku) — brak pliku dla wybranego trybu pokazuje podpowiedź "wgraj w
// Załącznikach" zamiast całkiem znikać, żeby użytkownik od razu widział, gdzie i co dodać.
function ItemPreviewBox({ item }: { item: Item }) {
  const { t } = useLanguage()
  const { pdf, step } = usePreviewSources(item)
  const [mode, setMode] = useState<"2d" | "3d">("2d")

  useEffect(() => setMode("2d"), [item.id])

  const isAttachmentDriven = item.itemType === "part" || item.itemType === "assembly"
  if (!isAttachmentDriven && !pdf && !step) return null

  const active = mode === "2d" ? pdf : step
  const missingHint = mode === "2d" ? t("preview.missingPdfHint") : t("preview.missingStepHint")

  return (
    <div className="flex w-[32rem] shrink-0 flex-col gap-1.5">
      <div className="h-[22rem] overflow-hidden rounded-xl bg-muted/30 ring-1 ring-foreground/10">
        {active ? (
          mode === "2d" ? (
            <PdfPreview url={active.url} />
          ) : (
            <Suspense
              fallback={
                <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
                  {t("preview.loading")}
                </div>
              }
            >
              <StepPreview url={active.url} />
            </Suspense>
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
