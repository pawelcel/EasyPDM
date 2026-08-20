import { useEffect, useState } from "react"

import { api } from "@/api/client"
import { FormError } from "@/components/ui/form-error"
import { Hint } from "@/components/ui/hint"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { SectionLabel } from "@/components/ui/section-label"
import type { TranslationKey } from "@/i18n/translations"
import { useLanguage } from "@/i18n/use-language"

// Te same 4 wartości "rodzaj" co properties.rodzaj Części (part-property-form.tsx,
// add-node-dialog.tsx) — kolejność stała, dopasowana do kolejności w formularzu dodawania.
const PART_KINDS: { rodzaj: string; labelKey: TranslationKey }[] = [
  { rodzaj: "Wykonywana", labelKey: "part.kindManufactured" },
  { rodzaj: "Zakupowa", labelKey: "part.kindPurchased" },
  { rodzaj: "Normalia", labelKey: "part.kindStandard" },
  { rodzaj: "Klienta", labelKey: "part.kindClient" },
]

// Złożenia nie mają "rodzaju" — dostają JEDEN wspólny prefiks pod sztywnym kluczem
// "Zlozenie" (nie prawdziwa wartość properties.rodzaj), zob. ItemEndpoints.cs.
const ASSEMBLY_KIND = { rodzaj: "Zlozenie", labelKey: "itemType.assembly" as TranslationKey }

function NamingSettingsView() {
  const { t } = useLanguage()
  const [prefixes, setPrefixes] = useState<Record<string, string>>({})
  const [saving, setSaving] = useState<string | null>(null)
  const [error, setError] = useState("")
  const [loadError, setLoadError] = useState(false)

  useEffect(() => {
    api
      .getItemNumberPrefixes()
      .then((rows) => {
        const map: Record<string, string> = {}
        for (const row of rows) map[row.rodzaj] = row.prefix ?? ""
        setPrefixes(map)
      })
      .catch(() => setLoadError(true))
  }, [])

  async function save(rodzaj: string, value: string) {
    const trimmed = value.trim()
    const previous = prefixes
    setPrefixes((p) => ({ ...p, [rodzaj]: trimmed }))
    setSaving(rodzaj)
    setError("")
    try {
      await api.setItemNumberPrefix(rodzaj, trimmed || null)
    } catch (err) {
      setPrefixes(previous)
      setError(err instanceof Error ? err.message : t("naming.saveFailed"))
    } finally {
      setSaving(null)
    }
  }

  if (loadError) {
    return (
      <div className="mx-auto max-w-2xl">
        <h2 className="mb-4 text-lg font-semibold tracking-tight">{t("settings.naming")}</h2>
        <Hint>{t("database.loadError")}</Hint>
      </div>
    )
  }

  return (
    <div className="mx-auto max-w-2xl">
      <h2 className="mb-4 text-lg font-semibold tracking-tight">{t("settings.naming")}</h2>

      <div className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
        <Hint>{t("naming.hint")}</Hint>

        <SectionLabel>{t("itemType.part")}</SectionLabel>
        <div className="flex flex-col gap-3">
          {PART_KINDS.map((kind) => (
            <PrefixRow
              key={kind.rodzaj}
              rodzaj={kind.rodzaj}
              label={t(kind.labelKey)}
              value={prefixes[kind.rodzaj] ?? ""}
              disabled={saving === kind.rodzaj}
              placeholder={t("naming.prefixPlaceholder")}
              onChange={(value) => setPrefixes((p) => ({ ...p, [kind.rodzaj]: value }))}
              onSave={(value) => save(kind.rodzaj, value)}
            />
          ))}
        </div>

        <SectionLabel>{t("itemType.assembly")}</SectionLabel>
        <div className="flex flex-col gap-3">
          <PrefixRow
            rodzaj={ASSEMBLY_KIND.rodzaj}
            label={t(ASSEMBLY_KIND.labelKey)}
            value={prefixes[ASSEMBLY_KIND.rodzaj] ?? ""}
            disabled={saving === ASSEMBLY_KIND.rodzaj}
            placeholder={t("naming.prefixPlaceholder")}
            onChange={(value) => setPrefixes((p) => ({ ...p, [ASSEMBLY_KIND.rodzaj]: value }))}
            onSave={(value) => save(ASSEMBLY_KIND.rodzaj, value)}
          />
        </div>

        <FormError>{error}</FormError>
      </div>
    </div>
  )
}

function PrefixRow({
  rodzaj,
  label,
  value,
  disabled,
  placeholder,
  onChange,
  onSave,
}: {
  rodzaj: string
  label: string
  value: string
  disabled: boolean
  placeholder: string
  onChange: (value: string) => void
  onSave: (value: string) => void
}) {
  return (
    <div className="flex items-center gap-3">
      <Label htmlFor={`naming-prefix-${rodzaj}`} className="w-32 shrink-0">
        {label}
      </Label>
      <Input
        id={`naming-prefix-${rodzaj}`}
        value={value}
        maxLength={4}
        placeholder={placeholder}
        disabled={disabled}
        className="w-24"
        onChange={(e) => onChange(e.target.value)}
        onBlur={(e) => onSave(e.target.value)}
      />
    </div>
  )
}

export { NamingSettingsView }
