import { useEffect, useState } from "react"

import { api, ApiError } from "@/api/client"
import { revisionLabel } from "@/api/types"
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

// Domyślna szerokość proponowana przy WŁĄCZANIU dopełniania — cztery cyfry to typowy
// wybór i dokładnie ten z przykładu w podpowiedzi.
const DEFAULT_DIGITS = 4

// Pole przyjmuje tylko 1-10: zero oznaczałoby "wyłączone", a od tego jest osobny włącznik.
function clampDigits(raw: string): number {
  return Math.max(1, Math.min(10, Number(raw) || DEFAULT_DIGITS))
}

// Jak numer będzie wyglądał po zapisaniu ustawień: prefiks + numer dopełniony zerami do
// zadanej szerokości. Dokładnie to samo liczy serwer (ItemNumbering.Label) — tutaj jest to
// tylko podgląd na żywo, dla wartości jeszcze niezapisanych.
//
// Podgląd dotyczy elementów tworzonych PO zapisaniu ustawień: i prefiks, i dopełnienie są
// zamrażane na elemencie przy jego tworzeniu, bo numer elementu to jednocześnie nazwa jego
// pliku na dysku, a tej nie da się przeliczyć wstecz.
function formatNumberExample(
  prefix: string,
  digits: number,
  sample: number,
  sampleName: string
): string {
  const text = String(sample)
  const number = `${prefix}${digits > 0 ? text.padStart(digits, "0") : text}`
  // Pełna postać, a nie sam numer: dokładnie to widać w drzewku i taką nazwę nadaje plikowi
  // makro CAD (tam dochodzi jeszcze rozszerzenie zależne od programu). Rewizja zawsze "A",
  // bo nowy element zaczyna od pierwszej — revisionLabel(1), żeby nie zaszywać litery.
  return `${number}(${sampleName}).${revisionLabel(1)}`
}

function NamingSettingsView() {
  const { t } = useLanguage()
  const [prefixes, setPrefixes] = useState<Record<string, string>>({})
  const [saving, setSaving] = useState<string | null>(null)
  const [error, setError] = useState("")
  const [loadError, setLoadError] = useState(false)
  // Dopełnienie zerami dotyczy WSZYSTKICH rodzajów naraz, więc siedzi tu, nad listą
  // prefiksów — i wchodzi do każdego podglądu niżej.
  //
  // Serwer zna JEDNĄ wartość (0 = bez dopełniania), ale w interfejsie rozdzielamy ją na
  // włącznik i liczbę cyfr: po wyłączeniu wpisana liczba zostaje w pamięci, więc ponowne
  // włączenie nie każe jej podawać od nowa. Wyłączone = dokładnie dotychczasowe zachowanie.
  const [paddingEnabled, setPaddingEnabled] = useState(false)
  const [digitsInput, setDigitsInput] = useState(DEFAULT_DIGITS)
  const [savingDigits, setSavingDigits] = useState(false)
  const digits = paddingEnabled ? digitsInput : 0

  useEffect(() => {
    Promise.all([api.getItemNumberPrefixes(), api.getItemNumberFormat()])
      .then(([rows, format]) => {
        const map: Record<string, string> = {}
        for (const row of rows) map[row.rodzaj] = row.prefix ?? ""
        setPrefixes(map)
        setPaddingEnabled(format.digits > 0)
        if (format.digits > 0) setDigitsInput(format.digits)
      })
      .catch(() => setLoadError(true))
  }, [])

  // enabled/value podajemy jawnie, zamiast czytać ze stanu: oba zapisy (przestawienie
  // włącznika i zmiana liczby cyfr) wołają to zaraz po setState, kiedy stan jeszcze się nie
  // zaktualizował.
  async function saveDigits(enabled: boolean, value: number) {
    const previousEnabled = paddingEnabled
    const previousValue = digitsInput
    setPaddingEnabled(enabled)
    setDigitsInput(value)
    setSavingDigits(true)
    setError("")
    try {
      await api.setItemNumberFormat(enabled ? value : 0)
    } catch (err) {
      setPaddingEnabled(previousEnabled)
      setDigitsInput(previousValue)
      setError(err instanceof Error ? err.message : t("naming.saveFailed"))
    } finally {
      setSavingDigits(false)
    }
  }

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

        <SectionLabel>{t("naming.digitsTitle")}</SectionLabel>
        <Hint>{t("naming.digitsHint")}</Hint>
        <label className="mt-2 flex cursor-pointer items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={paddingEnabled}
            disabled={savingDigits}
            onChange={(e) => void saveDigits(e.target.checked, digitsInput)}
            className="size-3.5 shrink-0 accent-primary"
          />
          {t("naming.digitsEnableLabel")}
        </label>
        {/* Pole zostaje widoczne także po wyłączeniu, tylko wyszarzone — żeby było wiadomo,
            co się włącza, zamiast kazać szukać zniknniętej opcji. */}
        <div className="mt-2 flex items-center gap-3">
          <Label htmlFor="naming-digits" className="w-32 shrink-0">
            {t("naming.digitsLabel")}
          </Label>
          <Input
            id="naming-digits"
            type="number"
            min={1}
            max={10}
            step={1}
            value={digitsInput}
            disabled={savingDigits || !paddingEnabled}
            className="w-24 [appearance:textfield] [&::-webkit-inner-spin-button]:appearance-none [&::-webkit-outer-spin-button]:appearance-none"
            onChange={(e) => setDigitsInput(clampDigits(e.target.value))}
            onBlur={(e) => void saveDigits(paddingEnabled, clampDigits(e.target.value))}
          />
          <span className="flex-1 truncate text-right font-mono text-[13px] text-muted-foreground">
            {t("naming.examplePrefix")} {formatNumberExample("", digits, 1, t("naming.exampleName"))}
          </span>
        </div>

        <SectionLabel>{t("itemType.part")}</SectionLabel>
        <div className="flex flex-col gap-3">
          {PART_KINDS.map((kind) => (
            <PrefixRow
              key={kind.rodzaj}
              rodzaj={kind.rodzaj}
              label={t(kind.labelKey)}
              value={prefixes[kind.rodzaj] ?? ""}
              digits={digits}
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
            digits={digits}
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
  digits,
  disabled,
  placeholder,
  onChange,
  onSave,
}: {
  rodzaj: string
  label: string
  value: string
  digits: number
  disabled: boolean
  placeholder: string
  onChange: (value: string) => void
  onSave: (value: string) => void
}) {
  const { t } = useLanguage()
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
      {/* Podgląd po prawej: od razu widać, jak będzie wyglądał numer po wpisaniu prefiksu,
          zamiast zgadywać, czy "C" skleja się z numerem, czy dostaje separator. Aktualizuje
          się przy pisaniu, zanim cokolwiek zostanie zapisane. */}
      <span className="flex-1 truncate text-right font-mono text-[13px] text-muted-foreground">
        {t("naming.examplePrefix")} {formatNumberExample(value.trim(), digits, 1, t("naming.exampleName"))}
      </span>
    </div>
  )
}

export { NamingSettingsView }
