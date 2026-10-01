import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { projectLabel, type Project } from "@/api/types"
import { useLanguage } from "@/i18n/use-language"

function ProjectSelect({
  projects,
  value,
  onChange,
}: {
  projects: Project[]
  value: string
  onChange: (projectId: string) => void
}) {
  const { t } = useLanguage()
  // Zamknięty projekt znika z listy DO WYBORU -- ale musi zostać w niej, jeśli jest
  // WŁAŚNIE aktualnie wybrany (np. świeżo zamknięty, albo otwarty przez link "przejdź do
  // projektu"), inaczej Base UI Select samo czyści wartość, bo nie znajduje dla niej
  // odpowiadającego SelectItem (potwierdzone w praktyce -- objawiało się jako natychmiastowy
  // powrót do "Wybierz projekt" zaraz po kliknięciu "Zamknij projekt").
  const activeProjects = projects.filter((p) => !p.closed || p.id === value)

  return (
    <Select
      value={value || "none"}
      onValueChange={(v) => onChange(v === "none" ? "" : (v as string))}
    >
      {/* Trzykrotnie szerszy niż dawne min-w-44: nazwa projektu bywa długa, a do tego dochodzi
          klient i druga nazwa w nawiasie (zob. projectLabel), więc ucinało się to, co istotne.
          Pełna szerokość dopiero od "sm" i z max-w-full -- na wąskim oknie min-width nie da się
          zmniejszyć i pole wystawałoby poza ekran; belka jest flex-wrap, więc przyciski po
          prostu schodzą niżej. */}
      <SelectTrigger className="min-w-44 max-w-full sm:min-w-132">
        <SelectValue>
          {(v: string) => {
            const project = projects.find((p) => p.id === v)
            return project ? projectLabel(project) : t("addNode.selectProjectPlaceholder")
          }}
        </SelectValue>
      </SelectTrigger>
      <SelectContent>
        <SelectItem value="none">{t("addNode.selectProjectPlaceholder")}</SelectItem>
        {activeProjects.map((p) => (
          <SelectItem key={p.id} value={p.id}>
            {projectLabel(p)}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

export { ProjectSelect }
