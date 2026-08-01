import { cn } from "@/lib/utils"

// Lista rozwija się dalej w prawo od głównego paska ikon, gdy aktywna jest zakładka
// "Ustawienia" — to miejsce na kolejne sekcje ustawień w przyszłości.
interface SettingsOption {
  id: string
  label: string
}

const SETTINGS_OPTIONS: SettingsOption[] = [
  { id: "users", label: "Użytkownicy" },
  { id: "storage", label: "Magazyn plików" },
]

function SettingsSidebar({
  activeId,
  onSelect,
}: {
  activeId: string
  onSelect: (id: string) => void
}) {
  return (
    <div className="flex w-44 shrink-0 flex-col gap-0.5 border-r bg-card p-1.5">
      <div className="px-2 py-1.5 text-[11.5px] font-medium tracking-wide text-muted-foreground uppercase">
        Ustawienia
      </div>
      {SETTINGS_OPTIONS.map((opt) => (
        <button
          key={opt.id}
          type="button"
          onClick={() => onSelect(opt.id)}
          className={cn(
            "rounded-md px-2 py-1.5 text-left text-sm text-muted-foreground hover:bg-accent hover:text-foreground",
            activeId === opt.id && "bg-accent text-foreground"
          )}
        >
          {opt.label}
        </button>
      ))}
    </div>
  )
}

export { SettingsSidebar }
