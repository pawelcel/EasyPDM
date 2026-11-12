import { useState } from "react"

import { api } from "@/api/client"
import type { Project, ProjectLanguage } from "@/api/types"
import { FormError } from "@/components/ui/form-error"
import { Label } from "@/components/ui/label"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { useProjectLanguages } from "@/features/project-languages/use-project-languages"
import { useLanguage } from "@/i18n/use-language"

function languageLabel(lang: ProjectLanguage | undefined) {
  return lang ? `${lang.name} (${lang.code})` : ""
}

// Języki dokumentacji projektu: język główny (każdy projekt, na start domyślny z ustawień),
// przełącznik "wielojęzyczny" i — gdy włączony — lista języków projektu. Każda zmiana
// zapisuje się od razu osobnym PATCH /api/projects/{id}/languages, niezależnym od zapisu
// pól formularza (zob. komentarz przy tym endpoincie). Język główny jest na liście zawsze,
// więc jego pole jest zaznaczone i zablokowane.
function ProjectLanguagesField({
  project,
  isAdmin,
  onUpdated,
}: {
  project: Project
  isAdmin: boolean
  onUpdated: () => void | Promise<void>
}) {
  const { t } = useLanguage()
  const { languages } = useProjectLanguages()
  const [pending, setPending] = useState(false)
  const [error, setError] = useState("")

  // Kliknięcie widać od razu: do czasu odpowiedzi serwera i odświeżenia projektu pola pokazują
  // wysłany stan, a nie poprzedni. Przy błędzie wracają do tego, co jest w projekcie.
  const [sent, setSent] = useState<{ mainLanguageId: number; multilingual: boolean; languageIds: number[] } | null>(null)

  async function save(mainLanguageId: number, multilingual: boolean, languageIds: number[]) {
    setPending(true)
    setError("")
    setSent({ mainLanguageId, multilingual, languageIds })
    try {
      await api.setProjectLanguages(project.id, { mainLanguageId, multilingual, languageIds })
      await onUpdated()
    } catch (err) {
      setError(err instanceof Error ? err.message : t("project.saveFailed"))
    } finally {
      setSent(null)
      setPending(false)
    }
  }

  const shown = sent ?? project
  const main = shown.mainLanguageId
  const selected = new Set(shown.languageIds)
  const disabled = !isAdmin || pending

  return (
    <div className="flex flex-col gap-2">
      <Label htmlFor="project-main-language">{t("project.mainLanguage")}</Label>
      <Select
        value={String(main)}
        disabled={disabled}
        onValueChange={(v) => v && save(Number(v), shown.multilingual, shown.languageIds)}
      >
        <SelectTrigger id="project-main-language" className="w-full">
          <SelectValue>{(v: string) => languageLabel(languages.find((l) => String(l.id) === v))}</SelectValue>
        </SelectTrigger>
        <SelectContent>
          {languages.map((lang) => (
            <SelectItem key={lang.id} value={String(lang.id)}>
              {languageLabel(lang)}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>

      <label className="flex cursor-pointer items-center gap-2 text-sm">
        <input
          type="checkbox"
          checked={shown.multilingual}
          disabled={disabled}
          onChange={(e) => save(main, e.target.checked, shown.languageIds)}
          className="size-3.5 shrink-0 accent-primary"
        />
        {t("project.multilingual")}
      </label>

      {shown.multilingual && (
        <div className="flex flex-col gap-1 pl-5">
          {languages.map((lang) => {
            const isMain = lang.id === main
            return (
              <label
                key={lang.id}
                className={`flex items-center gap-2 text-sm ${isMain || disabled ? "" : "cursor-pointer"}`}
              >
                <input
                  type="checkbox"
                  checked={isMain || selected.has(lang.id)}
                  disabled={isMain || disabled}
                  onChange={(e) =>
                    save(
                      main,
                      true,
                      e.target.checked
                        ? [...shown.languageIds, lang.id]
                        : shown.languageIds.filter((id) => id !== lang.id)
                    )
                  }
                  className="size-3.5 shrink-0 accent-primary"
                />
                <span className="w-9 shrink-0 font-mono text-[13px] font-semibold">{lang.code}</span>
                <span>{lang.name}</span>
                {isMain && <span className="text-[12.5px] text-muted-foreground">{t("project.mainLanguageNote")}</span>}
              </label>
            )
          })}
        </div>
      )}

      <FormError>{error}</FormError>
    </div>
  )
}

export { ProjectLanguagesField }
