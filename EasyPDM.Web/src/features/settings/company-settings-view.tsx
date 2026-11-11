import { useCallback, useEffect, useRef, useState } from "react"
import { Trash2, Upload } from "lucide-react"

import { api } from "@/api/client"
import type { CompanyProfile } from "@/api/types"
import { Button } from "@/components/ui/button"
import { FormError } from "@/components/ui/form-error"
import { Hint } from "@/components/ui/hint"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { SectionLabel } from "@/components/ui/section-label"
import { useLanguage } from "@/i18n/use-language"

// Ustawienia -> Firma (administrator): nazwa i logo firmy, które makro CAD wstawi do tabelki
// rysunkowej. Logo PNG/JPEG do 2 MB; serwer sprawdza rodzaj po zawartości pliku.
function CompanySettingsView() {
  const { t } = useLanguage()
  const [company, setCompany] = useState<CompanyProfile | null>(null)
  const [name, setName] = useState("")
  const [error, setError] = useState("")
  const [savedHint, setSavedHint] = useState(false)
  const [pending, setPending] = useState(false)
  const fileInput = useRef<HTMLInputElement>(null)

  const refetch = useCallback(async () => {
    try {
      const data = await api.getCompany()
      setCompany(data)
      setName(data.name ?? "")
    } catch {
      setError(t("database.loadError"))
    }
  }, [t])

  useEffect(() => {
    refetch()
  }, [refetch])

  async function saveName() {
    if (!company || name.trim() === (company.name ?? "")) return
    setError("")
    try {
      await api.setCompanyName(name.trim())
      await refetch()
      setSavedHint(true)
      window.setTimeout(() => setSavedHint(false), 2000)
    } catch (err) {
      setError(err instanceof Error ? err.message : t("company.saveFailed"))
    }
  }

  async function upload(file: File) {
    setPending(true)
    setError("")
    try {
      await api.uploadCompanyLogo(file)
      await refetch()
    } catch (err) {
      setError(err instanceof Error ? err.message : t("company.logoFailed"))
    } finally {
      setPending(false)
      if (fileInput.current) fileInput.current.value = ""
    }
  }

  async function removeLogo() {
    setPending(true)
    setError("")
    try {
      await api.removeCompanyLogo()
      await refetch()
    } catch (err) {
      setError(err instanceof Error ? err.message : t("company.logoFailed"))
    } finally {
      setPending(false)
    }
  }

  return (
    <div className="mx-auto max-w-2xl">
      <h2 className="mb-4 text-lg font-semibold tracking-tight">{t("settings.company")}</h2>

      <div className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
        <Hint>{t("company.hint")}</Hint>

        <div className="mt-3 flex flex-col gap-1.5">
          <Label htmlFor="company-name">{t("company.nameLabel")}</Label>
          <Input
            id="company-name"
            value={name}
            disabled={!company}
            placeholder={t("company.namePlaceholder")}
            onChange={(e) => setName(e.target.value)}
            onBlur={saveName}
            onKeyDown={(e) => {
              if (e.key === "Enter") (e.target as HTMLInputElement).blur()
            }}
          />
          {savedHint && <Hint>{t("company.saved")}</Hint>}
        </div>
      </div>

      <div className="mt-4 rounded-xl bg-card p-4 ring-1 ring-foreground/10">
        <SectionLabel>{t("company.logoLabel")}</SectionLabel>
        <Hint>{t("company.logoHint")}</Hint>

        <div className="mt-3 flex items-center gap-4">
          <div className="flex size-28 shrink-0 items-center justify-center rounded-lg bg-white ring-1 ring-foreground/10">
            {company?.hasLogo ? (
              <img
                src={api.companyLogoUrl(company.logoUpdatedAt)}
                alt={t("company.logoLabel")}
                className="max-h-24 max-w-24 object-contain"
              />
            ) : (
              <span className="text-[12.5px] text-neutral-500">{t("company.noLogo")}</span>
            )}
          </div>
          <div className="flex flex-col gap-2">
            <input
              ref={fileInput}
              type="file"
              accept="image/png,image/jpeg"
              className="hidden"
              onChange={(e) => {
                const file = e.target.files?.[0]
                if (file) void upload(file)
              }}
            />
            <Button variant="outline" disabled={pending || !company} onClick={() => fileInput.current?.click()}>
              <Upload className="size-4" />
              {company?.hasLogo ? t("company.replaceLogo") : t("company.uploadLogo")}
            </Button>
            {company?.hasLogo && (
              <Button variant="ghost" disabled={pending} onClick={removeLogo}>
                <Trash2 className="size-4" />
                {t("company.removeLogo")}
              </Button>
            )}
          </div>
        </div>
      </div>

      <div className="mt-2">
        <FormError>{error}</FormError>
      </div>
    </div>
  )
}

export { CompanySettingsView }
