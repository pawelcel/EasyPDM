import { ArrowUpRight } from "lucide-react"

import type { ClientProjectSummary } from "@/api/types"
import { Button } from "@/components/ui/button"
import { Hint } from "@/components/ui/hint"
import { SectionLabel } from "@/components/ui/section-label"
import { useLanguage } from "@/i18n/use-language"

// The projects of a client or of one of its second names, each with a jump to the project.
// Shared by both panels so they list them the same way.
function ClientProjectsList({
  projects,
  emptyHint,
  onNavigateToProject,
}: {
  projects: ClientProjectSummary[]
  emptyHint: string
  onNavigateToProject?: (id: string) => void
}) {
  const { t } = useLanguage()
  return (
    <div>
      <SectionLabel>{t("client.projectsLabel")}</SectionLabel>
      {projects.length > 0 ? (
        <ul className="mt-1 flex max-h-[7.5rem] flex-col gap-1 overflow-y-auto">
          {projects.map((p) => (
            <li key={p.id} className="flex items-center gap-2 text-[12.5px]">
              <span className="min-w-0 flex-1 truncate">{p.name}</span>
              {onNavigateToProject && (
                <Button
                  type="button"
                  size="icon-xs"
                  onClick={() => onNavigateToProject(p.id)}
                  aria-label={t("client.goToProjectAria")}
                  title={t("client.goToProjectAria")}
                >
                  <ArrowUpRight />
                </Button>
              )}
            </li>
          ))}
        </ul>
      ) : (
        <Hint>{emptyHint}</Hint>
      )}
    </div>
  )
}

export { ClientProjectsList }
