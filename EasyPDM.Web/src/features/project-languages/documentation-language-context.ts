import { createContext } from "react"

import type { ProjectLanguage } from "@/api/types"

// Kontekst języków dokumentacji, w którym oglądany jest element: main to główny język projektu
// (w nim pokazują się np. obróbki), others — języki, na które tłumaczy się jego dane (języki
// projektu wielojęzycznego, a w pozostałych wszystkie języki dokumentacji). Ustawia go panel
// elementu albo okno tworzenia. Lista języków jest pobierana RAZ przez dostawcę, a nie przez
// każdy komponent, który z niej korzysta — wiersz BOM-u też czyta język, więc przy pobieraniu
// w hooku długie zestawienie wysyłało jedno żądanie na wiersz.
export interface DocumentationLanguageValue {
  main: ProjectLanguage | undefined
  others: ProjectLanguage[]
}

export const DocumentationLanguageContext = createContext<DocumentationLanguageValue>({ main: undefined, others: [] })
