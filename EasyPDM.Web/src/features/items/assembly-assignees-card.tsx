import { useEffect, useState } from "react"
import { Star, X } from "lucide-react"

import { api } from "@/api/client"
import type { AssemblyAssignees } from "@/api/types"
import { Button } from "@/components/ui/button"
import { FormError } from "@/components/ui/form-error"
import { Hint } from "@/components/ui/hint"
import { PANEL_CARD } from "@/components/ui/panel-card"
import { SectionLabel } from "@/components/ui/section-label"
import { UserCombobox } from "@/features/projects/user-combobox"
import { useLanguage } from "@/i18n/use-language"
import { cn } from "@/lib/utils"

// Who works on an assembly: people of its project's team (or the project's lead), any number of
// them, one of whom may lead the work (the star). A person may work on several assemblies. Only
// the project's lead edits it (canEdit from the server), and each person assigned is notified;
// everyone else sees it.
function AssemblyAssigneesCard({ itemId }: { itemId: string }) {
  const { t } = useLanguage()
  const [data, setData] = useState<AssemblyAssignees | null>(null)
  const [newUserId, setNewUserId] = useState<string | null>(null)
  const [error, setError] = useState("")

  async function reload() {
    try {
      setData(await api.getAssemblyAssignees(itemId))
    } catch {
      setData(null)
    }
  }

  useEffect(() => {
    void reload()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [itemId])

  async function run(action: () => Promise<unknown>) {
    setError("")
    try {
      await action()
      await reload()
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    }
  }

  if (!data) return null
  const assigned = new Set(data.assignees.map((a) => a.userId))
  const candidates = data.candidates.filter((c) => !assigned.has(c.userId))

  return (
    <div className={PANEL_CARD}>
      <SectionLabel>{t("assignees.title")}</SectionLabel>
      <div className="flex flex-col gap-2">
        {data.assignees.length === 0 ? (
          <Hint>{t("assignees.none")}</Hint>
        ) : (
          <ul className="flex flex-col gap-1">
            {data.assignees.map((a) => (
              <li key={a.userId} className="flex items-center gap-2 text-sm">
                {data.canEdit ? (
                  <Button
                    type="button"
                    size="icon-xs"
                    variant="ghost"
                    aria-label={a.isLead ? t("assignees.unsetLeadAria") : t("assignees.setLeadAria")}
                    title={a.isLead ? t("assignees.unsetLeadAria") : t("assignees.setLeadAria")}
                    onClick={() => run(() => api.setAssemblyAssigneeLead(itemId, a.userId, !a.isLead))}
                  >
                    <Star className={cn(a.isLead && "fill-amber-400 text-amber-400")} />
                  </Button>
                ) : (
                  <Star className={cn("size-3.5", a.isLead ? "fill-amber-400 text-amber-400" : "opacity-0")} />
                )}
                <span className="min-w-0 flex-1 truncate">{a.name}</span>
                {a.isLead && (
                  <span className="rounded-full bg-amber-400/15 px-2 py-0.5 text-[11.5px] font-medium text-amber-500">
                    {t("assignees.lead")}
                  </span>
                )}
                {data.canEdit && (
                  <Button
                    type="button"
                    size="icon-xs"
                    variant="ghost"
                    aria-label={t("assignees.removeAria")}
                    title={t("assignees.removeAria")}
                    onClick={() => run(() => api.removeAssemblyAssignee(itemId, a.userId))}
                  >
                    <X />
                  </Button>
                )}
              </li>
            ))}
          </ul>
        )}
        {data.canEdit &&
          (data.candidates.length === 0 ? (
            <Hint>{t("assignees.noTeamHint")}</Hint>
          ) : (
            candidates.length > 0 && (
              <div className="flex items-center gap-2">
                <div className="min-w-0 flex-1">
                  <UserCombobox
                    users={candidates}
                    value={newUserId}
                    onChange={setNewUserId}
                    placeholder={t("team.pickPerson")}
                  />
                </div>
                <Button
                  type="button"
                  size="sm"
                  variant="outline"
                  disabled={newUserId === null}
                  onClick={() =>
                    newUserId &&
                    run(async () => {
                      await api.addAssemblyAssignee(itemId, newUserId)
                      setNewUserId(null)
                    })
                  }
                >
                  {t("project.addContact")}
                </Button>
              </div>
            )
          ))}
        <FormError>{error}</FormError>
      </div>
    </div>
  )
}

export { AssemblyAssigneesCard }
