import { useEffect, useState } from "react"

import { api, ApiError } from "@/api/client"
import { Button } from "@/components/ui/button"
import { ConfirmDialog } from "@/components/ui/confirm-dialog"
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

// Złożenia mają własne rodzaje (Wykonywane/Zakupowe/Klienta), ale osobny prefiks dostaje
// tylko WYKONYWANE — pod sztywnym kluczem "Zlozenie" (nie jest to wartość properties.rodzaj).
// Zakupowe i klienta numerują się prefiksem odpowiedniego rodzaju Części wyżej, zob.
// ItemEndpoints.AssemblyPrefixKind.
const ASSEMBLY_KIND = {
  rodzaj: "Zlozenie",
  labelKey: "naming.assemblyManufacturedLabel" as TranslationKey,
}

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
        <Hint>{t("naming.assemblyPrefixHint")}</Hint>
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

      <ResetSequenceSection />
    </div>
  )
}

// Cofa numerację elementów (item_number_seq) tak, żeby KOLEJNY nowo utworzony element
// dostał wskazany numer -- tylko gdy żaden już istniejący element nie ma numeru równego
// lub wyższego (backend to sprawdza i odmawia, jeśli nie). Elementy z numerem NIŻSZYM
// zostają nietknięte, więc to pozwala odzyskać sam "ogon" numeracji po usuniętych
// elementach testowych (np. istnieją #1-#3, usunięto #4-#10 -> cofnięcie do 4 sprawia, że
// kolejny element znów dostanie #4) -- pełny reset do 1 to tylko szczególny przypadek tej
// samej reguły, wymagający pustej bazy.
function ResetSequenceSection() {
  const { t } = useLanguage()
  const [nextNumber, setNextNumber] = useState<number | null>(null)
  const [maxAssigned, setMaxAssigned] = useState<number | null>(null)
  const [target, setTarget] = useState("")
  const [loadError, setLoadError] = useState(false)
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [resetting, setResetting] = useState(false)
  const [error, setError] = useState("")
  const [success, setSuccess] = useState(false)

  function refresh() {
    api
      .getItemNumberSequence()
      .then((data) => {
        setNextNumber(data.nextNumber)
        setMaxAssigned(data.maxAssignedNumber)
        setTarget(String(data.nextNumber))
        setLoadError(false)
      })
      .catch(() => setLoadError(true))
  }

  useEffect(refresh, [])

  const targetValue = Number(target)
  const targetValid = target.trim() !== "" && Number.isInteger(targetValue) && targetValue >= 1

  async function performReset() {
    setResetting(true)
    setError("")
    setSuccess(false)
    try {
      await api.resetItemNumberSequence(targetValue)
      setConfirmOpen(false)
      setSuccess(true)
      refresh()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t("naming.resetSequenceFailed"))
    } finally {
      setResetting(false)
    }
  }

  return (
    <div className="mt-4 rounded-xl bg-card p-4 ring-1 ring-foreground/10">
      <SectionLabel>{t("naming.resetSequenceTitle")}</SectionLabel>
      <Hint>{t("naming.resetSequenceHint")}</Hint>

      {loadError ? (
        <Hint>{t("database.loadError")}</Hint>
      ) : (
        <>
          <div className="mt-2 text-[13px] text-muted-foreground">
            {t("naming.resetSequenceCurrentNext", { number: nextNumber ?? "…" })}
            {" · "}
            {maxAssigned === null
              ? t("naming.resetSequenceNoneAssigned")
              : t("naming.resetSequenceMaxAssigned", { number: maxAssigned })}
          </div>

          <div className="mt-2 flex items-end gap-3">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="reset-sequence-target">{t("naming.resetSequenceTargetLabel")}</Label>
              <Input
                id="reset-sequence-target"
                type="number"
                min={1}
                step={1}
                value={target}
                disabled={resetting}
                className="w-28 [appearance:textfield] [&::-webkit-inner-spin-button]:appearance-none [&::-webkit-outer-spin-button]:appearance-none"
                onChange={(e) => setTarget(e.target.value)}
              />
            </div>
            <Button
              variant="destructive"
              onClick={() => {
                setError("")
                setConfirmOpen(true)
              }}
              disabled={resetting || !targetValid}
            >
              {t("naming.resetSequenceButton")}
            </Button>
          </div>
        </>
      )}

      {success && (
        <div className="mt-2">
          <Hint>{t("naming.resetSequenceSuccess", { number: targetValue })}</Hint>
        </div>
      )}
      <FormError>{error}</FormError>

      <ConfirmDialog
        open={confirmOpen}
        title={t("naming.resetSequenceConfirmTitle")}
        description={t("naming.resetSequenceConfirmDescription", { number: targetValue })}
        confirmLabel={t("naming.resetSequenceButton")}
        variant="destructive"
        onConfirm={performReset}
        onCancel={() => setConfirmOpen(false)}
        pending={resetting}
        error={error}
      />
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
