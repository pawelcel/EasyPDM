export type PreviewKind = "pdf" | "step"

// Tylko formaty, dla których mamy podgląd w przeglądarce — reszta dostaje wyłącznie
// przycisk pobierania. "step" obejmuje kilka formatów 3D (STEP/IGES natywnie przez
// occt-import-js, STL przez wbudowany loader three.js) — to jeden slot podglądu 3D, nie
// dosłownie tylko format STEP.
export function previewKindOf(fileName: string): PreviewKind | null {
  const ext = fileName.toLowerCase().split(".").pop() ?? ""
  if (ext === "pdf") return "pdf"
  if (ext === "step" || ext === "stp" || ext === "iges" || ext === "igs" || ext === "stl") return "step"
  return null
}
