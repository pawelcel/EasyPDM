import type { Process } from "@/api/types"

// Nazwy obróbek w jednym języku dokumentacji, bez powtórzeń — do podpowiedzi przy wpisywaniu
// nazwy w tym języku. Tylko teksty faktycznie w nim zapisane, więc polskie pole nie podpowiada
// angielskich nazw (i odwrotnie), nawet gdy katalog pokazuje obróbkę w języku zastępczym.
// exceptId pomija obróbkę, którą właśnie się edytuje.
export function processNamesIn(processes: Process[], languageId: number | undefined, exceptId?: number) {
  if (languageId === undefined) return []
  const names = new Map<string, string>()
  for (const p of processes) {
    if (p.id === exceptId) continue
    const text = p.texts.find((x) => x.languageId === languageId)
    if (text && !names.has(text.name.toLocaleLowerCase())) names.set(text.name.toLocaleLowerCase(), text.name)
  }
  return [...names.values()].sort((a, b) => a.localeCompare(b))
}
