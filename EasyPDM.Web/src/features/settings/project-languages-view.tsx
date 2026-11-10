import { useCallback, useEffect, useState } from "react"
import { Check, Pencil, Trash2, X } from "lucide-react"

import { api, ApiError } from "@/api/client"
import type { ProjectLanguage } from "@/api/types"
import { Button } from "@/components/ui/button"
import { ConfirmDialog } from "@/components/ui/confirm-dialog"
import { FormError } from "@/components/ui/form-error"
import { Hint } from "@/components/ui/hint"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { SectionLabel } from "@/components/ui/section-label"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { useLanguage } from "@/i18n/use-language"

const CODE_PATTERN = /^[A-Z]{1,3}$/

// Skrót wpisuje się od razu wielkimi literami: małe litery są podnoszone, a wszystko spoza
// A-Z (cyfry, spacje, polskie znaki) odrzucane jeszcze w polu, zamiast dopiero przy zapisie.
function toCode(raw: string) {
  return raw
    .toUpperCase()
    .replace(/[^A-Z]/g, "")
    .slice(0, 3)
}

// Ustawienia -> Język -> Języki projektu. Lista języków dokumentacji firmy (nie interfejsu):
// pełna nazwa i skrót z 1-3 wielkich liter. Zmienia ją administrator, pozostali tylko widzą.
function ProjectLanguagesView({ isAdmin }: { isAdmin: boolean }) {
  const { t } = useLanguage()
  const [languages, setLanguages] = useState<ProjectLanguage[] | null>(null)
  const [loadError, setLoadError] = useState(false)
  const [confirming, setConfirming] = useState<ProjectLanguage | null>(null)
  const [deletePending, setDeletePending] = useState(false)
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [editingId, setEditingId] = useState<number | null>(null)

  const refetch = useCallback(async () => {
    try {
      setLanguages(await api.getProjectLanguages())
      setLoadError(false)
    } catch {
      setLoadError(true)
    }
  }, [])

  useEffect(() => {
    refetch()
  }, [refetch])

  // Sprawdzenie przed wysłaniem, z czytelnym komunikatem (który język ma już ten skrót);
  // serwer i tak pilnuje unikalności, a jego 409 obsługuje submit niżej.
  function validate(name: string, code: string, exceptId: number | null): string | null {
    if (!name) return t("projectLanguage.nameRequired")
    if (!CODE_PATTERN.test(code)) return t("projectLanguage.codeInvalid")
    const others = (languages ?? []).filter((l) => l.id !== exceptId)
    if (others.some((l) => l.name.toLowerCase() === name.toLowerCase())) return t("projectLanguage.nameTaken")
    const sameCode = others.find((l) => l.code === code)
    if (sameCode) return t("projectLanguage.codeTaken", { name: sameCode.name })
    return null
  }

  async function save(id: number | null, name: string, code: string): Promise<string | null> {
    const invalid = validate(name, code, id)
    if (invalid) return invalid
    try {
      if (id === null) await api.addProjectLanguage({ name, code })
      else await api.updateProjectLanguage(id, { name, code })
      await refetch()
      return null
    } catch (err) {
      return err instanceof ApiError && err.status === 409
        ? t("projectLanguage.conflict")
        : t("projectLanguage.saveFailed")
    }
  }

  const [defaultError, setDefaultError] = useState<string | null>(null)
  const defaultLanguage = languages?.find((l) => l.isDefault) ?? null

  async function changeDefault(id: number) {
    setDefaultError(null)
    try {
      await api.setDefaultProjectLanguage(id)
      await refetch()
    } catch {
      setDefaultError(t("projectLanguage.saveFailed"))
    }
  }

  async function confirmDelete() {
    if (!confirming) return
    setDeletePending(true)
    setDeleteError(null)
    try {
      await api.removeProjectLanguage(confirming.id)
      setConfirming(null)
      await refetch()
    } catch (err) {
      setDeleteError(err instanceof Error ? err.message : t("projectLanguage.deleteFailed"))
    } finally {
      setDeletePending(false)
    }
  }

  return (
    <div className="mx-auto max-w-2xl">
      <h2 className="mb-4 text-lg font-semibold tracking-tight">{t("settings.projectLanguages")}</h2>

      {languages && languages.length > 0 && (
        <div className="mb-4 rounded-xl bg-card p-4 ring-1 ring-foreground/10">
          <SectionLabel>{t("projectLanguage.defaultLabel")}</SectionLabel>
          <Hint>{t("projectLanguage.defaultHint")}</Hint>
          <div className="mt-2">
            {isAdmin ? (
              <Select
                value={defaultLanguage ? String(defaultLanguage.id) : ""}
                onValueChange={(v) => v && changeDefault(Number(v))}
              >
                <SelectTrigger className="w-64" aria-label={t("projectLanguage.defaultLabel")}>
                  <SelectValue>
                    {(v: string) => {
                      const lang = languages.find((l) => String(l.id) === v)
                      return lang ? `${lang.name} (${lang.code})` : ""
                    }}
                  </SelectValue>
                </SelectTrigger>
                <SelectContent>
                  {languages.map((lang) => (
                    <SelectItem key={lang.id} value={String(lang.id)}>
                      {lang.name} ({lang.code})
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            ) : (
              <span className="text-sm">
                {defaultLanguage ? `${defaultLanguage.name} (${defaultLanguage.code})` : "—"}
              </span>
            )}
          </div>
          {defaultError && (
            <div className="mt-2">
              <FormError>{defaultError}</FormError>
            </div>
          )}
        </div>
      )}

      <div className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
        <SectionLabel>{t("projectLanguage.listLabel")}</SectionLabel>
        <Hint>
          {t("projectLanguage.hint")}
          {!isAdmin && ` ${t("projectLanguage.hintReadOnly")}`}
        </Hint>

        {loadError ? (
          <div className="mt-3">
            <Hint>{t("database.loadError")}</Hint>
          </div>
        ) : (
          <>
            {isAdmin && <AddLanguageRow onAdd={(name, code) => save(null, name, code)} />}

            {languages === null ? null : languages.length > 0 ? (
              <ul className="mt-3 flex flex-col gap-0.5">
                {languages.map((lang) =>
                  editingId === lang.id ? (
                    <EditLanguageRow
                      key={lang.id}
                      language={lang}
                      onSave={async (name, code) => {
                        const error = await save(lang.id, name, code)
                        if (!error) setEditingId(null)
                        return error
                      }}
                      onCancel={() => setEditingId(null)}
                    />
                  ) : (
                    <li
                      key={lang.id}
                      className="flex items-center gap-2 rounded-md px-2 py-1.5 text-sm hover:bg-accent"
                    >
                      <span className="w-12 shrink-0 font-mono text-[13px] font-semibold">{lang.code}</span>
                      <span className="truncate">{lang.name}</span>
                      {lang.isDefault && (
                        <span className="shrink-0 rounded bg-primary/15 px-1.5 py-0.5 text-[11.5px] text-primary">
                          {t("projectLanguage.defaultBadge")}
                        </span>
                      )}
                      {isAdmin && (
                        <div className="ml-auto flex shrink-0 items-center gap-0.5">
                          <Button
                            size="icon-xs"
                            variant="ghost"
                            aria-label={t("common.editNamed", { name: lang.name })}
                            onClick={() => setEditingId(lang.id)}
                          >
                            <Pencil className="size-3.5 text-muted-foreground" />
                          </Button>
                          <Button
                            size="icon-xs"
                            variant="ghost"
                            aria-label={t("common.deleteNamed", { name: lang.name })}
                            title={lang.isDefault ? t("projectLanguage.cannotDeleteDefault") : undefined}
                            disabled={lang.isDefault}
                            onClick={() => {
                              setDeleteError(null)
                              setConfirming(lang)
                            }}
                          >
                            <Trash2 className="size-3.5 text-muted-foreground" />
                          </Button>
                        </div>
                      )}
                    </li>
                  )
                )}
              </ul>
            ) : (
              <div className="mt-3">
                <Hint>{t("projectLanguage.empty")}</Hint>
              </div>
            )}
          </>
        )}
      </div>

      {confirming && (
        <ConfirmDialog
          open
          title={t("common.deleteNamed", { name: confirming.name })}
          description={t("projectLanguage.deleteConfirm", { name: confirming.name, code: confirming.code })}
          confirmLabel={t("common.delete")}
          variant="destructive"
          onConfirm={confirmDelete}
          onCancel={() => setConfirming(null)}
          pending={deletePending}
          error={deleteError}
        />
      )}
    </div>
  )
}

function AddLanguageRow({ onAdd }: { onAdd: (name: string, code: string) => Promise<string | null> }) {
  const { t } = useLanguage()
  const [name, setName] = useState("")
  const [code, setCode] = useState("")
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  async function submit() {
    setPending(true)
    const result = await onAdd(name.trim(), code)
    setPending(false)
    setError(result)
    if (!result) {
      setName("")
      setCode("")
    }
  }

  return (
    <form
      className="mt-3 flex flex-col gap-1.5"
      onSubmit={(e) => {
        e.preventDefault()
        submit()
      }}
    >
      <div className="flex items-end gap-2">
        <div className="flex min-w-0 flex-1 flex-col gap-1">
          <Label htmlFor="project-language-name">{t("projectLanguage.nameLabel")}</Label>
          <Input
            id="project-language-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder={t("projectLanguage.namePlaceholder")}
          />
        </div>
        <div className="flex w-24 flex-col gap-1">
          <Label htmlFor="project-language-code">{t("projectLanguage.codeLabel")}</Label>
          <Input
            id="project-language-code"
            value={code}
            onChange={(e) => setCode(toCode(e.target.value))}
            placeholder={t("projectLanguage.codePlaceholder")}
            maxLength={3}
            className="font-mono uppercase"
          />
        </div>
        <Button type="submit" disabled={pending}>
          {t("common.add")}
        </Button>
      </div>
      <Hint>{t("projectLanguage.codeHint")}</Hint>
      {error && <FormError>{error}</FormError>}
    </form>
  )
}

function EditLanguageRow({
  language,
  onSave,
  onCancel,
}: {
  language: ProjectLanguage
  onSave: (name: string, code: string) => Promise<string | null>
  onCancel: () => void
}) {
  const { t } = useLanguage()
  const [name, setName] = useState(language.name)
  const [code, setCode] = useState(language.code)
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  async function submit() {
    setPending(true)
    const result = await onSave(name.trim(), code)
    setPending(false)
    setError(result)
  }

  return (
    <li className="rounded-md bg-accent/50 px-2 py-1.5">
      <form
        className="flex items-center gap-2"
        onSubmit={(e) => {
          e.preventDefault()
          submit()
        }}
        onKeyDown={(e) => {
          if (e.key === "Escape") onCancel()
        }}
      >
        <Input
          value={code}
          onChange={(e) => setCode(toCode(e.target.value))}
          maxLength={3}
          aria-label={t("projectLanguage.codeLabel")}
          className="w-16 shrink-0 font-mono uppercase"
        />
        <Input
          value={name}
          onChange={(e) => setName(e.target.value)}
          aria-label={t("projectLanguage.nameLabel")}
          className="min-w-0 flex-1"
          autoFocus
        />
        <Button type="submit" size="icon-xs" variant="ghost" disabled={pending} aria-label={t("common.save")}>
          <Check className="size-3.5" />
        </Button>
        <Button type="button" size="icon-xs" variant="ghost" onClick={onCancel} aria-label={t("common.cancel")}>
          <X className="size-3.5 text-muted-foreground" />
        </Button>
      </form>
      {error && (
        <div className="mt-1">
          <FormError>{error}</FormError>
        </div>
      )}
    </li>
  )
}

export { ProjectLanguagesView }
