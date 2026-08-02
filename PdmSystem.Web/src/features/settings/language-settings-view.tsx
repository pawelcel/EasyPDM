import { Hint } from "@/components/ui/hint"
import { SectionLabel } from "@/components/ui/section-label"
import { LanguageSelect } from "@/i18n/language-select"
import { useLanguage } from "@/i18n/use-language"

function LanguageSettingsView() {
  const { t } = useLanguage()

  return (
    <div className="mx-auto max-w-2xl">
      <h2 className="mb-4 text-lg font-semibold tracking-tight">{t("language.title")}</h2>

      <div className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
        <SectionLabel>{t("language.title")}</SectionLabel>
        <Hint>{t("language.description")}</Hint>
        <div className="mt-2">
          <LanguageSelect className="w-40" />
        </div>
      </div>
    </div>
  )
}

export { LanguageSettingsView }
