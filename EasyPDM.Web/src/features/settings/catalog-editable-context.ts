import { createContext, useContext } from "react"

// Czy bieżący użytkownik może zmieniać katalogi (Producenci, Klienci i ich podpanele). App
// ustawia false, gdy włączona jest opcja "katalogi zmienia tylko administrator", a to nie
// administrator — przyciski dodawania, edycji i usuwania znikają, a pola stają się tylko do
// odczytu. Kontekst zamiast propsa, bo te kontrolki siedzą kilka komponentów w głąb.
// Domyślnie true: poza dostawcą (np. w testach komponentu) nic się nie chowa, a regułę i tak
// egzekwuje serwer.
export const CatalogEditableContext = createContext(true)

export function useCatalogEditable() {
  return useContext(CatalogEditableContext)
}
