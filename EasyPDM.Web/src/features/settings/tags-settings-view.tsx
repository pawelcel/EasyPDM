import { useCallback, useEffect, useState } from "react"
import { Trash2 } from "lucide-react"

import { api } from "@/api/client"
import type { TagUsage } from "@/api/types"
import { Button } from "@/components/ui/button"
import { ConfirmDialog } from "@/components/ui/confirm-dialog"
import { Hint } from "@/components/ui/hint"
import { Input } from "@/components/ui/input"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { useLanguage } from "@/i18n/use-language"

type UsageFilter = "all" | "unused"

// Ustawienia -> Tagi (tylko administrator). Przy elemencie da się zdjąć tag tylko z niego, a
// sam tag zostawał w bazie na zawsze — w podpowiedziach i filtrze "Całej bazy", nawet gdy nikt
// go już nie nosił. Tu widać wszystkie tagi z liczbą elementów i można usunąć tag globalnie.
function TagsSettingsView({ onTagDeleted }: { onTagDeleted: (name: string) => Promise<void> }) {
  const { t } = useLanguage()
  const [tags, setTags] = useState<TagUsage[] | null>(null)
  const [loadError, setLoadError] = useState(false)
  const [search, setSearch] = useState("")
  const [usageFilter, setUsageFilter] = useState<UsageFilter>("all")
  const [confirming, setConfirming] = useState<TagUsage | null>(null)
  const [pending, setPending] = useState(false)
  const [deleteError, setDeleteError] = useState<string | null>(null)

  const refetch = useCallback(async () => {
    try {
      setTags(await api.getTagUsage())
      setLoadError(false)
    } catch {
      setLoadError(true)
    }
  }, [])

  useEffect(() => {
    refetch()
  }, [refetch])

  async function confirmDelete() {
    if (!confirming) return
    setPending(true)
    setDeleteError(null)
    try {
      await api.deleteTagEverywhere(confirming.name)
      const name = confirming.name
      setConfirming(null)
      await refetch()
      await onTagDeleted(name)
    } catch (err) {
      setDeleteError(err instanceof Error ? err.message : t("tagAdmin.deleteFailed"))
    } finally {
      setPending(false)
    }
  }

  const query = search.trim().toLowerCase()
  const visible = (tags ?? []).filter(
    (tag) =>
      (!query || tag.name.toLowerCase().includes(query)) &&
      (usageFilter === "all" || tag.itemCount === 0)
  )
  const filterLabel = (v: UsageFilter) => (v === "unused" ? t("tagAdmin.unusedOnly") : t("tagAdmin.filterAll"))

  return (
    <div className="mx-auto max-w-2xl">
      <h2 className="mb-4 text-lg font-semibold tracking-tight">{t("settings.tags")}</h2>

      <div className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
        <Hint>{t("tagAdmin.hint")}</Hint>

        {loadError ? (
          <div className="mt-3">
            <Hint>{t("database.loadError")}</Hint>
          </div>
        ) : (
          <>
            <div className="mt-3 mb-3 flex flex-wrap items-center gap-2">
              <Input
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder={t("tagAdmin.searchPlaceholder")}
                className="min-w-48 flex-1"
              />
              <Select value={usageFilter} onValueChange={(v) => setUsageFilter(v as UsageFilter)}>
                <SelectTrigger className="min-w-44">
                  <SelectValue>{(v: UsageFilter) => filterLabel(v)}</SelectValue>
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">{filterLabel("all")}</SelectItem>
                  <SelectItem value="unused">{filterLabel("unused")}</SelectItem>
                </SelectContent>
              </Select>
            </div>

            {tags === null ? null : visible.length > 0 ? (
              <ul className="flex flex-col gap-0.5">
                {visible.map((tag) => (
                  <li
                    key={tag.name}
                    className="flex items-center gap-2 rounded-md px-2 py-1.5 text-sm hover:bg-accent"
                  >
                    <Button
                      size="icon-xs"
                      variant="ghost"
                      aria-label={t("common.deleteNamed", { name: tag.name })}
                      onClick={() => {
                        setDeleteError(null)
                        setConfirming(tag)
                      }}
                    >
                      <Trash2 className="size-3.5 text-muted-foreground" />
                    </Button>
                    <span className="truncate">{tag.name}</span>
                    <span className="ml-auto shrink-0 text-[12.5px] text-muted-foreground">
                      {tag.itemCount === 0
                        ? t("tagAdmin.unused")
                        : t("tagAdmin.itemCount", { count: String(tag.itemCount) })}
                    </span>
                  </li>
                ))}
              </ul>
            ) : (
              <Hint>{tags.length === 0 ? t("tagAdmin.empty") : t("tagAdmin.emptyFiltered")}</Hint>
            )}
          </>
        )}
      </div>

      {confirming && (
        <ConfirmDialog
          open
          title={t("tagAdmin.deleteTitle", { name: confirming.name })}
          description={
            confirming.itemCount === 0
              ? t("tagAdmin.deleteConfirmUnused", { name: confirming.name })
              : t("tagAdmin.deleteConfirm", { name: confirming.name, count: String(confirming.itemCount) })
          }
          confirmLabel={t("common.delete")}
          variant="destructive"
          onConfirm={confirmDelete}
          onCancel={() => setConfirming(null)}
          pending={pending}
          error={deleteError}
        />
      )}
    </div>
  )
}

export { TagsSettingsView }
