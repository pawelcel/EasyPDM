import { useCallback, useEffect, useState, type ReactNode } from "react"

import { ThemeContext, type Theme } from "@/theme/theme-context"

const STORAGE_KEY = "pdm_theme"

function readStoredTheme(): Theme {
  const stored = localStorage.getItem(STORAGE_KEY)
  return stored === "light" ? "light" : "dark"
}

function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setThemeState] = useState<Theme>(readStoredTheme)

  // Ten sam efekt odpala się też od razu przy montowaniu — index.html ma inline-owy skrypt,
  // który ustawia klasę JESZCZE PRZED tym renderem (unika mignięcia złym motywem przy
  // starcie), a to tylko utrzymuje ją zsynchronizowaną z reactowym stanem od tego momentu dalej.
  useEffect(() => {
    document.documentElement.classList.toggle("dark", theme === "dark")
  }, [theme])

  const setTheme = useCallback((next: Theme) => {
    setThemeState(next)
    localStorage.setItem(STORAGE_KEY, next)
  }, [])

  return <ThemeContext.Provider value={{ theme, setTheme }}>{children}</ThemeContext.Provider>
}

export { ThemeProvider }
