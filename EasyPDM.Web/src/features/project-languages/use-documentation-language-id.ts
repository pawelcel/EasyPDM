import { useContext } from "react"

import type { ProjectLanguage } from "@/api/types"
import { DocumentationLanguageContext } from "@/features/project-languages/documentation-language-context"
import { useProjectLanguages } from "@/features/project-languages/use-project-languages"

// Główny język projektu z DocumentationLanguageProvider, a bez niego — domyślny język dokumentacji.
export function useDocumentationLanguageId(): number | undefined {
  return useDocumentationLanguages().main?.id
}

// Główny język oglądanego elementu i języki, na które można tłumaczyć jego dane: języki
// projektu wielojęzycznego, a w pozostałych — wszystkie języki dokumentacji.
export function useDocumentationLanguages(): { main: ProjectLanguage | undefined; others: ProjectLanguage[] } {
  const { languageId, languageIds } = useContext(DocumentationLanguageContext)
  const { languages } = useProjectLanguages()
  const main = languages.find((l) => l.id === languageId) ?? languages.find((l) => l.isDefault)
  const others = languages.filter((l) => l.id !== main?.id && (!languageIds || languageIds.includes(l.id)))
  return { main, others }
}
