import { useCallback, useEffect, useState } from "react"
import { Trash2 } from "lucide-react"

import { api } from "@/api/client"
import type { MacroToken } from "@/api/types"
import { Button } from "@/components/ui/button"
import { FormError } from "@/components/ui/form-error"
import { Hint } from "@/components/ui/hint"
import { SectionLabel } from "@/components/ui/section-label"
import { useLanguage } from "@/i18n/use-language"

// Zalogowane makra CAD (Ustawienia -> Użytkownicy): każde logowanie makra to osobny token,
// niezależny od sesji przeglądarki. Odwołanie wylogowuje tylko to makro — przy następnej
// operacji poprosi o hasło. Zmiana hasła użytkownika odwołuje wszystkie jego tokeny.
function MacroTokensSection() {
  const { t, language } = useLanguage()
  const [tokens, setTokens] = useState<MacroToken[] | null>(null)
  const [error, setError] = useState("")

  const refetch = useCallback(async () => {
    try {
      setTokens(await api.getMacroTokens())
    } catch {
      setError(t("database.loadError"))
    }
  }, [t])

  useEffect(() => {
    refetch()
  }, [refetch])

  async function revoke(id: string) {
    setError("")
    try {
      await api.revokeMacroToken(id)
      await refetch()
    } catch (err) {
      setError(err instanceof Error ? err.message : t("macroTokens.revokeFailed"))
    }
  }

  const when = (iso: string) => new Date(iso).toLocaleString(language)

  return (
    <div className="mt-4 rounded-xl bg-card p-4 ring-1 ring-foreground/10">
      <SectionLabel>{t("macroTokens.title")}</SectionLabel>
      <Hint>{t("macroTokens.hint")}</Hint>
      {tokens === null ? null : tokens.length === 0 ? (
        <div className="mt-2">
          <Hint>{t("macroTokens.empty")}</Hint>
        </div>
      ) : (
        <ul className="mt-2 flex flex-col gap-0.5">
          {tokens.map((tok) => (
            <li key={tok.id} className="flex items-center gap-2 rounded-md px-2 py-1.5 text-sm hover:bg-accent">
              <div className="min-w-0 flex-1">
                <div className="truncate">
                  <span className="font-medium">{tok.client}</span>
                  <span className="text-muted-foreground">
                    {" "}
                    — {tok.displayName} ({tok.username})
                  </span>
                </div>
                <div className="text-[12.5px] text-muted-foreground">
                  {t("macroTokens.lastUsed", { when: when(tok.lastUsedAt) })} ·{" "}
                  {t("macroTokens.loggedIn", { when: when(tok.createdAt) })}
                </div>
              </div>
              <Button
                size="icon-xs"
                variant="ghost"
                aria-label={t("macroTokens.revokeAria", { client: tok.client, user: tok.username })}
                title={t("macroTokens.revoke")}
                onClick={() => revoke(tok.id)}
              >
                <Trash2 className="size-3.5 text-muted-foreground" />
              </Button>
            </li>
          ))}
        </ul>
      )}
      <FormError>{error}</FormError>
    </div>
  )
}

export { MacroTokensSection }
