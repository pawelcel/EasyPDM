import { useRef } from "react"

import {
  Combobox,
  ComboboxChip,
  ComboboxChips,
  ComboboxChipsInput,
  ComboboxContent,
  ComboboxEmpty,
  ComboboxItem,
  ComboboxList,
  ComboboxValue,
} from "@/components/ui/combobox"
import { Hint } from "@/components/ui/hint"
import { useProcesses } from "@/features/processes/use-processes"
import { useLanguage } from "@/i18n/use-language"

// Wybór obróbek z katalogu (menu "Obróbka") — kilka albo żadna. Pole jest dynamiczne: wpisany
// tekst zawęża listę, wybrane obróbki stoją w polu jako etykiety z krzyżykiem. Opis obróbki
// stoi przy niej na liście i pod polem przy każdej wybranej — sama nazwa ("Malowanie") często
// nie mówi, o które malowanie chodzi.
//
// Lista pokazuje tylko obróbki dla danego typu elementu, a obok nich te już wybrane, których
// w katalogu nie ma (usunięte albo przemianowane): zostają widoczne i da się je odznaczyć,
// zamiast znikać po cichu. Wspólne dla panelu elementu i okna tworzenia.
function ProcessSelect({
  target,
  value,
  onChange,
  disabled = false,
}: {
  target: "part" | "assembly"
  value: string[]
  onChange: (next: string[]) => void
  disabled?: boolean
}) {
  const { t } = useLanguage()
  const { processes } = useProcesses()
  const anchor = useRef<HTMLDivElement>(null)
  const catalog = processes.filter((p) => (target === "part" ? p.forParts : p.forAssemblies)).map((p) => p.name)
  const outside = value.filter((name) => !catalog.includes(name))
  const items = [...catalog, ...outside]
  const descriptionOf = (name: string) => processes.find((p) => p.name === name)?.description ?? null

  if (items.length === 0) return <Hint>{t("part.processesEmptyCatalog")}</Hint>

  return (
    <div className="flex flex-col gap-1">
      <Combobox
        multiple
        items={items}
        value={value}
        disabled={disabled}
        // Escape ma tylko zamknąć listę. Combobox z wieloma wartościami czyści nim domyślnie cały
        // wybór, co przy zamykaniu listy kasowało świeżo wybrane obróbki.
        onValueChange={(v, details) => {
          if (details.reason !== "escape-key") onChange(v as string[])
        }}
      >
        <ComboboxChips ref={anchor} className="w-full">
          <ComboboxValue>
            {(values: string[]) => (
              <>
                {values.map((name) => (
                  <ComboboxChip key={name}>{name}</ComboboxChip>
                ))}
                <ComboboxChipsInput placeholder={values.length === 0 ? t("part.processesSearch") : undefined} />
              </>
            )}
          </ComboboxValue>
        </ComboboxChips>
        <ComboboxContent anchor={anchor}>
          <ComboboxEmpty>{t("part.processesNoMatch")}</ComboboxEmpty>
          <ComboboxList>
            {(name: string) => (
              <ComboboxItem key={name} value={name}>
                <span className="flex min-w-0 flex-col">
                  <span>
                    {name}
                    {outside.includes(name) && (
                      <span className="text-muted-foreground"> ({t("part.processesOutside")})</span>
                    )}
                  </span>
                  {descriptionOf(name) && (
                    <span className="truncate text-[12px] text-muted-foreground">{descriptionOf(name)}</span>
                  )}
                </span>
              </ComboboxItem>
            )}
          </ComboboxList>
        </ComboboxContent>
      </Combobox>
      {value.length > 0 && (
        <ul className="flex flex-col gap-0.5 text-[12.5px]">
          {value.map((name) => (
            <li key={name}>
              <span className="font-medium">{name}</span>
              {descriptionOf(name) && (
                <span className="whitespace-pre-line text-muted-foreground"> — {descriptionOf(name)}</span>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

export { ProcessSelect }
