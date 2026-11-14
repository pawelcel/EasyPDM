import { useEffect, useState } from "react"

import { api, ApiError } from "@/api/client"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { FormError } from "@/components/ui/form-error"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { useLanguage } from "@/i18n/use-language"

// Ustawiane przy wylogowaniu, żeby strona logowania nie zalogowała od razu z powrotem przez
// logowanie jednokrotne (Oidc:AutoRedirect); czyszczone przy następnym logowaniu przez SSO.
export const SSO_PAUSED_KEY = "pdm_sso_paused"

// Dokąd SSO odsyła przeglądarkę: na tę samą stronę, z parametrami (np. link z makra CAD).
function ssoStartUrl() {
  const back = window.location.pathname + window.location.search
  return `/api/auth/oidc/start?redirect=${encodeURIComponent(back)}`
}

const SSO_ERRORS: Record<string, "login.ssoNoAccess" | "login.ssoLocalAccount"> = {
  "no-access": "login.ssoNoAccess",
  "local-account": "login.ssoLocalAccount",
}

function LoginView({ onLoggedIn }: { onLoggedIn: () => void | Promise<void> }) {
  const { t } = useLanguage()
  // Logowanie jednokrotne: gdy serwer je ma, nad formularzem jest przycisk; z Oidc:AutoRedirect
  // świeża wizyta idzie od razu do dostawcy. ?local=1 pokazuje sam formularz hasła (lokalne konto
  // awaryjne), tak samo nieudane SSO (?sso-error=...) i wylogowanie w tej karcie.
  const [params] = useState(() => new URLSearchParams(window.location.search))
  const localOnly = params.get("local") === "1"
  const ssoError = params.get("sso-error")
  const [sso, setSso] = useState(false)
  // Dopóki nie wiadomo, czy przekierować do dostawcy, nic nie jest rysowane — logowanie, które
  // przechodzi od razu, nie pokazuje strony logowania.
  const [checking, setChecking] = useState(!localOnly)

  useEffect(() => {
    if (localOnly) return
    api
      .getAuthConfig()
      .then((config) => {
        if (!config.sso) return setChecking(false)
        setSso(true)
        let paused = false
        try {
          paused = sessionStorage.getItem(SSO_PAUSED_KEY) === "1"
        } catch {
          paused = false
        }
        if (config.ssoAutoRedirect && !ssoError && !paused) window.location.assign(ssoStartUrl())
        else setChecking(false)
      })
      .catch(() => {
        setSso(false)
        setChecking(false)
      })
  }, [localOnly, ssoError])
  const [username, setUsername] = useState("")
  const [password, setPassword] = useState("")
  const [error, setError] = useState("")
  const [submitting, setSubmitting] = useState(false)

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    if (!username.trim() || !password) {
      setError(t("login.errorMissingFields"))
      return
    }

    setSubmitting(true)
    setError("")
    try {
      await api.login(username.trim(), password)
      await onLoggedIn()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t("login.errorGeneric"))
    } finally {
      setSubmitting(false)
    }
  }

  if (checking) return <div className="min-h-screen bg-background" aria-busy="true" />

  return (
    <div className="flex min-h-screen items-center justify-center bg-background px-4">
      <Card className="w-full max-w-sm">
        <CardHeader>
          <CardTitle>{t("login.title")}</CardTitle>
          <CardDescription>{t("login.description")}</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          {ssoError && <FormError>{t(SSO_ERRORS[ssoError] ?? "login.ssoFailed")}</FormError>}
          {sso && (
            <Button
              type="button"
              onClick={() => {
                try {
                  sessionStorage.removeItem(SSO_PAUSED_KEY)
                } catch {
                  // pamięć sesji zablokowana: nie ma czego czyścić
                }
                window.location.assign(ssoStartUrl())
              }}
            >
              {t("login.ssoSubmit")}
            </Button>
          )}
          {sso && <p className="text-center text-xs text-muted-foreground">{t("login.localAccount")}</p>}
          <form className="flex flex-col gap-3" onSubmit={handleSubmit}>
            <div className="flex flex-col gap-1">
              <Label htmlFor="login-username">{t("login.username")}</Label>
              <Input
                id="login-username"
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                autoFocus
              />
            </div>
            <div className="flex flex-col gap-1">
              <Label htmlFor="login-password">{t("login.password")}</Label>
              <Input
                id="login-password"
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
              />
            </div>
            <FormError>{error}</FormError>
            <Button type="submit" variant={sso ? "outline" : "default"} disabled={submitting} className="mt-1">
              {submitting ? t("login.submitting") : t("login.submit")}
            </Button>
          </form>
        </CardContent>
      </Card>
    </div>
  )
}

export { LoginView }
