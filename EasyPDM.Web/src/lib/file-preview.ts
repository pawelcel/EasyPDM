export type PreviewKind = "pdf" | "step"

// Tylko formaty, dla których mamy podgląd w przeglądarce — reszta dostaje wyłącznie
// przycisk pobierania.
export function previewKindOf(fileName: string): PreviewKind | null {
  const ext = fileName.toLowerCase().split(".").pop() ?? ""
  if (ext === "pdf") return "pdf"
  if (ext === "step" || ext === "stp") return "step"
  return null
}
