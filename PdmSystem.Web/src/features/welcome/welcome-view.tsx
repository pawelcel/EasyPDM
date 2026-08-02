import { Database, FolderKanban } from "lucide-react"

import { Card, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { useLanguage } from "@/i18n/use-language"

function WelcomeView({ onNavigate }: { onNavigate: (view: "projects" | "database") => void }) {
  const { t } = useLanguage()

  return (
    <div className="mx-auto flex max-w-2xl flex-col gap-6 py-10 text-center">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">{t("welcome.heading")}</h1>
        <p className="mt-1.5 text-sm text-muted-foreground">{t("welcome.subheading")}</p>
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        <button type="button" className="text-left" onClick={() => onNavigate("projects")}>
          <Card className="h-full transition-colors hover:bg-accent">
            <CardHeader>
              <FolderKanban className="mb-1 size-5 text-primary" />
              <CardTitle>{t("welcome.projectsTitle")}</CardTitle>
              <CardDescription>{t("welcome.projectsDescription")}</CardDescription>
            </CardHeader>
          </Card>
        </button>

        <button type="button" className="text-left" onClick={() => onNavigate("database")}>
          <Card className="h-full transition-colors hover:bg-accent">
            <CardHeader>
              <Database className="mb-1 size-5 text-primary" />
              <CardTitle>{t("welcome.databaseTitle")}</CardTitle>
              <CardDescription>{t("welcome.databaseDescription")}</CardDescription>
            </CardHeader>
          </Card>
        </button>
      </div>
    </div>
  )
}

export { WelcomeView }
