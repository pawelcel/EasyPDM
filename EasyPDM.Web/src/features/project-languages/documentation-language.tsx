import type { ReactNode } from "react"

import { DocumentationLanguageContext } from "@/features/project-languages/documentation-language-context"

function DocumentationLanguageProvider({ languageId, children }: { languageId: number | undefined; children: ReactNode }) {
  return <DocumentationLanguageContext.Provider value={languageId}>{children}</DocumentationLanguageContext.Provider>
}

export { DocumentationLanguageProvider }
