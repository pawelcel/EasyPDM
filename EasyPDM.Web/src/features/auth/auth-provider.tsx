import { useEffect, useState, type ReactNode } from "react"

import { api, UNAUTHORIZED_EVENT } from "@/api/client"
import type { CurrentUser } from "@/api/types"
import { AuthContext } from "@/features/auth/auth-context"

function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<CurrentUser | null>(null)
  const [loading, setLoading] = useState(true)

  async function refetch() {
    try {
      setUser(await api.getMe())
    } catch {
      setUser(null)
    } finally {
      setLoading(false)
    }
  }

  async function logout() {
    try {
      await api.logout()
    } finally {
      setUser(null)
    }
  }

  useEffect(() => {
    refetch()
    // Sesja mogła wygasnąć (albo ktoś inny usunął konto) w trakcie pracy — dowolne wywołanie
    // API zwracające 401 od razu wraca do ekranu logowania, zamiast po cichu psuć resztę UI.
    const onUnauthorized = () => setUser(null)
    window.addEventListener(UNAUTHORIZED_EVENT, onUnauthorized)
    return () => window.removeEventListener(UNAUTHORIZED_EVENT, onUnauthorized)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  return (
    <AuthContext.Provider value={{ user, loading, refetch, logout }}>
      {children}
    </AuthContext.Provider>
  )
}

export { AuthProvider }
