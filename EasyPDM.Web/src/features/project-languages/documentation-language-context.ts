import { createContext } from "react"

// Język dokumentacji, w którym pokazuje się dane elementu (np. obróbki) — główny język projektu,
// w którego kontekście element jest oglądany. Ustawia go panel elementu albo okno tworzenia;
// poza nimi (albo dla elementu bez projektu) obowiązuje domyślny język dokumentacji z ustawień.
export const DocumentationLanguageContext = createContext<number | undefined>(undefined)
