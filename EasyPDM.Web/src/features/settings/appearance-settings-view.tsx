import { Hint } from "@/components/ui/hint"
import { SectionLabel } from "@/components/ui/section-label"
import { useLanguage } from "@/i18n/use-language"
import { ThemeToggle } from "@/theme/theme-toggle"

function AppearanceSettingsView() {
  const { t } = useLanguage()

  return (
    <div className="mx-auto max-w-2xl">
      <h2 className="mb-4 text-lg font-semibold tracking-tight">{t("appearance.title")}</h2>

      <div className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
        <SectionLabel>{t("appearance.title")}</SectionLabel>
        <Hint>{t("appearance.description")}</Hint>
        <div className="mt-2">
          <ThemeToggle />
        </div>
      </div>
    </div>
  )
}

export { AppearanceSettingsView }
