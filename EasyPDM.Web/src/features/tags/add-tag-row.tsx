import { useState } from "react"

import { Button } from "@/components/ui/button"
import { FormError } from "@/components/ui/form-error"
import { Input } from "@/components/ui/input"
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
}: {
  onAdd: (name: string) => void | Promise<void>
  className?: string
  placeholder?: string
  disabled?: boolean
}) {
  const { t } = useLanguage()
  const [value, setValue] = useState("")
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState("")

  // Czyścimy pole i pokazujemy błąd DOPIERO po zakończeniu onAdd — wcześniej (przed tą
  // poprawką) pole czyściło się od razu, niezależnie od wyniku, więc nieudane dodanie
  // (np. tag już istnieje) cicho gubiło to, co użytkownik wpisał, bez żadnego komunikatu.
  async function submit() {
    const trimmed = value.trim()
    if (!trimmed || submitting) return
    setSubmitting(true)
    setError("")
    try {
      await onAdd(trimmed)
      setValue("")
    } catch (err) {
      setError(err instanceof Error ? err.message : t("item.addTagFailed"))
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className={cn("flex flex-col gap-1", className)}>
      <div className="flex gap-1.5">
        <Input
          value={value}
          onChange={(e) => setValue(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter") submit()
          }}
          placeholder={placeholder ?? t("item.newTagPlaceholder")}
          className="h-7 text-[13px]"
          disabled={submitting || disabled}
        />
        <Button size="sm" variant="secondary" onClick={submit} disabled={submitting || disabled}>
          {t("common.add")}
        </Button>
      </div>
      <FormError className="text-[12.5px]">{error}</FormError>
    </div>
  )
}

export { AddTagRow }
