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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Textarea } from "@/components/ui/textarea"
import { useLanguage } from "@/i18n/use-language"

type Draft = Record<number, { name: string; description: string }>

function draftFrom(process: Process): Draft {
  const draft: Draft = {}
  for (const text of process.texts) draft[text.languageId] = { name: text.name, description: text.description ?? "" }
  return draft
}

// Okno tłumaczeń obróbki: na górze wersja w domyślnym języku dokumentacji, pod nią nazwa i
// opis w JEDNYM wybranym z listy innym języku — przy kilkunastu językach osobne pola dla
// każdego robiłyby z okna długą ścianę formularzy. Zmiany we wszystkich przeglądanych
// językach zostają w szkicu i zapis wysyła je naraz (PUT /api/processes/{id}/texts); język
// z pustą nazwą to brak tłumaczenia.
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
  // Na start pierwszy język bez tłumaczenia — to zwykle ten, który przyszło się uzupełnić.
  const firstToEdit = () => (others.find((l) => !draftFrom(process)[l.id]?.name) ?? others[0])?.id ?? null
  const [selectedId, setSelectedId] = useState<number | null>(firstToEdit)
  const selected = others.find((l) => l.id === selectedId) ?? others[0]
  const hasText = (id: number) => !!draft[id]?.name.trim()

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
          setSelectedId(firstToEdit())
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

        {others.length > 0 && selected ? (
          <section className="flex flex-col gap-2">
            <Label htmlFor={`process-${process.id}-translation-language`}>{t("process.translationsLanguage")}</Label>
            <Select value={String(selected.id)} onValueChange={(v) => v && setSelectedId(Number(v))}>
              <SelectTrigger id={`process-${process.id}-translation-language`} className="w-full">
                <SelectValue>
                  {(v: string) => {
                    const lang = others.find((l) => String(l.id) === v)
                    return lang ? <LanguageOption lang={lang} translated={hasText(lang.id)} /> : ""
                  }}
                </SelectValue>
              </SelectTrigger>
              <SelectContent>
                {others.map((lang) => (
                  <SelectItem key={lang.id} value={String(lang.id)}>
                    <LanguageOption lang={lang} translated={hasText(lang.id)} />
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <div key={selected.id}>{fields(selected)}</div>
          </section>
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

// Pozycja listy języków: skrót, nazwa i znacznik, czy tłumaczenie już jest (liczy się szkic,
// więc znacznik pojawia się od razu po wpisaniu nazwy, przed zapisem).
function LanguageOption({ lang, translated }: { lang: ProjectLanguage; translated: boolean }) {
  const { t } = useLanguage()
  return (
    <span className="flex items-center gap-2">
      <span className="w-9 font-mono text-[13px] font-semibold">{lang.code}</span>
      <span>{lang.name}</span>
      <span className={`text-[12px] ${translated ? "text-emerald-600 dark:text-emerald-400" : "text-muted-foreground"}`}>
        {translated ? t("process.translationsDone") : t("process.translationsMissing")}
      </span>
    </span>
  )
}

export { ProcessTranslationsDialog }
