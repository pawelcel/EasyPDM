import { useEffect, useState } from "react"

import { useLanguage } from "@/i18n/use-language"

// Pobieramy bajty sami (zamiast dać przeglądarce iframe'ować URL bezpośrednio), żeby
// nadać blobowi jawny typ "application/pdf" — endpoint pobierania serwuje wszystko jako
// application/octet-stream (wymusza pobranie), więc surowy <iframe src={url}> by nie
// wyrenderował podglądu.
function PdfPreview({ url }: { url: string }) {
  const { t } = useLanguage()
  const [objectUrl, setObjectUrl] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    let localUrl: string | null = null

    fetch(url)
      .then((res) => {
        if (!res.ok) throw new Error(String(res.status))
        return res.blob()
      })
      .then((blob) => {
        if (cancelled) return
        localUrl = URL.createObjectURL(new Blob([blob], { type: "application/pdf" }))
        setObjectUrl(localUrl)
      })
      .catch(() => {
        if (!cancelled) setError(t("preview.loadFailed"))
      })

    return () => {
      cancelled = true
      if (localUrl) URL.revokeObjectURL(localUrl)
    }
  }, [url, t])

  if (error) return <div className="flex h-full items-center justify-center text-sm text-destructive">{error}</div>
  if (!objectUrl) return <div className="flex h-full items-center justify-center text-sm text-muted-foreground">{t("preview.loading")}</div>

  // "#view=FitH" każe wbudowanej przeglądarce PDF dopasować stronę do szerokości ramki —
  // bez tego domyślny zoom zostawiał dużo pustego marginesu wokół dokumentu.
  return <iframe title="PDF" src={`${objectUrl}#view=FitH`} className="h-full w-full rounded-md" />
}

export { PdfPreview }
