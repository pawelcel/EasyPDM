export type PreviewKind = "pdf" | "image"

// Tylko formaty, dla których mamy podgląd w przeglądarce — reszta dostaje wyłącznie
// przycisk pobierania.
//
// STEP/IGES/STL CELOWO nie są już podglądalne. Renderowaliśmy je przez occt-import-js
// (OpenCascade w WebAssembly) i three.js, żeby otrzymać NIERUCHOMY obraz — bez obracania,
// jedno renderer.render(). Cena była płacona przy każdym otwarciu, u każdego użytkownika:
// pobranie bryły, teselacja i liczenie krawędzi dla każdej bryły. Model pokazujemy teraz
// jako gotowy zrzut PNG, który makro CAD robi raz, przy wysyłce (rola załącznika "image").
// Sam STEP wgrywa się bez zmian i jest do pobrania — po prostu nie służy do wyświetlania.
export function previewKindOf(fileName: string): PreviewKind | null {
  const ext = fileName.toLowerCase().split(".").pop() ?? ""
  if (ext === "pdf") return "pdf"
  if (ext === "png" || ext === "jpg" || ext === "jpeg" || ext === "webp" || ext === "gif") return "image"
  return null
}
