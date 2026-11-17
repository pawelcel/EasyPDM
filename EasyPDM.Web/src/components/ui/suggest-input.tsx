import { Autocomplete } from "@base-ui/react/autocomplete"

import { ComboboxContent, ComboboxEmpty, ComboboxInput, ComboboxList } from "@/components/ui/combobox"
import { Hint } from "@/components/ui/hint"
import { useLanguage } from "@/i18n/use-language"
import { cn } from "@/lib/utils"

// Pole tekstowe z podpowiedziami: można wpisać dowolną wartość, ale istniejące pojawiają się
// od razu po kliknięciu i zawężają przy pisaniu. Zastępuje natywny <datalist>, który pokazywał
// listę dopiero po wpisaniu znaku/podwójnym kliknięciu, a w Firefoksie wewnątrz okna
// dialogowego bywał zawodny. Celowo Autocomplete, nie Combobox: wartością Autocomplete jest sam
// tekst pola, więc nowa, jeszcze nieistniejąca wartość jest tak samo dozwolona jak wybrana z
// listy. Combobox pamięta wybrany element i przy wyjściu z pola przywracał jego nazwę, kasując
// to, co wpisano później. Lista, okienko i pusty stan to te same części co w Comboboxie.
function SuggestInput({
  id,
  value,
  onChange,
  suggestions,
  placeholder,
  newValueHint,
  nearDuplicateHint = true,
  onPick,
  onEnter,
  disabled,
  inputClassName,
}: {
  id?: string
  value: string
  onChange: (value: string) => void
  suggestions: string[]
  placeholder?: string
  // Tekst pokazywany w liście, gdy wpisana wartość nie pasuje do żadnej istniejącej
  // ("zostanie dodana nowa grupa") — żeby było jasne, że to nie błąd, tylko nowa pozycja.
  newValueHint?: string
  // Podpowiedź "użyj istniejącej pisowni" ma sens tam, gdzie wartość ma trafić w istniejącą
  // pozycję (grupa materiału). Tam, gdzie tworzy się NOWĄ pozycję (nazwa obróbki), zachęta do
  // wybrania istniejącej prowadzi prosto w duplikat — takie miejsce ma własny komunikat.
  nearDuplicateHint?: boolean
  // Called when a suggestion is chosen from the list (a click, or Enter on a highlighted one),
  // for fields where choosing is the action itself (adding a tag), not just filling the field.
  onPick?: (value: string) => void
  // Enter with no suggestion highlighted. Deferred a tick, so a pick made by the same Enter
  // (the list "clicks" the highlighted item) has already reached onPick.
  onEnter?: () => void
  disabled?: boolean
  inputClassName?: string
}) {
  const { t } = useLanguage()
  const trimmed = value.trim()
  // Ta sama nazwa różniąca się tylko wielkością liter lub spacjami to prawie zawsze pomyłka
  // ("stal" obok "Stal" rozbiłoby filtr na dwie grupy) — podpowiadamy istniejącą pisownię.
  const nearDuplicate =
    trimmed && !suggestions.includes(trimmed)
      ? suggestions.find((s) => s.trim().toLocaleLowerCase() === trimmed.toLocaleLowerCase())
      : undefined

  return (
    <div className="flex flex-col gap-1">
      <Autocomplete.Root
        items={suggestions}
        value={value}
        onValueChange={(next, details) => {
          onChange(next)
          if (details.reason === "item-press") onPick?.(next)
        }}
        openOnInputClick
      >
        <ComboboxInput
          id={id}
          placeholder={placeholder}
          className={cn("w-full", inputClassName)}
          showTrigger={false}
          disabled={disabled}
          onKeyDown={
            onEnter
              ? (e) => {
                  if (e.key === "Enter") setTimeout(onEnter, 0)
                }
              : undefined
          }
        />
        <ComboboxContent>
          <ComboboxEmpty>{trimmed ? newValueHint : null}</ComboboxEmpty>
          <ComboboxList>
            {(s: string) => (
              <Autocomplete.Item
                key={s}
                value={s}
                className="relative flex w-full cursor-default items-center rounded-md px-1.5 py-1 text-sm outline-hidden select-none data-highlighted:bg-accent data-highlighted:text-accent-foreground"
              >
                {s}
              </Autocomplete.Item>
            )}
          </ComboboxList>
        </ComboboxContent>
      </Autocomplete.Root>
      {nearDuplicateHint && nearDuplicate && (
        <Hint>
          {t("suggestInput.nearDuplicate", { name: nearDuplicate })}{" "}
          <button type="button" className="underline" onClick={() => onChange(nearDuplicate)}>
            {t("suggestInput.useExisting")}
          </button>
        </Hint>
      )}
    </div>
  )
}

export { SuggestInput }
