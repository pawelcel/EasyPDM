import { useRef } from "react"

import type { ProcessRef } from "@/api/types"
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

interface Entry {
  key: string
  ref: ProcessRef
  outside: boolean
}

// Wybór obróbek z katalogu (menu "Obróbka") — kilka albo żadna. Pole jest dynamiczne: wpisany
// tekst zawęża listę (po nazwie i opisie), wybrane obróbki stoją w polu jako etykiety z
// krzyżykiem. Opis stoi przy obróbce na liście i pod polem przy każdej wybranej, bo ta sama
// nazwa ("Hartowanie") bywa w katalogu kilka razy z różnym opisem.
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
  const anchor = useRef<HTMLDivElement>(null)

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
  const sameName = (name: string) => entries.filter((e) => e.ref.name === name).length > 1
  const chipLabel = (ref: ProcessRef) =>
    ref.description && sameName(ref.name) ? `${ref.name} · ${ref.description}` : ref.name

  if (entries.length === 0) return <Hint>{t("part.processesEmptyCatalog")}</Hint>

  return (
    <div className="flex flex-col gap-1">
      <Combobox
        multiple
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
        <ComboboxChips ref={anchor} className="w-full">
          <ComboboxValue>
            {(keys: string[]) => (
              <>
                {keys.map((key) => {
                  const ref = byKey.get(key)?.ref
                  return ref ? <ComboboxChip key={key}>{chipLabel(ref)}</ComboboxChip> : null
                })}
                <ComboboxChipsInput placeholder={keys.length === 0 ? t("part.processesSearch") : undefined} />
              </>
            )}
          </ComboboxValue>
        </ComboboxChips>
        <ComboboxContent anchor={anchor}>
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
        <ul className="flex flex-col gap-0.5 text-[12.5px]">
          {value.map((ref, i) => (
            <li key={selectedKeys[i]}>
              <span className="font-medium">{ref.name}</span>
              {ref.description && (
                <span className="whitespace-pre-line text-muted-foreground"> — {ref.description}</span>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

export { ProcessSelect }
