import { useEffect, useState } from "react"

import { api, ApiError } from "@/api/client"
import { Button } from "@/components/ui/button"
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { FormError } from "@/components/ui/form-error"
import { Hint } from "@/components/ui/hint"
import { clearPendingDeviceLogin, usePendingDeviceLogin } from "@/features/auth/pending-device-login"
import { useLanguage } from "@/i18n/use-language"

// "Program X on a computer asks to sign in as you" -- Allow issues it its own macro token
// (listed under Settings -> Users), Deny tells it no. It shows the program's name and the code
// the program displays, so a request someone else started is recognisable and can be refused.
function DeviceLoginDialog() {
  const { t } = useLanguage()
  const code = usePendingDeviceLogin()
  const [client, setClient] = useState<string | null>(null)
  const [error, setError] = useState("")
  const [busy, setBusy] = useState(false)
  const [done, setDone] = useState<"allowed" | null>(null)

  useEffect(() => {
    if (!code) return
    setClient(null)
    setError("")
    setDone(null)
    api
      .getDeviceLogin(code)
      .then((r) => setClient(r.client))
      .catch((err) => setError(err instanceof ApiError ? err.message : t("deviceLogin.failed")))
  }, [code, t])

  if (!code) return null

  async function answer(allow: boolean) {
    setBusy(true)
    setError("")
    try {
      if (allow) {
        await api.approveDeviceLogin(code!)
        setDone("allowed")
      } else {
        await api.denyDeviceLogin(code!)
        clearPendingDeviceLogin()
      }
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t("deviceLogin.failed"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dialog open>
      <DialogContent showCloseButton={false}>
        <DialogHeader>
          <DialogTitle>{t("deviceLogin.title")}</DialogTitle>
        </DialogHeader>
        {done ? (
          <Hint>{t("deviceLogin.allowed", { client: client ?? "" })}</Hint>
        ) : (
          <div className="flex flex-col gap-2 text-sm">
            {client && <p>{t("deviceLogin.question", { client })}</p>}
            <p>
              {t("deviceLogin.code")} <span className="font-mono text-base font-semibold">{code}</span>
            </p>
            <Hint>{t("deviceLogin.hint")}</Hint>
            <FormError>{error}</FormError>
          </div>
        )}
        <DialogFooter>
          {done ? (
            <Button onClick={clearPendingDeviceLogin}>{t("common.close")}</Button>
          ) : (
            <>
              <Button variant="outline" onClick={() => (client ? answer(false) : clearPendingDeviceLogin())} disabled={busy}>
                {t("deviceLogin.deny")}
              </Button>
              <Button onClick={() => answer(true)} disabled={busy || !client}>
                {t("deviceLogin.allow")}
              </Button>
            </>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export { DeviceLoginDialog }
