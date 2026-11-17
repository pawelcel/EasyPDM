import { useRef, useState } from "react"

import { Button } from "@/components/ui/button"
import { FormError } from "@/components/ui/form-error"
import { Input } from "@/components/ui/input"
import { SuggestInput } from "@/components/ui/suggest-input"
import { cn } from "@/lib/utils"
import { useLanguage } from "@/i18n/use-language"

// Używane też poza tagami (typy produktów producenta) — stąd podmienialny placeholder;
// reszta zachowania (czyszczenie pola dopiero po udanym zapisie, komunikat błędu z
// wyjątku rzuconego przez onAdd) jest identyczna niezależnie od tego, co się dodaje.
function AddTagRow({
  onAdd,
  className,
  placeholder,
  disabled,
  suggestions,
}: {
  onAdd: (name: string) => void | Promise<void>
  className?: string
  placeholder?: string
  disabled?: boolean
  // Existing values to suggest while typing (the tags already in use); a new value can still
  // be typed. Without it the field is a plain input, as for manufacturer product types.
  suggestions?: string[]
}) {
  const { t } = useLanguage()
  const [value, setValue] = useState("")
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState("")
  // Refs as well as state: choosing a suggestion with Enter both picks it (onPick) and, a tick
  // later, reaches onEnter, which must see that the pick is already being added.
  const valueRef = useRef("")
  const submittingRef = useRef(false)

  function change(next: string) {
    valueRef.current = next
    setValue(next)
  }

  // Czyścimy pole i pokazujemy błąd DOPIERO po zakończeniu onAdd — wcześniej (przed tą
  // poprawką) pole czyściło się od razu, niezależnie od wyniku, więc nieudane dodanie
  // (np. tag już istnieje) cicho gubiło to, co użytkownik wpisał, bez żadnego komunikatu.
  async function submit(override?: string) {
    const trimmed = (override ?? valueRef.current).trim()
    if (!trimmed || submittingRef.current) return
    submittingRef.current = true
    setSubmitting(true)
    setError("")
    try {
      await onAdd(trimmed)
      change("")
    } catch (err) {
      setError(err instanceof Error ? err.message : t("item.addTagFailed"))
    } finally {
      submittingRef.current = false
      setSubmitting(false)
    }
  }

  return (
    <div className={cn("flex flex-col gap-1", className)}>
      <div className="flex gap-1.5">
        {suggestions ? (
          <div className="min-w-0 flex-1">
            <SuggestInput
              value={value}
              onChange={change}
              suggestions={suggestions}
              placeholder={placeholder ?? t("item.newTagPlaceholder")}
              newValueHint={t("item.newTagHint")}
              onPick={(picked) => submit(picked)}
              onEnter={() => submit()}
              disabled={submitting || disabled}
              inputClassName="h-7 text-[13px]"
            />
          </div>
        ) : (
          <Input
            value={value}
            onChange={(e) => change(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") submit()
            }}
            placeholder={placeholder ?? t("item.newTagPlaceholder")}
            className="h-7 text-[13px]"
            disabled={submitting || disabled}
          />
        )}
        <Button size="sm" variant="secondary" onClick={() => submit()} disabled={submitting || disabled}>
          {t("common.add")}
        </Button>
      </div>
      <FormError className="text-[12.5px]">{error}</FormError>
    </div>
  )
}

export { AddTagRow }
