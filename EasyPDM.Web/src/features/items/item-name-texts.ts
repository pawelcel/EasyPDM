export interface NameText {
  languageId: number
  name: string
}

// Tłumaczenia nazwy elementu w properties.nameTexts ({languageId, name}[]). Sama nazwa
// (fileName) jest w głównym języku projektu i zmienia się zwykłym polem — z jego blokadami
// (status, właściciel, plik CAD), więc tutaj stoi tylko do odczytu.
export function readNameTexts(value: unknown): NameText[] {
  if (!Array.isArray(value)) return []
  return value.flatMap((t): NameText[] =>
    t && typeof t === "object" && typeof t.languageId === "number" && typeof t.name === "string" && t.name.trim()
      ? [{ languageId: t.languageId, name: t.name }]
      : []
  )
}
