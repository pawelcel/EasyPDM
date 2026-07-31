import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import type { Project } from "@/api/types"

function ProjectSelect({
  projects,
  value,
  onChange,
}: {
  projects: Project[]
  value: string
  onChange: (projectId: string) => void
}) {
  return (
    <Select
      value={value || "all"}
      onValueChange={(v) => onChange(v === "all" ? "" : (v as string))}
    >
      <SelectTrigger className="min-w-44">
        <SelectValue>
          {(v: string) => {
            const project = projects.find((p) => p.id === v)
            return project ? `${project.name} (${project.itemCount})` : "Wszystkie projekty"
          }}
        </SelectValue>
      </SelectTrigger>
      <SelectContent>
        <SelectItem value="all">Wszystkie projekty</SelectItem>
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
