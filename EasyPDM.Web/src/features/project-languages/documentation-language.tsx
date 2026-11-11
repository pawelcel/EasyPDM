import type { ReactNode } from "react"

import { DocumentationLanguageContext } from "@/features/project-languages/documentation-language-context"

function DocumentationLanguageProvider({
  languageId,
  languageIds,
  children,
}: {
  languageId: number | undefined
  languageIds?: number[]
  children: ReactNode
}) {
  return (
    <DocumentationLanguageContext.Provider value={{ languageId, languageIds }}>{children}</DocumentationLanguageContext.Provider>
  )
}

export { DocumentationLanguageProvider }
