import { ArrowUpRight } from "lucide-react"
import { useEffect, useState } from "react"

import { api } from "@/api/client"
import { itemDisplayLabel, type UsedInEntry } from "@/api/types"
import { Button } from "@/components/ui/button"
import { Hint } from "@/components/ui/hint"
import { SectionLabel } from "@/components/ui/section-label"
import { useLanguage } from "@/i18n/use-language"

// "Gdzie używane" — odwrotność BOM-u: wszystkie złożenia (na dowolnej głębokości), do
// których ten element pośrednio albo bezpośrednio należy. Tylko do odczytu, z jednym
// przyciskiem nawigacyjnym per wiersz.
function UsedInPanel({
  itemId,
  onSelectChild,
}: {
  itemId: string
  onSelectChild?: (id: string, parentId: string | null | undefined) => void
}) {
  const { t } = useLanguage()
  const [entries, setEntries] = useState<UsedInEntry[]>([])

  useEffect(() => {
    let cancelled = false
    api
      .getUsedIn(itemId)
      .then((result) => {
        if (!cancelled) setEntries(result)
      })
      .catch(() => {
        if (!cancelled) setEntries([])
      })
    return () => {
      cancelled = true
    }
  }, [itemId])

  return (
    <>
      <SectionLabel>{t("usedIn.title")}</SectionLabel>
      {entries.length === 0 ? (
        <Hint>{t("usedIn.empty")}</Hint>
      ) : (
        // Ten sam limit "max ~5 wierszy + przewijanie" co ItemHistoryPanel.
        <ul className="flex max-h-[7.5rem] flex-col gap-1 overflow-y-auto">
          {entries.map((entry) => (
            <li key={entry.id} className="flex items-center gap-2 text-[12.5px]">
              {/* min-w-0 jest konieczne, żeby "truncate" na dziecku flex w ogóle zadziałało —
                  bez tego etykieta nie kurczy się poniżej swojej naturalnej szerokości i albo
                  wypycha nazwę projektu/przycisk poza panel, albo się z nimi nie mieści.
                  Nazwa projektu ma "shrink-0", żeby to ZAWSZE ona była widoczna w całości, a
                  przycinała się (jeśli trzeba) tylko sama etykieta elementu. */}
              <span className="min-w-0 flex-1 truncate">{itemDisplayLabel(entry)}</span>
              {entry.projectName && (
                <span className="shrink-0 text-muted-foreground">({entry.projectName})</span>
              )}
              {onSelectChild && (
                <Button
                  type="button"
                  size="icon-xs"
                  onClick={() => onSelectChild(entry.id, undefined)}
                  aria-label={t("item.goToItemAria")}
                  title={t("item.goToItemAria")}
                >
                  <ArrowUpRight />
                </Button>
              )}
            </li>
          ))}
        </ul>
      )}
    </>
  )
}

export { UsedInPanel }
