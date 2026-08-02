import { FolderKanban } from "lucide-react"

import type { Project } from "@/api/types"
import { cn } from "@/lib/utils"
import { useLanguage } from "@/i18n/use-language"

function ProjectRow({
  project,
  selected,
  onSelect,
}: {
  project: Project
  selected: boolean
  onSelect: () => void
}) {
  const { t } = useLanguage()

  return (
    <button
      type="button"
      onClick={onSelect}
      className={cn(
        "flex items-center gap-2 rounded-md px-2 py-1.5 text-left text-sm hover:bg-accent",
        selected && "bg-accent"
      )}
    >
      <FolderKanban className="size-4 shrink-0 text-muted-foreground" />
      <div className="min-w-0 flex-1">
        <div className="truncate font-medium">{project.name}</div>
        <div className="truncate text-[12px] text-muted-foreground">
          {t("itemType.project")} · {project.itemCount}{" "}
          {t(project.itemCount === 1 ? "project.itemSingular" : "project.itemPlural")}
        </div>
      </div>
    </button>
  )
}

export { ProjectRow }
