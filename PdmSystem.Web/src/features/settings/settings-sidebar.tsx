import { cn } from "@/lib/utils"
import type { TranslationKey } from "@/i18n/translations"
import { useLanguage } from "@/i18n/use-language"

// Lista rozwija się dalej w prawo od głównego paska ikon, gdy aktywna jest zakładka
// "Ustawienia" — to miejsce na kolejne sekcje ustawień w przyszłości.
interface SettingsOption {
  id: string
  labelKey: TranslationKey
  adminOnly?: boolean
}

const SETTINGS_OPTIONS: SettingsOption[] = [
  { id: "users", labelKey: "settings.users", adminOnly: true },
  { id: "storage", labelKey: "settings.storage", adminOnly: true },
  { id: "appearance", labelKey: "settings.appearance" },
  { id: "language", labelKey: "settings.language" },
]

function SettingsSidebar({
  activeId,
  isAdmin,
  onSelect,
}: {
  activeId: string
  isAdmin: boolean
  onSelect: (id: string) => void
}) {
  const { t } = useLanguage()
  const options = SETTINGS_OPTIONS.filter((opt) => !opt.adminOnly || isAdmin)

  return (
    <div className="flex w-44 shrink-0 flex-col gap-0.5 border-r bg-card p-1.5">
      <div className="px-2 py-1.5 text-[11.5px] font-medium tracking-wide text-muted-foreground uppercase">
        {t("nav.settings")}
      </div>
      {options.map((opt) => (
        <button
          key={opt.id}
          type="button"
          onClick={() => onSelect(opt.id)}
          className={cn(
            "rounded-md px-2 py-1.5 text-left text-sm text-muted-foreground hover:bg-accent hover:text-foreground",
            activeId === opt.id && "bg-accent text-foreground"
          )}
        >
          {t(opt.labelKey)}
        </button>
      ))}
    </div>
  )
}

export { SettingsSidebar }
