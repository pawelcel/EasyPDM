import { useEffect, useState } from "react"
import { Download, Search } from "lucide-react"

import { api } from "@/api/client"
import type { ClientFileSearchResult } from "@/api/types"
import { Hint } from "@/components/ui/hint"
import { Input } from "@/components/ui/input"
import { useDebouncedValue } from "@/hooks/use-debounced-value"
import { useLanguage } from "@/i18n/use-language"

function ClientFileSearch({ clientId }: { clientId: number }) {
  const { t } = useLanguage()
  const [query, setQuery] = useState("")
  const debouncedQuery = useDebouncedValue(query, 300)
  const [results, setResults] = useState<ClientFileSearchResult[]>([])

  useEffect(() => {
    if (!debouncedQuery.trim()) {
      setResults([])
      return
    }
    api.searchClientFiles(clientId, debouncedQuery).then(setResults)
  }, [clientId, debouncedQuery])

  return (
    <div>
      <div className="relative">
        <Search className="pointer-events-none absolute top-1/2 left-2 size-3.5 -translate-y-1/2 text-muted-foreground" />
        <Input
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder={t("client.searchFilesPlaceholder")}
          className="pl-7"
        />
      </div>

      {debouncedQuery.trim() && (
        <div className="mt-2">
          {results.length > 0 ? (
            <ul className="flex flex-col gap-0.5">
              {results.map((r) => (
                <li key={r.id}>
                  <a
                    href={api.clientNodeDownloadUrl(clientId, r.id)}
                    target="_blank"
                    rel="noreferrer"
                    className="flex items-center gap-2 rounded-md px-2 py-1.5 text-sm hover:bg-accent"
                  >
                    <Download className="size-3.5 shrink-0 text-muted-foreground" />
                    <span className="truncate">{r.path}</span>
                  </a>
                </li>
              ))}
            </ul>
          ) : (
            <Hint>{t("client.searchFilesNoResults")}</Hint>
          )}
        </div>
      )}
    </div>
  )
}

export { ClientFileSearch }
