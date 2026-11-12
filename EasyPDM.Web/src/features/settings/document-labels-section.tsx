import { useEffect, useState } from "react"

import { api } from "@/api/client"
import type { DocumentLabel } from "@/api/types"
import { Button } from "@/components/ui/button"
import { FormError } from "@/components/ui/form-error"
import { Hint } from "@/components/ui/hint"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { SectionLabel } from "@/components/ui/section-label"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { useProjectLanguages } from "@/features/project-languages/use-project-languages"
import type { TranslationKey } from "@/i18n/translations"
import { useLanguage } from "@/i18n/use-language"

// Podpisy w dokumentach generowanych przez program (na razie nagłówki kolumn eksportu CSV z
// BOM-u), osobno dla każdego języka dokumentacji. Wbudowane są tłumaczenia PL, EN i DE; każde
// można zmienić, a dla innych języków wpisać. Puste pole = podpis wbudowany (pokazany jako
// podpowiedź). Zmienia administrator, pozostali tylko widzą.
function DocumentLabelsSection({ isAdmin }: { isAdmin: boolean }) {
  const { t } = useLanguage()
  const { languages } = useProjectLanguages()
  const [languageId, setLanguageId] = useState<number | null>(null)
  const [labels, setLabels] = useState<DocumentLabel[]>([])
  const [draft, setDraft] = useState<Record<string, string>>({})
  const [error, setError] = useState("")
  const [saved, setSaved] = useState(false)
  const [pending, setPending] = useState(false)

  const selected = languages.find((l) => l.id === languageId) ?? languages.find((l) => l.isDefault)
  const selectedId = selected?.id

  function applyLoaded(list: DocumentLabel[]) {
    setLabels(list)
    setDraft(Object.fromEntries(list.map((l) => [l.key, l.custom ? l.text : ""])))
  }

  useEffect(() => {
    if (selectedId === undefined) return
    let cancelled = false
    api
      .getDocumentLabels(selectedId)
      .then((list) => {
        if (cancelled) return
        applyLoaded(list)
        setError("")
      })
      .catch(() => {
        if (!cancelled) setError(t("database.loadError"))
      })
    return () => {
      cancelled = true
    }
  }, [selectedId, t])

  async function save() {
    if (selectedId === undefined) return
    setPending(true)
    setError("")
    try {
      await api.setDocumentLabels(selectedId, draft)
      applyLoaded(await api.getDocumentLabels(selectedId))
      setSaved(true)
      window.setTimeout(() => setSaved(false), 2000)
    } catch (err) {
      setError(err instanceof Error ? err.message : t("documentLabels.saveFailed"))
    } finally {
      setPending(false)
    }
  }

  if (languages.length === 0) return null

  return (
    <div className="mt-4 rounded-xl bg-card p-4 ring-1 ring-foreground/10">
      <SectionLabel>{t("documentLabels.title")}</SectionLabel>
      <Hint>{t("documentLabels.hint")}</Hint>

      <div className="mt-3 flex flex-col gap-1.5">
        <Label htmlFor="document-labels-language">{t("documentLabels.languageLabel")}</Label>
        <Select value={selected ? String(selected.id) : ""} onValueChange={(v) => v && setLanguageId(Number(v))}>
          <SelectTrigger id="document-labels-language" className="w-64">
            <SelectValue>
              {(v: string) => {
                const lang = languages.find((l) => String(l.id) === v)
                return lang ? `${lang.name} (${lang.code})` : ""
              }}
            </SelectValue>
          </SelectTrigger>
          <SelectContent>
            {languages.map((lang) => (
              <SelectItem key={lang.id} value={String(lang.id)}>
                {lang.name} ({lang.code})
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      <div className="mt-3">
        <div className="mb-1 text-[12.5px] font-medium text-muted-foreground">{t("documentLabels.csvGroup")}</div>
        <div className="flex flex-col gap-1.5">
          {labels.map((l) => (
            <div key={l.key} className="grid grid-cols-[minmax(0,16rem)_1fr] items-center gap-2">
              <span className="text-sm">{t(`documentLabels.key.${l.key}` as TranslationKey)}</span>
              {isAdmin ? (
                <Input
                  aria-label={t(`documentLabels.key.${l.key}` as TranslationKey)}
                  value={draft[l.key] ?? ""}
                  placeholder={l.defaultText}
                  onChange={(e) => setDraft((d) => ({ ...d, [l.key]: e.target.value }))}
                />
              ) : (
                <span className="text-sm">{l.text}</span>
              )}
            </div>
          ))}
        </div>
      </div>

      {isAdmin && (
        <div className="mt-3 flex items-center gap-3">
          <Button onClick={save} disabled={pending || selectedId === undefined}>
            {t("common.save")}
          </Button>
          {saved && <Hint>{t("company.saved")}</Hint>}
        </div>
      )}
      <FormError>{error}</FormError>
    </div>
  )
}

export { DocumentLabelsSection }
