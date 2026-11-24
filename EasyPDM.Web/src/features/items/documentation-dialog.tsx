import { useEffect, useState } from "react"

import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog"
import { Hint } from "@/components/ui/hint"
import { api } from "@/api/client"
import type { Item } from "@/api/types"
import { useLanguage } from "@/i18n/use-language"

// Okno "Pobierz dokumentację" — działa zarówno dla pojedynczego elementu (Część/Złożenie,
// razem z całym jego poddrzewem), jak i dla całego projektu; różni je tylko to, skąd biorą
// listę rozszerzeń i URL do pobrania (przekazywane przez wywołującego). Domyślnie zaznaczone
// są wszystkie znalezione rozszerzenia — użytkownik odznacza to, czego nie chce.
// Złożenie może dołożyć do archiwum swój BOM (withBom): te same CSV, co pod listą BOM-u,
// domyślnie odznaczone, żeby dotychczasowe pobranie dawało to samo co wcześniej.
type BomExport = "full" | "aggregated"
const BOM_EXPORTS: BomExport[] = ["full", "aggregated"]

function DocumentationDialog({
  trigger,
  fetchExtensions,
  buildDownloadUrl,
  withBom = false,
}: {
  trigger: React.ReactElement
  fetchExtensions: () => Promise<string[]>
  buildDownloadUrl: (extensions: string[], bom: BomExport[]) => string
  withBom?: boolean
}) {
  const { t } = useLanguage()
  const [open, setOpen] = useState(false)
  const [loading, setLoading] = useState(false)
  const [extensions, setExtensions] = useState<string[]>([])
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [selectedBom, setSelectedBom] = useState<Set<BomExport>>(new Set())

  useEffect(() => {
    if (!open) return
    setLoading(true)
    setSelectedBom(new Set())
    fetchExtensions()
      .then((exts) => {
        setExtensions(exts)
        setSelected(new Set(exts))
      })
      .catch(() => {
        setExtensions([])
        setSelected(new Set())
      })
      .finally(() => setLoading(false))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  function toggle(ext: string) {
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(ext)) next.delete(ext)
      else next.add(ext)
      return next
    })
  }

  function toggleBom(kind: BomExport) {
    setSelectedBom((prev) => {
      const next = new Set(prev)
      if (next.has(kind)) next.delete(kind)
      else next.add(kind)
      return next
    })
  }

  const selectedList = [...selected]
  const selectedBomList = BOM_EXPORTS.filter((kind) => selectedBom.has(kind))
  const anythingSelected = selectedList.length > 0 || selectedBomList.length > 0

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger render={trigger} />
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("documentation.title")}</DialogTitle>
        </DialogHeader>

        {loading ? (
          <Hint>{t("common.loading")}</Hint>
        ) : (
          <>
            {extensions.length === 0 ? (
              <Hint>{t("documentation.noFiles")}</Hint>
            ) : (
              <Hint>{t("documentation.description")}</Hint>
            )}
            <ul className="flex flex-col gap-0.5">
              {extensions.map((ext) => (
                <li key={ext}>
                  <label className="flex cursor-pointer items-center gap-2 rounded-md px-1.5 py-1 text-sm hover:bg-accent">
                    <input
                      type="checkbox"
                      checked={selected.has(ext)}
                      onChange={() => toggle(ext)}
                      className="size-3.5 shrink-0 accent-primary"
                    />
                    .{ext}
                  </label>
                </li>
              ))}
            </ul>
            {withBom && (
              <ul className="flex flex-col gap-0.5 border-t pt-2">
                {BOM_EXPORTS.map((kind) => (
                  <li key={kind}>
                    <label className="flex cursor-pointer items-center gap-2 rounded-md px-1.5 py-1 text-sm hover:bg-accent">
                      <input
                        type="checkbox"
                        checked={selectedBom.has(kind)}
                        onChange={() => toggleBom(kind)}
                        className="size-3.5 shrink-0 accent-primary"
                      />
                      {t(kind === "full" ? "documentation.bomFull" : "documentation.bomAggregated")}
                    </label>
                  </li>
                ))}
              </ul>
            )}
          </>
        )}

        <DialogFooter>
          <Button variant="outline" onClick={() => setOpen(false)}>
            {t("common.cancel")}
          </Button>
          {anythingSelected ? (
            <Button
              render={
                // Zamknięcie okna odłożone na kolejny tick — jeśli odepniemy <a> z DOM-u
                // zanim przeglądarka zdąży zainicjować pobieranie (atrybut "download"), samo
                // pobranie potrafi się nie odpalić.
                <a
                  href={buildDownloadUrl(selectedList, selectedBomList)}
                  download
                  onClick={() => setTimeout(() => setOpen(false), 0)}
                />
              }
            >
              {t("documentation.download")}
            </Button>
          ) : (
            <Button disabled>{t("documentation.download")}</Button>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

// "Pobierz dokumentację" for one item (part or assembly with its subtree). The button sits in
// several places (the item panel, the bar over the item list, the bar over the project tree);
// they all use this, so an option added here (such as the BOM of an assembly) shows in each.
function ItemDocumentationDialog({ item, trigger }: { item: Pick<Item, "id" | "itemType">; trigger: React.ReactElement }) {
  return (
    <DocumentationDialog
      trigger={trigger}
      fetchExtensions={() => api.getItemDocumentationExtensions(item.id)}
      buildDownloadUrl={(extensions, bom) => api.itemDocumentationUrl(item.id, extensions, bom)}
      withBom={item.itemType === "assembly"}
    />
  )
}

export { DocumentationDialog, ItemDocumentationDialog }
