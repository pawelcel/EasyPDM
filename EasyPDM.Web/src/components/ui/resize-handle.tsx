import type { MouseEvent } from "react"

import { useLanguage } from "@/i18n/use-language"

// Cienki, przeciągalny uchwyt między dwiema kolumnami (zob. useResizableWidth) — sam nie
// trzyma żadnego stanu, tylko przekazuje zdarzenie myszy dalej.
function ResizeHandle({ onMouseDown }: { onMouseDown: (e: MouseEvent) => void }) {
  const { t } = useLanguage()
  return (
    <div
      role="separator"
      aria-orientation="vertical"
      aria-label={t("common.resizePanelAria")}
      onMouseDown={onMouseDown}
      className="mx-1.5 w-1 shrink-0 cursor-col-resize rounded bg-transparent transition-colors hover:bg-foreground/15 active:bg-foreground/25"
    />
  )
}

export { ResizeHandle }
