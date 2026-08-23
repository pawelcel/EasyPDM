import { Coffee } from "lucide-react"

import { SectionLabel } from "@/components/ui/section-label"
import { useLanguage } from "@/i18n/use-language"

// Link buycoffee.to autora — stały, wpisany wprost (nie ustawienie do zmiany przez
// użytkownika), zob. README.md dla tego samego linku po stronie GitHuba.
const SUPPORT_URL = "https://buycoffee.to/easypdm"

function SupportSettingsView() {
  const { t } = useLanguage()

  return (
    <div className="mx-auto max-w-2xl">
      <h2 className="mb-4 text-lg font-semibold tracking-tight">{t("support.title")}</h2>

      <div className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
        <SectionLabel>{t("support.heading")}</SectionLabel>
        <p className="text-sm text-muted-foreground">{t("support.description")}</p>

        <a
          href={SUPPORT_URL}
          target="_blank"
          rel="noreferrer"
          className="mt-3 flex items-center gap-3 rounded-xl bg-[#FFDD00] p-4 text-black
            transition-transform hover:scale-[1.01] hover:brightness-95 active:scale-[0.99]"
        >
          <Coffee className="size-8 shrink-0" strokeWidth={1.75} />
          <div className="flex flex-col">
            <span className="font-semibold">{t("support.buttonLabel")}</span>
            <span className="text-sm opacity-80">buycoffee.to/easypdm</span>
          </div>
        </a>
      </div>
    </div>
  )
}

export { SupportSettingsView }
