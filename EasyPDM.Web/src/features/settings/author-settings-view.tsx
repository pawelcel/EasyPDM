import { SectionLabel } from "@/components/ui/section-label"
import { useLanguage } from "@/i18n/use-language"
import { APP_VERSION } from "@/version"

function AuthorSettingsView() {
  const { t } = useLanguage()

  return (
    <div className="mx-auto max-w-2xl">
      <h2 className="mb-4 text-lg font-semibold tracking-tight">{t("author.title")}</h2>

      <div className="flex flex-col gap-4 rounded-xl bg-card p-4 ring-1 ring-foreground/10">
        <div>
          <SectionLabel>EasyPDM</SectionLabel>
          <div className="text-sm">
            {t("author.appVersionLabel")}: v{APP_VERSION}
          </div>
        </div>

        <div>
          <SectionLabel>{t("author.originatorLabel")}</SectionLabel>
          <div className="text-sm">Paweł Celmer</div>
        </div>

        <div>
          <SectionLabel>{t("author.licenseLabel")}</SectionLabel>
          <div className="text-sm">MIT</div>
        </div>

        <div>
          <SectionLabel>{t("author.repoLabel")}</SectionLabel>
          <a
            className="text-sm text-primary hover:underline"
            href="https://github.com/pawelcel/EasyPDM"
            target="_blank"
            rel="noreferrer"
          >
            github.com/pawelcel/EasyPDM
          </a>
        </div>
      </div>
    </div>
  )
}

export { AuthorSettingsView }
