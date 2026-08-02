import { useState } from "react"
import {
  ChevronLeft,
  ChevronRight,
  Database,
  Factory,
  FolderKanban,
  Home,
  Layers,
  Settings,
  type LucideIcon,
} from "lucide-react"

import { Button } from "@/components/ui/button"
import { cn } from "@/lib/utils"
import type { TranslationKey } from "@/i18n/translations"
import { useLanguage } from "@/i18n/use-language"

interface SidebarOption {
  id: string
  labelKey: TranslationKey
  icon: LucideIcon
}

const OPTIONS: SidebarOption[] = [
  { id: "welcome", labelKey: "nav.welcome", icon: Home },
  { id: "projects", labelKey: "nav.projects", icon: FolderKanban },
  { id: "database", labelKey: "nav.database", icon: Database },
  { id: "materials", labelKey: "nav.materials", icon: Layers },
  { id: "manufacturers", labelKey: "nav.manufacturers", icon: Factory },
]

const SETTINGS_OPTION: SidebarOption = { id: "settings", labelKey: "nav.settings", icon: Settings }

function AppSidebar({
  activeId,
  onSelect,
  showSettings = false,
}: {
  activeId: string | null
  onSelect: (id: string) => void
  showSettings?: boolean
}) {
  const { t } = useLanguage()
  const [expanded, setExpanded] = useState(false)
  const options = showSettings ? [...OPTIONS, SETTINGS_OPTION] : OPTIONS

  return (
    <div
      className={cn(
        "flex shrink-0 flex-col border-r bg-card transition-[width] duration-150",
        expanded ? "w-52" : "w-11"
      )}
    >
      <div className="flex justify-end p-1.5">
        <Button
          size="icon-xs"
          variant="ghost"
          aria-label={t(expanded ? "nav.collapse" : "nav.expand")}
          onClick={() => setExpanded((e) => !e)}
        >
          {expanded ? <ChevronLeft className="size-3.5" /> : <ChevronRight className="size-3.5" />}
        </Button>
      </div>

      <div className="flex flex-col gap-0.5 px-1.5">
        {options.map((opt) => (
          <button
            key={opt.id}
            type="button"
            onClick={() => onSelect(opt.id)}
            title={t(opt.labelKey)}
            className={cn(
              "flex items-center gap-2 rounded-md px-2 py-1.5 text-sm text-muted-foreground hover:bg-accent hover:text-foreground",
              activeId === opt.id && "bg-accent text-foreground"
            )}
          >
            <opt.icon className="size-4 shrink-0" />
            {expanded && <span className="truncate">{t(opt.labelKey)}</span>}
          </button>
        ))}
      </div>
    </div>
  )
}

export { AppSidebar }
