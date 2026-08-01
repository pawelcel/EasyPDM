import { useState } from "react"
import { ChevronLeft, ChevronRight, Database, FolderKanban, Home, Layers, Users, type LucideIcon } from "lucide-react"

import { Button } from "@/components/ui/button"
import { cn } from "@/lib/utils"

interface SidebarOption {
  id: string
  label: string
  icon: LucideIcon
}

const OPTIONS: SidebarOption[] = [
  { id: "welcome", label: "Strona główna", icon: Home },
  { id: "projects", label: "Projekty", icon: FolderKanban },
  { id: "database", label: "Cała baza", icon: Database },
  { id: "materials", label: "Lista materiałów", icon: Layers },
]

const USERS_OPTION: SidebarOption = { id: "users", label: "Użytkownicy", icon: Users }

function AppSidebar({
  activeId,
  onSelect,
  showUsers = false,
}: {
  activeId: string | null
  onSelect: (id: string) => void
  showUsers?: boolean
}) {
  const [expanded, setExpanded] = useState(false)
  const options = showUsers ? [...OPTIONS, USERS_OPTION] : OPTIONS

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
          aria-label={expanded ? "Zwiń pasek" : "Rozwiń pasek"}
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
            title={opt.label}
            className={cn(
              "flex items-center gap-2 rounded-md px-2 py-1.5 text-sm text-muted-foreground hover:bg-accent hover:text-foreground",
              activeId === opt.id && "bg-accent text-foreground"
            )}
          >
            <opt.icon className="size-4 shrink-0" />
            {expanded && <span className="truncate">{opt.label}</span>}
          </button>
        ))}
      </div>
    </div>
  )
}

export { AppSidebar }
