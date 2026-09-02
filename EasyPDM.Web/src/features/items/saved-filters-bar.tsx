import { useCallback, useEffect, useState } from "react"
import { Plus, X } from "lucide-react"

import { api } from "@/api/client"
import type { SavedFilter } from "@/api/types"
import { Button } from "@/components/ui/button"
import {
  Combobox,
  ComboboxContent,
  ComboboxEmpty,
  ComboboxInput,
  ComboboxItem,
  ComboboxList,
} from "@/components/ui/combobox"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog"
import { FormError } from "@/components/ui/form-error"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { coerceDatabaseFilters, type DatabaseFilters } from "@/features/items/database-filters"
import { useLanguage } from "@/i18n/use-language"

function SavedFiltersBar({
  currentFilters,
  onApply,
}: {
  currentFilters: DatabaseFilters
  onApply: (filters: DatabaseFilters) => void
}) {
  const { t } = useLanguage()
  const [savedFilters, setSavedFilters] = useState<SavedFilter[]>([])
  const [saveOpen, setSaveOpen] = useState(false)
  const [name, setName] = useState("")
  const [error, setError] = useState("")
  const [deleteError, setDeleteError] = useState("")

  const refetch = useCallback(async () => {
    setSavedFilters(await api.getSavedFilters())
  }, [])

  useEffect(() => {
    refetch()
  }, [refetch])

  async function handleSave() {
    const trimmed = name.trim()
    if (!trimmed) {
      setError(t("savedFilters.nameRequired"))
      return
    }
    setError("")
    try {
      await api.saveFilter(trimmed, currentFilters as unknown as Record<string, unknown>)
      setSaveOpen(false)
      setName("")
      await refetch()
    } catch (err) {
      setError(err instanceof Error ? err.message : t("savedFilters.saveFailed"))
    }
  }

  async function handleDelete(id: string) {
    try {
      setDeleteError("")
      await api.deleteSavedFilter(id)
      await refetch()
    } catch (err) {
      setDeleteError(err instanceof Error ? err.message : t("savedFilters.deleteFailed"))
    }
  }

  return (
    <>
      <Dialog
        open={saveOpen}
        onOpenChange={(next) => {
          setSaveOpen(next)
          if (!next) {
            setName("")
            setError("")
          }
        }}
      >
        <DialogTrigger
          render={
            <Button size="sm" variant="outline">
              <Plus className="size-3.5" /> {t("savedFilters.saveButton")}
            </Button>
          }
        />
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{t("savedFilters.dialogTitle")}</DialogTitle>
          </DialogHeader>
          <div className="flex flex-col gap-2">
            <Label htmlFor="saved-filter-name">{t("common.name")}</Label>
            <Input
              id="saved-filter-name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder={t("savedFilters.namePlaceholder")}
            />
            <FormError>{error}</FormError>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setSaveOpen(false)}>
              {t("common.cancel")}
            </Button>
            <Button onClick={handleSave}>{t("common.save")}</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Combobox
        items={savedFilters}
        value={null}
        onValueChange={(sf: SavedFilter | null) => {
          if (sf) onApply(coerceDatabaseFilters(sf.filters))
        }}
        itemToStringLabel={(sf: SavedFilter) => sf.name}
      >
        <ComboboxInput placeholder={t("savedFilters.placeholder")} className="w-52" />
        <ComboboxContent>
          <ComboboxEmpty>{t("savedFilters.empty")}</ComboboxEmpty>
          <ComboboxList>
            {(sf: SavedFilter) => (
              <ComboboxItem key={sf.id} value={sf}>
                <span className="flex-1 truncate">{sf.name}</span>
                {/* stopPropagation na pointerdown i click — bez tego kliknięcie "x" trafiałoby
                    też do combobox jako wybór/zastosowanie tej pozycji, zamiast tylko ją usunąć. */}
                <button
                  type="button"
                  onPointerDown={(e) => e.stopPropagation()}
                  onClick={(e) => {
                    e.stopPropagation()
                    handleDelete(sf.id)
                  }}
                  aria-label={t("savedFilters.deleteAria", { name: sf.name })}
                  className="absolute right-1.5 flex size-5 shrink-0 items-center justify-center rounded text-muted-foreground hover:text-destructive"
                >
                  <X className="size-3" />
                </button>
              </ComboboxItem>
            )}
          </ComboboxList>
        </ComboboxContent>
      </Combobox>
      {deleteError && <FormError>{deleteError}</FormError>}
    </>
  )
}

export { SavedFiltersBar }
