import { RefreshCw, X } from "lucide-react"

import { localizeProcess, type Process, type ProcessRef } from "@/api/types"
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
import { useDocumentationLanguageId } from "@/features/project-languages/use-documentation-language-id"
import { useLanguage } from "@/i18n/use-language"

// Czy kopia obróbki w elemencie różni się od jej wpisu w katalogu (nazwa, opis albo tłumaczenia).
// Kolejność tłumaczeń bez znaczenia; brak opisu i pusty opis to to samo.
function differsFromCatalog(ref: ProcessRef, entry: Process): boolean {
  const norm = (texts: { languageId: number; name: string; description: string | null }[] | undefined) =>
    JSON.stringify(
      [...(texts ?? [])]
        .sort((a, b) => a.languageId - b.languageId)
        .map((x) => [x.languageId, x.name, x.description || null])
    )
  return (
    ref.name !== entry.name ||
    (ref.description || null) !== (entry.description || null) ||
    norm(ref.texts) !== norm(entry.texts)
  )
}

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
  // Lista i wybrane pokazują się w głównym języku projektu (zob. DocumentationLanguageProvider).
  const languageId = useDocumentationLanguageId()
  const shown = (ref: ProcessRef) => localizeProcess(ref, languageId)

  const available = processes.filter((p) => (target === "part" ? p.forParts : p.forAssemblies))
  const inCatalog = (ref: ProcessRef) => ref.id !== null && available.some((p) => p.id === ref.id)
  const selectedKeys = value.map((ref, i) => (inCatalog(ref) ? `c${ref.id}` : `s${i}`))
  const entries: Entry[] = [
    ...available.map((p) => {
      const chosen = value.find((ref) => ref.id === p.id)
      // Nowo wybrana bierze z katalogu wersję domyślną i kopię wszystkich tłumaczeń.
      const picked: ProcessRef = { id: p.id, name: p.name, description: p.description, texts: p.texts }
      return { key: `c${p.id}`, ref: chosen ?? picked, outside: false }
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
          if (!ref) return key
          const { name, description } = shown(ref)
          return [name, description].filter(Boolean).join(" ")
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
              const text = shown(entry.ref)
              return (
                <ComboboxItem key={key} value={key}>
                  <span className="flex min-w-0 flex-col">
                    <span>
                      {text.name}
                      {entry.outside && (
                        <span className="text-muted-foreground"> ({t("part.processesOutside")})</span>
                      )}
                    </span>
                    {text.description && (
                      <span className="truncate text-[12px] text-muted-foreground">{text.description}</span>
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
          {value.map((ref, i) => {
            const text = shown(ref)
            // Element trzyma kopię obróbki z chwili wyboru; jeśli w katalogu poprawiono opis albo
            // dodano tłumaczenie, można ją odświeżyć — tylko ręcznie i tylko gdy pole jest
            // edytowalne (element w pracy), więc wydana specyfikacja nie zmienia się po cichu.
            const catalogEntry = ref.id !== null ? processes.find((p) => p.id === ref.id) : undefined
            const stale = !disabled && catalogEntry !== undefined && differsFromCatalog(ref, catalogEntry)
            return (
            <li key={selectedKeys[i]} className="flex items-start gap-1.5">
              <Button
                size="icon-xs"
                variant="ghost"
                disabled={disabled}
                aria-label={t("common.deleteNamed", { name: text.name })}
                onClick={() => onChange(value.filter((_, j) => j !== i))}
              >
                <X className="size-3.5 text-muted-foreground" />
              </Button>
              <span className="pt-px">
                <span className="font-medium">{text.name}</span>
                {text.description && (
                  <span className="whitespace-pre-line text-muted-foreground"> — {text.description}</span>
                )}
              </span>
              {stale && catalogEntry && (
                <Button
                  size="icon-xs"
                  variant="ghost"
                  className="ml-auto shrink-0"
                  aria-label={t("part.processesRefresh", { name: text.name })}
                  title={t("part.processesRefreshHint")}
                  onClick={() =>
                    onChange(
                      value.map((r, j) =>
                        j === i
                          ? { id: catalogEntry.id, name: catalogEntry.name, description: catalogEntry.description, texts: catalogEntry.texts }
                          : r
                      )
                    )
                  }
                >
                  <RefreshCw className="size-3.5 text-amber-600 dark:text-amber-400" />
                </Button>
              )}
            </li>
            )
          })}
        </ul>
      )}
    </div>
  )
}

export { ProcessSelect }
