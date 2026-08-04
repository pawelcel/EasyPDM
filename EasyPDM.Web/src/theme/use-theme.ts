import { useContext } from "react"

import { ThemeContext } from "@/theme/theme-context"

export function useTheme() {
  const ctx = useContext(ThemeContext)
  if (!ctx) throw new Error("useTheme musi być użyte wewnątrz <ThemeProvider>.")
  return ctx
}
