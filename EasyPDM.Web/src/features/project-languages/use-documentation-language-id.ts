import { useContext } from "react"

import { DocumentationLanguageContext } from "@/features/project-languages/documentation-language-context"
import { useProjectLanguages } from "@/features/project-languages/use-project-languages"

// Główny język projektu z DocumentationLanguageProvider, a bez niego — domyślny język dokumentacji.
export function useDocumentationLanguageId(): number | undefined {
  const fromContext = useContext(DocumentationLanguageContext)
  const { languages } = useProjectLanguages()
  return fromContext ?? languages.find((l) => l.isDefault)?.id
}
