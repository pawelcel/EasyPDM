import { useContext } from "react"

import {
  DocumentationLanguageContext,
  type DocumentationLanguageValue,
} from "@/features/project-languages/documentation-language-context"

// Główny język, w którym pokazują się dane oglądanego elementu (zob. DocumentationLanguageProvider).
// Poza dostawcą undefined — teksty pokazują się wtedy w swojej wersji domyślnej.
export function useDocumentationLanguageId(): number | undefined {
  return useContext(DocumentationLanguageContext).main?.id
}

// Główny język oglądanego elementu i języki, na które można tłumaczyć jego dane.
export function useDocumentationLanguages(): DocumentationLanguageValue {
  return useContext(DocumentationLanguageContext)
}
