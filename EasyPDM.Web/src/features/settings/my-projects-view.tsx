import type { Project } from "@/api/types"
import { Hint } from "@/components/ui/hint"
import { SectionLabel } from "@/components/ui/section-label"
import { useLanguage } from "@/i18n/use-language"

// "projects" pochodzi z GET /api/projects, które już filtruje wg dostępu (admin widzi
// wszystkie, zwykły użytkownik tylko przypisane przez project_users) — ten widok tylko
// wyświetla to, co backend i tak już zwrócił zalogowanemu użytkownikowi.
function MyProjectsView({ displayName, projects }: { displayName: string; projects: Project[] }) {
  const { t } = useLanguage()

  return (
    <div className="mx-auto max-w-2xl">
      <h2 className="mb-4 text-lg font-semibold tracking-tight">{displayName}</h2>

      <div className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
        <SectionLabel>{t("myProjects.title")}</SectionLabel>
        <Hint>{t("myProjects.description")}</Hint>

        {projects.length === 0 ? (
          <Hint>{t("myProjects.empty")}</Hint>
        ) : (
          <ul className="mt-2 flex flex-col gap-0.5">
            {projects.map((p) => (
              <li
                key={p.id}
                className="truncate rounded-md px-2 py-1.5 text-sm hover:bg-accent"
              >
                {p.name} <span className="text-muted-foreground">({p.itemCount})</span>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  )
}

export { MyProjectsView }
