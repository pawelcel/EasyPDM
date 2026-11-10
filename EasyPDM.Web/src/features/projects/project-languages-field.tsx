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

  async function save(mainLanguageId: number, multilingual: boolean, languageIds: number[]) {
    setPending(true)
    setError("")
    try {
      await api.setProjectLanguages(project.id, { mainLanguageId, multilingual, languageIds })
      await onUpdated()
    } catch (err) {
      setError(err instanceof Error ? err.message : t("project.saveFailed"))
    } finally {
      setPending(false)
    }
  }

  const main = project.mainLanguageId
  const selected = new Set(project.languageIds)
  const disabled = !isAdmin || pending

  return (
    <div className="flex flex-col gap-2">
      <Label htmlFor="project-main-language">{t("project.mainLanguage")}</Label>
      <Select
        value={String(main)}
        disabled={disabled}
        onValueChange={(v) => v && save(Number(v), project.multilingual, project.languageIds)}
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
          checked={project.multilingual}
          disabled={disabled}
          onChange={(e) => save(main, e.target.checked, project.languageIds)}
          className="size-3.5 shrink-0 accent-primary"
        />
        {t("project.multilingual")}
      </label>

      {project.multilingual && (
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
                        ? [...project.languageIds, lang.id]
                        : project.languageIds.filter((id) => id !== lang.id)
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
