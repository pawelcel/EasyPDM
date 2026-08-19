import { lazy, Suspense } from "react"

import { Dialog, DialogContent, DialogTitle } from "@/components/ui/dialog"
import type { PreviewKind } from "@/lib/file-preview"

import { PdfPreview } from "@/features/preview/pdf-preview"
import { useLanguage } from "@/i18n/use-language"

// three.js (renderer STEP-a) waży kilkaset KB — ładujemy go dopiero, gdy ktoś faktycznie
// otworzy podgląd STEP, żeby nie obciążać startowego bundla dla wszystkich użytkowników,
// którzy z podglądu STEP nigdy nie skorzystają.
const StepPreview = lazy(() => import("@/features/preview/step-preview").then((m) => ({ default: m.StepPreview })))

function PreviewDialog({
  open,
  onOpenChange,
  fileName,
  url,
  kind,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  fileName: string
  url: string
  kind: PreviewKind
}) {
  const { t } = useLanguage()
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="flex h-[85vh] max-w-4xl flex-col sm:max-w-4xl">
        <DialogTitle className="truncate pr-8">{fileName}</DialogTitle>
        <div className="min-h-0 flex-1 rounded-md bg-muted/30">
          {kind === "pdf" ? (
            <PdfPreview url={url} />
          ) : (
            <Suspense
              fallback={
                <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
                  {t("preview.loading")}
                </div>
              }
            >
              <StepPreview url={url} />
            </Suspense>
          )}
        </div>
      </DialogContent>
    </Dialog>
  )
}

export { PreviewDialog }
