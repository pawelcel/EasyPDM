import { useEffect, useRef, useState } from "react"

import { api } from "@/api/client"
import type { NotificationType } from "@/api/types"
import { FormError } from "@/components/ui/form-error"
import { Hint } from "@/components/ui/hint"
import { SectionLabel } from "@/components/ui/section-label"
import type { TranslationKey } from "@/i18n/translations"
import { useLanguage } from "@/i18n/use-language"

const ITEM_TYPES: { type: NotificationType; labelKey: TranslationKey }[] = [
  { type: "status_review", labelKey: "notifications.pref.statusReview" },
  { type: "status_released", labelKey: "notifications.pref.statusReleased" },
  { type: "status_regressed", labelKey: "notifications.pref.statusRegressed" },
  { type: "new_revision", labelKey: "notifications.pref.newRevision" },
]
const PROJECT_TYPES: { type: NotificationType; labelKey: TranslationKey }[] = [
  { type: "project_assigned", labelKey: "notifications.pref.projectAssigned" },
  { type: "project_unassigned", labelKey: "notifications.pref.projectUnassigned" },
  { type: "project_deleted", labelKey: "notifications.pref.projectDeleted" },
]
const ACCOUNT_TYPES: { type: NotificationType; labelKey: TranslationKey }[] = [
  { type: "password_changed", labelKey: "notifications.pref.passwordChanged" },
  { type: "sample_project", labelKey: "notifications.pref.sampleProject" },
]
const ADMIN_TYPES: { type: NotificationType; labelKey: TranslationKey }[] = [
  { type: "low_disk_space", labelKey: "notifications.pref.lowDiskSpace" },
]

function NotificationSettingsView({ isAdmin }: { isAdmin: boolean }) {
  const { t } = useLanguage()
  const [enabled, setEnabled] = useState<Partial<Record<NotificationType, boolean>>>({})
  const [error, setError] = useState("")
  const [loadError, setLoadError] = useState(false)
  // Jeśli użytkownik zdąży kliknąć checkbox, zanim to początkowe pobranie się zakończy,
  // odpowiedź z serwera odzwierciedla stan SPRZED tego kliknięcia — bez tej flagi
  // nadpisałaby świeżo zmieniony stan lokalny z powrotem na stary, a drugie kliknięcie
  // (próba "naprawienia" tego, co wygląda na nieudane pierwsze) po cichu przełączyłoby
  // wartość z powrotem na niechcianą.
  const userEditedRef = useRef(false)

  useEffect(() => {
    let cancelled = false
    api
      .getNotificationPreferences()
      .then((rows) => {
        if (cancelled || userEditedRef.current) return
        const map: Partial<Record<NotificationType, boolean>> = {}
        for (const row of rows) map[row.type] = row.enabled
        setEnabled(map)
      })
      .catch(() => {
        if (!cancelled) setLoadError(true)
      })
    return () => {
      cancelled = true
    }
  }, [])

  async function toggle(type: NotificationType, checked: boolean) {
    userEditedRef.current = true
    const previous = enabled
    setEnabled((prev) => ({ ...prev, [type]: checked }))
    setError("")
    try {
      await api.updateNotificationPreference(type, checked)
    } catch (err) {
      setEnabled(previous)
      setError(err instanceof Error ? err.message : t("naming.saveFailed"))
    }
  }

  function renderGroup(labelKey: TranslationKey, types: { type: NotificationType; labelKey: TranslationKey }[]) {
    return (
      <div className="mt-4 first:mt-0">
        <SectionLabel>{t(labelKey)}</SectionLabel>
        <div className="flex flex-col gap-2">
          {types.map(({ type, labelKey: rowLabelKey }) => (
            <label key={type} className="flex cursor-pointer items-center gap-2 text-sm">
              <input
                type="checkbox"
                checked={enabled[type] ?? true}
                onChange={(e) => toggle(type, e.target.checked)}
                className="size-3.5 shrink-0 accent-primary"
              />
              {t(rowLabelKey)}
            </label>
          ))}
        </div>
      </div>
    )
  }

  if (loadError) {
    return (
      <div className="mx-auto max-w-2xl">
        <h2 className="mb-4 text-lg font-semibold tracking-tight">{t("settings.notifications")}</h2>
        <Hint>{t("database.loadError")}</Hint>
      </div>
    )
  }

  return (
    <div className="mx-auto max-w-2xl">
      <h2 className="mb-4 text-lg font-semibold tracking-tight">{t("settings.notifications")}</h2>
      <div className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
        {renderGroup("notifications.pref.groupItems", ITEM_TYPES)}
        {renderGroup("notifications.pref.groupProjects", PROJECT_TYPES)}
        {renderGroup("notifications.pref.groupAccount", ACCOUNT_TYPES)}
        {isAdmin && renderGroup("notifications.pref.groupAdmin", ADMIN_TYPES)}
        <FormError>{error}</FormError>
      </div>
    </div>
  )
}

export { NotificationSettingsView }
