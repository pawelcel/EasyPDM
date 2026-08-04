import { createContext } from "react"

export type Theme = "light" | "dark"

export interface ThemeContextValue {
  theme: Theme
  setTheme: (theme: Theme) => void
}

export const ThemeContext = createContext<ThemeContextValue | null>(null)
