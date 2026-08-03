import { Moon, Sun } from "lucide-react"

import { Button } from "@/components/ui/button"
import { useLanguage } from "@/i18n/use-language"
import { useTheme } from "@/theme/use-theme"

function ThemeToggle() {
  const { theme, setTheme } = useTheme()
  const { t } = useLanguage()

  return (
    <div className="flex gap-1.5">
      <Button
        size="sm"
        variant={theme === "light" ? "default" : "outline"}
        onClick={() => setTheme("light")}
      >
        <Sun className="size-3.5" /> {t("appearance.light")}
      </Button>
      <Button
        size="sm"
        variant={theme === "dark" ? "default" : "outline"}
        onClick={() => setTheme("dark")}
      >
        <Moon className="size-3.5" /> {t("appearance.dark")}
      </Button>
    </div>
  )
}

export { ThemeToggle }
