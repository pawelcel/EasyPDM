import { useMemo, type ReactNode } from "react"

import { DocumentationLanguageContext } from "@/features/project-languages/documentation-language-context"
import { useProjectLanguages } from "@/features/project-languages/use-project-languages"

// languageId — główny język projektu; bez niego (element bez projektu) obowiązuje domyślny język
// dokumentacji. languageIds — języki projektu wielojęzycznego; brak = wszystkie języki.
function DocumentationLanguageProvider({
  languageId,
  languageIds,
  children,
}: {
  languageId: number | undefined
  languageIds?: number[]
  children: ReactNode
}) {
  const { languages } = useProjectLanguages()
  const value = useMemo(() => {
    const main = languages.find((l) => l.id === languageId) ?? languages.find((l) => l.isDefault)
    const others = languages.filter((l) => l.id !== main?.id && (!languageIds || languageIds.includes(l.id)))
    return { main, others }
  }, [languages, languageId, languageIds])
  return <DocumentationLanguageContext.Provider value={value}>{children}</DocumentationLanguageContext.Provider>
}

export { DocumentationLanguageProvider }
