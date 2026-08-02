import { useContext } from "react"

import { LanguageContext } from "@/i18n/language-context"

export function useLanguage() {
  const ctx = useContext(LanguageContext)
  if (!ctx) throw new Error("useLanguage musi być użyte wewnątrz <LanguageProvider>.")
  return ctx
}
