import { X } from "lucide-react"

import type { ProcessRef } from "@/api/types"
import { Button } from "@/components/ui/button"
import {
  Combobox,
  ComboboxContent,
  ComboboxEmpty,
  ComboboxInput,
  ComboboxItem,
  ComboboxList,
} from "@/components/ui/combobox"
import { Hint } from "@/components/ui/hint"
import { useProcesses } from "@/features/processes/use-processes"
import { useLanguage } from "@/i18n/use-language"

interface Entry {
  key: string
  ref: ProcessRef
  outside: boolean
}

// Wybór obróbek z katalogu (menu "Obróbka") — kilka albo żadna. Pole zawsze mówi "Kliknij, aby
// wybrać": kliknięcie otwiera listę, wpisany tekst ją zawęża (po nazwie i opisie), a kliknięcie
// pozycji ją zaznacza albo odznacza. Wybrane obróbki NIE stoją w samym polu, tylko pod nim, z
// opisem i krzyżykiem do usunięcia — tak, jak chciał użytkownik; ta sama nazwa ("Hartowanie")
// bywa w katalogu kilka razy i dopiero opis mówi, o który wariant chodzi.
//
// Pozycje rozróżnia identyfikator, nie nazwa. Obróbka już wybrana zachowuje swoją kopię nazwy
// i opisu; nowo wybrana bierze je z katalogu. Wybrane, których w katalogu nie ma (usunięte),
// zostają widoczne z dopiskiem i da się je odznaczyć. Wspólne dla panelu elementu i okna
// tworzenia.
function ProcessSelect({
  target,
  value,
  onChange,
  disabled = false,
}: {
  target: "part" | "assembly"
  value: ProcessRef[]
  onChange: (next: ProcessRef[]) => void
  disabled?: boolean
}) {
  const { t } = useLanguage()
  const { processes } = useProcesses()

  const available = processes.filter((p) => (target === "part" ? p.forParts : p.forAssemblies))
  const inCatalog = (ref: ProcessRef) => ref.id !== null && available.some((p) => p.id === ref.id)
  const selectedKeys = value.map((ref, i) => (inCatalog(ref) ? `c${ref.id}` : `s${i}`))
  const entries: Entry[] = [
    ...available.map((p) => {
      const chosen = value.find((ref) => ref.id === p.id)
      return { key: `c${p.id}`, ref: chosen ?? { id: p.id, name: p.name, description: p.description }, outside: false }
    }),
    ...value.flatMap((ref, i) => (inCatalog(ref) ? [] : [{ key: `s${i}`, ref, outside: true }])),
  ]
  const byKey = new Map(entries.map((e) => [e.key, e]))

  if (entries.length === 0) return <Hint>{t("part.processesEmptyCatalog")}</Hint>

  return (
    <div className="flex flex-col gap-1">
      <Combobox
        multiple
        openOnInputClick
        items={entries.map((e) => e.key)}
        value={selectedKeys}
        disabled={disabled}
        itemToStringLabel={(key: string) => {
          const ref = byKey.get(key)?.ref
          return ref ? [ref.name, ref.description].filter(Boolean).join(" ") : key
        }}
        // Escape ma tylko zamknąć listę. Combobox z wieloma wartościami czyści nim domyślnie cały
        // wybór, co przy zamykaniu listy kasowało świeżo wybrane obróbki.
        onValueChange={(keys, details) => {
          if (details.reason === "escape-key") return
          onChange((keys as string[]).flatMap((key) => (byKey.has(key) ? [byKey.get(key)!.ref] : [])))
        }}
      >
        <ComboboxInput className="w-full" placeholder={t("part.processesPick")} disabled={disabled} />
        <ComboboxContent>
          <ComboboxEmpty>{t("part.processesNoMatch")}</ComboboxEmpty>
          <ComboboxList>
            {(key: string) => {
              const entry = byKey.get(key)
              if (!entry) return null
              return (
                <ComboboxItem key={key} value={key}>
                  <span className="flex min-w-0 flex-col">
                    <span>
                      {entry.ref.name}
                      {entry.outside && (
                        <span className="text-muted-foreground"> ({t("part.processesOutside")})</span>
                      )}
                    </span>
                    {entry.ref.description && (
                      <span className="truncate text-[12px] text-muted-foreground">{entry.ref.description}</span>
                    )}
                  </span>
                </ComboboxItem>
              )
            }}
          </ComboboxList>
        </ComboboxContent>
      </Combobox>
      {value.length > 0 && (
        <ul className="flex flex-col gap-0.5 text-sm">
          {value.map((ref, i) => (
            <li key={selectedKeys[i]} className="flex items-start gap-1.5">
              <Button
                size="icon-xs"
                variant="ghost"
                disabled={disabled}
                aria-label={t("common.deleteNamed", { name: ref.name })}
                onClick={() => onChange(value.filter((_, j) => j !== i))}
              >
                <X className="size-3.5 text-muted-foreground" />
              </Button>
              <span className="pt-px">
                <span className="font-medium">{ref.name}</span>
                {ref.description && (
                  <span className="whitespace-pre-line text-muted-foreground"> — {ref.description}</span>
                )}
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

export { ProcessSelect }
