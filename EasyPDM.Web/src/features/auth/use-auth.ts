import { useContext } from "react"

import { AuthContext } from "@/features/auth/auth-context"

export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error("useAuth musi być użyte wewnątrz <AuthProvider>.")
  return ctx
}
