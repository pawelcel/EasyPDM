import { useState, type ReactElement } from "react"

import { api } from "@/api/client"
import type { Process, ProjectLanguage } from "@/api/types"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog"
import { FormError } from "@/components/ui/form-error"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Textarea } from "@/components/ui/textarea"
import { useLanguage } from "@/i18n/use-language"

type Draft = Record<number, { name: string; description: string }>

function draftFrom(process: Process): Draft {
  const draft: Draft = {}
  for (const text of process.texts) draft[text.languageId] = { name: text.name, description: text.description ?? "" }
  return draft
}

// Okno tłumaczeń obróbki: na górze wersja w domyślnym języku dokumentacji, pod nią nazwa i
// opis w każdym innym języku. Zapis wysyła wszystkie języki naraz (PUT
// /api/processes/{id}/texts); język z pustą nazwą to brak tłumaczenia.
function ProcessTranslationsDialog({
  trigger,
  process,
  languages,
  onSaved,
}: {
  trigger: ReactElement
  process: Process
  languages: ProjectLanguage[]
  onSaved: () => Promise<void>
}) {
  const { t } = useLanguage()
  const [open, setOpen] = useState(false)
  const [draft, setDraft] = useState<Draft>(() => draftFrom(process))
  const [error, setError] = useState("")
  const [pending, setPending] = useState(false)

  const defaultLanguage = languages.find((l) => l.isDefault)
  const others = languages.filter((l) => !l.isDefault)

  function set(languageId: number, field: "name" | "description", value: string) {
    setDraft((d) => ({
      ...d,
      [languageId]: { name: d[languageId]?.name ?? "", description: d[languageId]?.description ?? "", [field]: value },
    }))
  }

  async function save() {
    const texts = languages
      .map((l) => ({ languageId: l.id, name: draft[l.id]?.name.trim() ?? "", description: draft[l.id]?.description.trim() || null }))
      .filter((x) => x.name)
    if (texts.length === 0) {
      setError(t("process.nameRequired"))
      return
    }
    setPending(true)
    setError("")
    try {
      await api.setProcessTexts(process.id, texts)
      await onSaved()
      setOpen(false)
    } catch (err) {
      setError(err instanceof Error ? err.message : t("process.saveFailed"))
    } finally {
      setPending(false)
    }
  }

  function fields(lang: ProjectLanguage) {
    const prefix = `process-${process.id}-lang-${lang.id}`
    return (
      <div className="flex flex-col gap-1.5">
        <Label htmlFor={`${prefix}-name`}>{t("process.nameLabel")}</Label>
        <Input
          id={`${prefix}-name`}
          value={draft[lang.id]?.name ?? ""}
          onChange={(e) => set(lang.id, "name", e.target.value)}
        />
        <Label htmlFor={`${prefix}-description`}>{t("process.descriptionLabel")}</Label>
        <Textarea
          id={`${prefix}-description`}
          value={draft[lang.id]?.description ?? ""}
          onChange={(e) => set(lang.id, "description", e.target.value)}
          rows={2}
        />
      </div>
    )
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next)
        if (next) {
          setDraft(draftFrom(process))
          setError("")
        }
      }}
    >
      <DialogTrigger render={trigger} />
      <DialogContent className="max-h-[85vh] overflow-y-auto sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{t("process.translationsTitle", { name: process.name })}</DialogTitle>
        </DialogHeader>

        {defaultLanguage && (
          <section className="rounded-lg bg-muted/50 p-3">
            <div className="mb-2 flex items-center gap-2 text-sm font-semibold">
              <span className="font-mono">{defaultLanguage.code}</span>
              <span>{defaultLanguage.name}</span>
              <span className="text-[12.5px] font-normal text-muted-foreground">
                {t("process.translationsDefaultNote")}
              </span>
            </div>
            {fields(defaultLanguage)}
          </section>
        )}

        {others.length > 0 ? (
          <div className="flex flex-col gap-4">
            {others.map((lang) => (
              <section key={lang.id}>
                <div className="mb-2 flex items-center gap-2 text-sm font-semibold">
                  <span className="font-mono">{lang.code}</span>
                  <span>{lang.name}</span>
                </div>
                {fields(lang)}
              </section>
            ))}
          </div>
        ) : (
          <p className="text-sm text-muted-foreground">{t("process.translationsNoOtherLanguages")}</p>
        )}

        <FormError>{error}</FormError>

        <DialogFooter>
          <Button variant="outline" onClick={() => setOpen(false)}>
            {t("common.cancel")}
          </Button>
          <Button onClick={save} disabled={pending}>
            {t("common.save")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export { ProcessTranslationsDialog }
