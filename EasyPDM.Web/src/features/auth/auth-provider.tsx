import { useEffect, useState, type ReactNode } from "react"

import { api, UNAUTHORIZED_EVENT } from "@/api/client"
import type { CurrentUser } from "@/api/types"
import { AuthContext } from "@/features/auth/auth-context"
import { SSO_PAUSED_KEY } from "@/features/auth/login-view"

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
      sessionStorage.setItem(SSO_PAUSED_KEY, "1")
    } catch {
      // pamięć sesji zablokowana: z Oidc:AutoRedirect strona logowania może zalogować od razu przez SSO
    }
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
