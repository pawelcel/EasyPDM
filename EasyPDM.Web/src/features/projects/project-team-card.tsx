import { useEffect, useState } from "react"
import { X } from "lucide-react"

import { api } from "@/api/client"
import type { ProjectTeam } from "@/api/types"
import { Button } from "@/components/ui/button"
import { FormError } from "@/components/ui/form-error"
import { Hint } from "@/components/ui/hint"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { PANEL_CARD } from "@/components/ui/panel-card"
import { SectionLabel } from "@/components/ui/section-label"
import { UserCombobox } from "@/features/projects/user-combobox"
import { useLanguage } from "@/i18n/use-language"

// The company's side of a project, as "People on the client's side" is the client's: the
// project's lead among EasyPDM users and the people taking part, each with a role (one person
// may be added several times with different roles). Picked from the users who can see the
// project and the administrators; the server takes off whoever loses access. Only an
// administrator edits it; everyone with access sees it.
function ProjectTeamCard({ projectId, isAdmin }: { projectId: string; isAdmin: boolean }) {
  const { t } = useLanguage()
  const [team, setTeam] = useState<ProjectTeam | null>(null)
  const [roles, setRoles] = useState<Record<number, string>>({})
  // The row just added: its role field takes the focus, so the role is typed straight away.
  const [justAdded, setJustAdded] = useState<number | null>(null)
  const [error, setError] = useState("")

  async function reload() {
    try {
      const next = await api.getProjectTeam(projectId)
      setTeam(next)
      setRoles(Object.fromEntries(next.members.map((m) => [m.id, m.role])))
    } catch {
      setTeam(null)
    }
  }

  useEffect(() => {
    void reload()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [projectId])

  async function run(action: () => Promise<unknown>) {
    setError("")
    try {
      await action()
      await reload()
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    }
  }

  if (!team) return null

  return (
    <div className={PANEL_CARD}>
      <SectionLabel>{t("team.title")}</SectionLabel>
      <div className="flex flex-col gap-2">
        <Label htmlFor="project-lead-user">{t("team.lead")}</Label>
        {isAdmin ? (
          <UserCombobox
            id="project-lead-user"
            users={team.candidates}
            value={team.leadUserId}
            onChange={(userId) => run(() => api.setProjectLeadUser(projectId, userId))}
            placeholder={t("team.pickPerson")}
          />
        ) : (
          <div className="text-sm">{team.leadName ?? <span className="text-muted-foreground">-</span>}</div>
        )}

        <Label>{t("team.members")}</Label>
        {team.members.length === 0 ? (
          <Hint>{t("team.noMembers")}</Hint>
        ) : (
          <ul className="flex flex-col gap-1.5">
            {team.members.map((m) => (
              <li key={m.id} className="flex items-center gap-2">
                <div className="w-1/3 min-w-0 truncate text-sm">{m.name}</div>
                {isAdmin ? (
                  <Input
                    className="flex-1"
                    autoFocus={m.id === justAdded}
                    value={roles[m.id] ?? ""}
                    placeholder={t("team.rolePlaceholder")}
                    aria-label={t("project.contactRole")}
                    onChange={(e) => setRoles((r) => ({ ...r, [m.id]: e.target.value }))}
                    onBlur={() => {
                      const role = (roles[m.id] ?? "").trim()
                      if (role !== m.role) void run(() => api.updateProjectTeamRole(projectId, m.id, role))
                    }}
                  />
                ) : (
                  <span className="flex-1 truncate text-sm text-muted-foreground">{m.role || "-"}</span>
                )}
                {isAdmin && (
                  <Button
                    type="button"
                    size="icon-xs"
                    variant="ghost"
                    aria-label={t("team.removeMemberAria")}
                    title={t("team.removeMemberAria")}
                    onClick={() => run(() => api.removeProjectTeamMember(projectId, m.id))}
                  >
                    <X />
                  </Button>
                )}
              </li>
            ))}
          </ul>
        )}
        {/* Picking a person adds them at once, with no role yet; the role is typed in their
            new row. The list stays, for the next person. */}
        {isAdmin && (
          <UserCombobox
            key={team.members.length}
            users={team.candidates}
            value={null}
            onChange={(userId) => {
              if (userId)
                void run(async () => {
                  const { id } = await api.addProjectTeamMember(projectId, userId, "")
                  setJustAdded(id)
                })
            }}
            placeholder={t("team.addPlaceholder")}
          />
        )}
        {isAdmin && <Hint>{t("team.candidatesHint")}</Hint>}
        <FormError>{error}</FormError>
      </div>
    </div>
  )
}

export { ProjectTeamCard }
