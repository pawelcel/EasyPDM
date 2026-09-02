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
  { id: "logs", labelKey: "settings.logs", adminOnly: true },
  { id: "naming", labelKey: "settings.naming", adminOnly: true },
  { id: "notifications", labelKey: "settings.notifications" },
  { id: "appearance", labelKey: "settings.appearance" },
  { id: "language", labelKey: "settings.language" },
  { id: "author", labelKey: "settings.author" },
]

function SettingsSidebar({
  activeId,
  isAdmin,
  myLabel,
  onSelect,
}: {
  activeId: string
  isAdmin: boolean
  myLabel: string
  onSelect: (id: string) => void
}) {
  const { t } = useLanguage()
  const options = SETTINGS_OPTIONS.filter((opt) => !opt.adminOnly || isAdmin)
  // "Autor" ma być OSTATNIĄ pozycją na liście — "Moje projekty" (dynamiczna etykieta,
  // spoza SETTINGS_OPTIONS bo to nazwa zalogowanego użytkownika, nie stały klucz
  // tłumaczenia) wstawiamy tuż PRZED nim, zamiast zawsze na końcu.
  const authorIndex = options.findIndex((opt) => opt.id === "author")
  const entries = [
    ...options.slice(0, authorIndex).map((opt) => ({ id: opt.id, label: t(opt.labelKey) })),
    { id: "myProjects", label: myLabel },
    ...options.slice(authorIndex).map((opt) => ({ id: opt.id, label: t(opt.labelKey) })),
  ]

  return (
    <div className="flex w-44 shrink-0 flex-col gap-0.5 border-r bg-card p-1.5">
      <div className="px-2 py-1.5 text-[11.5px] font-medium tracking-wide text-muted-foreground uppercase">
        {t("nav.settings")}
      </div>
      {entries.map((entry) => (
        <button
          key={entry.id}
          type="button"
          onClick={() => onSelect(entry.id)}
          className={cn(
            "truncate rounded-md px-2 py-1.5 text-left text-sm text-muted-foreground hover:bg-accent hover:text-foreground",
            activeId === entry.id && "bg-accent text-foreground"
          )}
        >
          {entry.label}
        </button>
      ))}

      {/* Odstęp mniej więcej wysokości jednej pozycji, żeby "Wesprzyj" wizualnie odróżniało
          się od zwykłych sekcji ustawień powyżej. */}
      <button
        type="button"
        onClick={() => onSelect("support")}
        className={cn(
          "mt-7 truncate rounded-md px-2 py-1.5 text-left text-sm text-muted-foreground hover:bg-accent hover:text-foreground",
          activeId === "support" && "bg-accent text-foreground"
        )}
      >
        {t("settings.support")}
      </button>
    </div>
  )
}

export { SettingsSidebar }
