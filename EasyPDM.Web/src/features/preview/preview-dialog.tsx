import { Dialog, DialogContent, DialogTitle } from "@/components/ui/dialog"
import type { PreviewKind } from "@/lib/file-preview"

import { PdfPreview } from "@/features/preview/pdf-preview"

// Podgląd modelu to zwykły obrazek (zrzut zrobiony przez makro CAD przy wysyłce), więc nie
// ma tu już nic do leniwego ładowania — renderer STEP-a (three.js + occt-import-js, kilkaset
// KB) został usunięty razem z samym renderowaniem. Zob. lib/file-preview.ts po powód.
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
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="flex h-[85vh] max-w-4xl flex-col sm:max-w-4xl">
        <DialogTitle className="truncate pr-8">{fileName}</DialogTitle>
        <div className="min-h-0 flex-1 rounded-md bg-muted/30">
          {kind === "pdf" ? (
            <PdfPreview url={url} />
          ) : (
            <img src={url} alt={fileName} className="h-full w-full object-contain" />
          )}
        </div>
      </DialogContent>
    </Dialog>
  )
}

export { PreviewDialog }
