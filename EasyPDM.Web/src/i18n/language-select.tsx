import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { LANGUAGE_NATIVE_NAMES, type Language } from "@/i18n/translations"
import { useLanguage } from "@/i18n/use-language"

const LANGUAGES = Object.keys(LANGUAGE_NATIVE_NAMES) as Language[]

// Nazwy języków w tym przełączniku CELOWO nie są tłumaczone przez t() — zawsze pokazują się
// w SWOIM WŁASNYM języku ("Polski"/"English"), tak jak w każdym typowym przełączniku języka,
// żeby użytkownik zawsze mógł znaleźć swój język, nawet jeśli obecny interfejs jest dla niego
// nieczytelny.
function LanguageSelect({ className }: { className?: string }) {
  const { language, setLanguage } = useLanguage()

  return (
    <Select value={language} onValueChange={(v) => setLanguage(v as Language)}>
      <SelectTrigger className={className} size="sm">
        <SelectValue>{(v: string) => LANGUAGE_NATIVE_NAMES[v as Language]}</SelectValue>
      </SelectTrigger>
      <SelectContent>
        {LANGUAGES.map((lang) => (
          <SelectItem key={lang} value={lang}>
            {LANGUAGE_NATIVE_NAMES[lang]}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

export { LanguageSelect }
