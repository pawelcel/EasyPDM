import { createContext } from "react"

// Kontekst języków dokumentacji, w którym oglądany jest element: languageId to główny język
// projektu (w nim pokazują się np. obróbki), languageIds — języki projektu wielojęzycznego
// (je proponuje okno tłumaczeń nazwy; brak = wszystkie języki dokumentacji). Ustawia go panel
// elementu albo okno tworzenia; poza nimi (albo dla elementu bez projektu) obowiązuje domyślny
// język dokumentacji z ustawień.
export interface DocumentationLanguageValue {
  languageId: number | undefined
  languageIds?: number[]
}

export const DocumentationLanguageContext = createContext<DocumentationLanguageValue>({ languageId: undefined })
