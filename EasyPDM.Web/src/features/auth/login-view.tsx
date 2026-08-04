import { useState } from "react"

import { api, ApiError } from "@/api/client"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { FormError } from "@/components/ui/form-error"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { useLanguage } from "@/i18n/use-language"

function LoginView({ onLoggedIn }: { onLoggedIn: () => void | Promise<void> }) {
  const { t } = useLanguage()
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

  return (
    <div className="flex min-h-screen items-center justify-center bg-background px-4">
      <Card className="w-full max-w-sm">
        <CardHeader>
          <CardTitle>{t("login.title")}</CardTitle>
          <CardDescription>{t("login.description")}</CardDescription>
        </CardHeader>
        <CardContent>
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
            <Button type="submit" disabled={submitting} className="mt-1">
              {submitting ? t("login.submitting") : t("login.submit")}
            </Button>
          </form>
        </CardContent>
      </Card>
    </div>
  )
}

export { LoginView }
