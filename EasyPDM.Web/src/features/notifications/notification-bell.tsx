import { Bell, X } from "lucide-react"
import { useState } from "react"

import type { NotificationEntry } from "@/api/types"
import { Button } from "@/components/ui/button"
import { Hint } from "@/components/ui/hint"
import { useNotifications } from "@/features/notifications/use-notifications"
import type { LanguageContextValue } from "@/i18n/language-context"
import { useLanguage } from "@/i18n/use-language"
import { cn } from "@/lib/utils"

// Dzwonek powiadomień w prawym górnym rogu (App.tsx, obok nazwy użytkownika). Panel to
// WŁASNA, lekka implementacja — w projekcie nie ma gotowego Popover/DropdownMenu — z
// niewidzialnym "fixed inset-0" tłem zamykającym panel po kliknięciu obok (ten sam trik
// co centralne modale w dialog.tsx, tylko bez przyciemnienia tła).
function NotificationBell({
  onNavigateToItem,
  onNavigateToProject,
  onNavigateToSettings,
}: {
  onNavigateToItem?: (id: string) => void
  onNavigateToProject?: (id: string) => void
  onNavigateToSettings?: () => void
}) {
  const { t } = useLanguage()
  const { unreadCount, items, refetch, markRead, markAllRead, deleteNotification } = useNotifications()
  const [open, setOpen] = useState(false)

  function toggle() {
    if (!open) refetch()
    setOpen((prev) => !prev)
  }

  function handleClick(entry: NotificationEntry) {
    if (!entry.readAt) markRead(entry.id)

    if (entry.itemId && onNavigateToItem) {
      onNavigateToItem(entry.itemId)
      setOpen(false)
    } else if (entry.projectId && onNavigateToProject) {
      onNavigateToProject(entry.projectId)
      setOpen(false)
    } else if (entry.type === "low_disk_space" && onNavigateToSettings) {
      onNavigateToSettings()
      setOpen(false)
    }
  }

  return (
    <div className="relative">
      <Button
        type="button"
        variant="ghost"
        size="icon-sm"
        onClick={toggle}
        aria-label={t("notifications.title")}
        title={t("notifications.title")}
      >
        <Bell />
        {unreadCount > 0 && (
          <span className="absolute top-0.5 right-0.5 flex h-3.5 min-w-3.5 items-center justify-center rounded-full bg-destructive px-0.5 text-[9px] leading-none font-medium text-destructive-foreground">
            {unreadCount > 99 ? "99+" : unreadCount}
          </span>
        )}
      </Button>

      {open && (
        <>
          <div className="fixed inset-0 z-40" onClick={() => setOpen(false)} />
          <div className="absolute top-full right-0 z-50 mt-2 w-96 rounded-xl bg-card p-2 shadow-lg ring-1 ring-foreground/10">
            <div className="flex items-center justify-between px-1.5 pt-1 pb-2">
              <span className="text-[12.5px] font-medium">{t("notifications.title")}</span>
              <Button type="button" variant="link" size="xs" onClick={markAllRead} disabled={unreadCount === 0}>
                {t("notifications.markAllRead")}
              </Button>
            </div>

            {items.length === 0 ? (
              <div className="px-1.5 pb-1.5">
                <Hint>{t("notifications.empty")}</Hint>
              </div>
            ) : (
              <ul className="flex max-h-96 flex-col gap-0.5 overflow-y-auto">
                {items.map((entry) => (
                  <li
                    key={entry.id}
                    className={cn("flex items-start gap-1 rounded-lg px-1.5 py-1.5", !entry.readAt && "bg-primary/5")}
                  >
                    <button
                      type="button"
                      onClick={() => handleClick(entry)}
                      className="flex flex-1 items-start gap-2 rounded-lg text-left text-[12.5px] hover:bg-muted"
                    >
                      {!entry.readAt && <span className="mt-1.5 size-1.5 shrink-0 rounded-full bg-primary" />}
                      <span className={cn("flex-1", entry.readAt && "pl-3.5")}>
                        <span className="block">{describe(entry, t)}</span>
                        <span className="text-muted-foreground">
                          {new Date(entry.createdAt).toLocaleString("pl-PL")}
                        </span>
                      </span>
                    </button>
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon-xs"
                      onClick={() => deleteNotification(entry.id)}
                      aria-label={t("notifications.deleteAria")}
                      title={t("notifications.deleteAria")}
                    >
                      <X />
                    </Button>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </>
      )}
    </div>
  )
}

function describe(entry: NotificationEntry, t: LanguageContextValue["t"]): string {
  const data = entry.data as Record<string, string | number>
  switch (entry.type) {
    case "status_review":
      return t("notifications.statusReview", { itemLabel: data.itemLabel })
    case "status_released":
      return t("notifications.statusReleased", { itemLabel: data.itemLabel })
    case "status_regressed":
      return t("notifications.statusRegressed", { itemLabel: data.itemLabel })
    case "new_revision":
      return t("notifications.newRevision", { itemLabel: data.itemLabel })
    case "project_assigned":
      return t("notifications.projectAssigned", { projectName: data.projectName })
    case "project_unassigned":
      return t("notifications.projectUnassigned", { projectName: data.projectName })
    case "project_deleted":
      return t("notifications.projectDeleted", { projectName: data.projectName })
    case "password_changed":
      return t("notifications.passwordChanged")
    case "low_disk_space":
      return t("notifications.lowDiskSpace", { freeGb: data.freeGb, totalGb: data.totalGb })
    case "sample_project":
      return t("notifications.sampleProject", { projectName: data.projectName })
  }
}

export { NotificationBell }
