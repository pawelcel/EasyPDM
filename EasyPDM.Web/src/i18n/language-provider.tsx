import { useCallback, useState, type ReactNode } from "react"

import { LanguageContext } from "@/i18n/language-context"
import { translations, type Language, type TranslationKey } from "@/i18n/translations"

const STORAGE_KEY = "pdm_language"

function readStoredLanguage(): Language {
  const stored = localStorage.getItem(STORAGE_KEY)
  return stored !== null && stored in translations ? (stored as Language) : "pl"
}

function LanguageProvider({ children }: { children: ReactNode }) {
  const [language, setLanguageState] = useState<Language>(readStoredLanguage)

  const setLanguage = useCallback((next: Language) => {
    setLanguageState(next)
    localStorage.setItem(STORAGE_KEY, next)
  }, [])

  const t = useCallback(
    (key: TranslationKey, params?: Record<string, string | number>) => {
      const template = translations[language][key]
      if (!params) return template
      return Object.entries(params).reduce(
        (acc, [name, value]) => acc.replaceAll(`{${name}}`, String(value)),
        template
      )
    },
    [language]
  )

  return (
    <LanguageContext.Provider value={{ language, setLanguage, t }}>
      {children}
    </LanguageContext.Provider>
  )
}

export { LanguageProvider }
