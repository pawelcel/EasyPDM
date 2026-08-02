import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import type { Project } from "@/api/types"
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

  return (
    <Select
      value={value || "all"}
      onValueChange={(v) => onChange(v === "all" ? "" : (v as string))}
    >
      <SelectTrigger className="min-w-44">
        <SelectValue>
          {(v: string) => {
            const project = projects.find((p) => p.id === v)
            return project ? `${project.name} (${project.itemCount})` : t("project.allProjects")
          }}
        </SelectValue>
      </SelectTrigger>
      <SelectContent>
        <SelectItem value="all">{t("project.allProjects")}</SelectItem>
        {projects.map((p) => (
          <SelectItem key={p.id} value={p.id}>
            {p.name} ({p.itemCount})
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

export { ProjectSelect }
