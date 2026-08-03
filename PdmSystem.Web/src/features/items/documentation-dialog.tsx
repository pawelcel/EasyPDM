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
import { useLanguage } from "@/i18n/use-language"

// Okno "Pobierz dokumentację" — działa zarówno dla pojedynczego elementu (Część/Złożenie,
// razem z całym jego poddrzewem), jak i dla całego projektu; różni je tylko to, skąd biorą
// listę rozszerzeń i URL do pobrania (przekazywane przez wywołującego). Domyślnie zaznaczone
// są wszystkie znalezione rozszerzenia — użytkownik odznacza to, czego nie chce.
function DocumentationDialog({
  trigger,
  fetchExtensions,
  buildDownloadUrl,
}: {
  trigger: React.ReactElement
  fetchExtensions: () => Promise<string[]>
  buildDownloadUrl: (extensions: string[]) => string
}) {
  const { t } = useLanguage()
  const [open, setOpen] = useState(false)
  const [loading, setLoading] = useState(false)
  const [extensions, setExtensions] = useState<string[]>([])
  const [selected, setSelected] = useState<Set<string>>(new Set())

  useEffect(() => {
    if (!open) return
    setLoading(true)
    fetchExtensions()
      .then((exts) => {
        setExtensions(exts)
        setSelected(new Set(exts))
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

  const selectedList = [...selected]

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger render={trigger} />
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("documentation.title")}</DialogTitle>
        </DialogHeader>

        {loading ? (
          <Hint>{t("common.loading")}</Hint>
        ) : extensions.length === 0 ? (
          <Hint>{t("documentation.noFiles")}</Hint>
        ) : (
          <>
            <Hint>{t("documentation.description")}</Hint>
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
          </>
        )}

        <DialogFooter>
          <Button variant="outline" onClick={() => setOpen(false)}>
            {t("common.cancel")}
          </Button>
          {selectedList.length > 0 ? (
            <Button
              render={
                // Zamknięcie okna odłożone na kolejny tick — jeśli odepniemy <a> z DOM-u
                // zanim przeglądarka zdąży zainicjować pobieranie (atrybut "download"), samo
                // pobranie potrafi się nie odpalić.
                <a
                  href={buildDownloadUrl(selectedList)}
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

export { DocumentationDialog }
