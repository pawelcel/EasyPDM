import { Building2, Database, Factory, FolderKanban, Layers, Settings } from "lucide-react"

import { Card, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import type { TranslationKey } from "@/i18n/translations"
import { useLanguage } from "@/i18n/use-language"

type WelcomeTarget = "projects" | "database" | "materials" | "manufacturers" | "clients" | "settings"

function WelcomeView({ onNavigate }: { onNavigate: (view: WelcomeTarget) => void }) {
  const { t } = useLanguage()

  const tiles: { target: WelcomeTarget; icon: typeof FolderKanban; titleKey: TranslationKey; descriptionKey: TranslationKey }[] = [
    { target: "projects", icon: FolderKanban, titleKey: "welcome.projectsTitle", descriptionKey: "welcome.projectsDescription" },
    { target: "database", icon: Database, titleKey: "welcome.databaseTitle", descriptionKey: "welcome.databaseDescription" },
    { target: "materials", icon: Layers, titleKey: "welcome.materialsTitle", descriptionKey: "welcome.materialsDescription" },
    { target: "manufacturers", icon: Factory, titleKey: "welcome.manufacturersTitle", descriptionKey: "welcome.manufacturersDescription" },
    { target: "clients", icon: Building2, titleKey: "welcome.clientsTitle", descriptionKey: "welcome.clientsDescription" },
    { target: "settings", icon: Settings, titleKey: "welcome.settingsTitle", descriptionKey: "welcome.settingsDescription" },
  ]

  return (
    <div className="mx-auto flex max-w-2xl flex-col gap-6 py-10 text-center">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">{t("welcome.heading")}</h1>
        <p className="mt-1.5 text-sm text-muted-foreground">{t("welcome.subheading")}</p>
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        {tiles.map(({ target, icon: Icon, titleKey, descriptionKey }) => (
          <button key={target} type="button" className="text-left" onClick={() => onNavigate(target)}>
            <Card className="h-full transition-colors hover:bg-accent">
              <CardHeader>
                <Icon className="mb-1 size-5 text-primary" />
                <CardTitle>{t(titleKey)}</CardTitle>
                <CardDescription>{t(descriptionKey)}</CardDescription>
              </CardHeader>
            </Card>
          </button>
        ))}
      </div>
    </div>
  )
}

export { WelcomeView }
