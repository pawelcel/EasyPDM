import { createContext } from "react"

import type { CurrentUser } from "@/api/types"

export interface AuthContextValue {
  user: CurrentUser | null
  loading: boolean
  refetch: () => Promise<void>
  logout: () => Promise<void>
}

export const AuthContext = createContext<AuthContextValue | null>(null)
