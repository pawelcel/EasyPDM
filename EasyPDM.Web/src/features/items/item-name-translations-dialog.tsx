import { useState } from "react"
import { Languages } from "lucide-react"

import { api } from "@/api/client"
import type { Item, ProjectLanguage } from "@/api/types"
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
import { Hint } from "@/components/ui/hint"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { useDocumentationLanguages } from "@/features/project-languages/use-documentation-language-id"
import { readNameTexts } from "@/features/items/item-name-texts"
import { useLanguage } from "@/i18n/use-language"

// Przycisk tłumaczeń przy nazwie Części/Złożenia i okno na wzór tłumaczeń obróbki: na górze
// nazwa w głównym języku projektu, pod nią tłumaczenie na jeden wybrany z listy język.
// Zmiany w kilku językach zostają w szkicu do zapisu; pusta nazwa to brak tłumaczenia.
function ItemNameTranslationsDialog({
  item,
  disabled,
  onChanged,
}: {
  item: Item
  disabled: boolean
  onChanged: () => void | Promise<void>
}) {
  const { t } = useLanguage()
  const { main, others } = useDocumentationLanguages()
  const [open, setOpen] = useState(false)
  const [draft, setDraft] = useState<Record<number, string>>({})
  const [selectedId, setSelectedId] = useState<number | null>(null)
  const [error, setError] = useState("")
  const [pending, setPending] = useState(false)

  const saved = readNameTexts(item.properties.nameTexts)
  const fromSaved = () => Object.fromEntries(saved.map((x) => [x.languageId, x.name])) as Record<number, string>
  const selected = others.find((l) => l.id === selectedId) ?? others[0]
  const hasText = (id: number) => !!draft[id]?.trim()

  async function save() {
    setPending(true)
    setError("")
    try {
      // Języki spoza listy (np. usunięte z projektu wielojęzycznego) zostają, jak były.
      const offered = new Set(others.map((l) => l.id))
      const nameTexts = [
        ...saved.filter((x) => !offered.has(x.languageId)),
        ...others.flatMap((l) => (draft[l.id]?.trim() ? [{ languageId: l.id, name: draft[l.id].trim() }] : [])),
      ]
      await api.updateProperties(item.id, { nameTexts })
      await onChanged()
      setOpen(false)
    } catch (err) {
      setError(err instanceof Error ? err.message : t("item.translationsSaveFailed"))
    } finally {
      setPending(false)
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next)
        if (next) {
          const initial = fromSaved()
          setDraft(initial)
          setSelectedId((others.find((l) => !initial[l.id]) ?? others[0])?.id ?? null)
          setError("")
        }
      }}
    >
      <DialogTrigger
        render={
          <Button
            size="icon"
            variant="outline"
            className="shrink-0"
            aria-label={t("item.nameTranslations")}
            title={t("item.nameTranslations")}
          >
            <Languages className="size-4 text-muted-foreground" />
          </Button>
        }
      />
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{t("item.nameTranslationsTitle", { name: item.fileName })}</DialogTitle>
        </DialogHeader>

        {main && (
          <section className="rounded-lg bg-muted/50 p-3">
            <div className="mb-2 flex items-center gap-2 text-sm font-semibold">
              <span className="font-mono">{main.code}</span>
              <span>{main.name}</span>
              <span className="text-[12.5px] font-normal text-muted-foreground">{t("item.translationsMainNote")}</span>
            </div>
            <Input value={item.fileName} disabled aria-label={t("common.name")} />
          </section>
        )}

        {others.length > 0 && selected ? (
          <section className="flex flex-col gap-2">
            <Label htmlFor={`item-${item.id}-translation-language`}>{t("process.translationsLanguage")}</Label>
            <Select value={String(selected.id)} onValueChange={(v) => v && setSelectedId(Number(v))}>
              <SelectTrigger id={`item-${item.id}-translation-language`} className="w-full">
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
            <Label htmlFor={`item-${item.id}-translation-name`}>{t("common.name")}</Label>
            <Input
              key={selected.id}
              id={`item-${item.id}-translation-name`}
              value={draft[selected.id] ?? ""}
              disabled={disabled}
              onChange={(e) => setDraft((d) => ({ ...d, [selected.id]: e.target.value }))}
            />
          </section>
        ) : (
          <p className="text-sm text-muted-foreground">{t("process.translationsNoOtherLanguages")}</p>
        )}

        {disabled && <Hint>{t("item.translationsLockedHint")}</Hint>}
        <FormError>{error}</FormError>

        <DialogFooter>
          <Button variant="outline" onClick={() => setOpen(false)}>
            {disabled ? t("common.close") : t("common.cancel")}
          </Button>
          {!disabled && (
            <Button onClick={save} disabled={pending || others.length === 0}>
              {t("common.save")}
            </Button>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

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

export { ItemNameTranslationsDialog }
