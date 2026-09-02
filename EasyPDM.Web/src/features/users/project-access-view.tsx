import { useCallback, useEffect, useState } from "react"

import { api } from "@/api/client"
import type { ManagedUser, Project, ProjectUserAssignment } from "@/api/types"
import { FormError } from "@/components/ui/form-error"
import { Hint } from "@/components/ui/hint"
import { SectionLabel } from "@/components/ui/section-label"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { useLanguage } from "@/i18n/use-language"

// Administrator zawsze widzi/może przeglądać wszystkie projekty niezależnie od przypisań —
// nie ma sensu dawać mu tu checkboxa, który i tak nic by nie zmienił, więc lista po prawej
// pokazuje tylko konta z rolą "user".
function ProjectAccessView({ users }: { users: ManagedUser[] }) {
  const { t } = useLanguage()
  const [projects, setProjects] = useState<Project[]>([])
  const [assignments, setAssignments] = useState<ProjectUserAssignment[]>([])
  const [selectedProjectId, setSelectedProjectId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const refetch = useCallback(async () => {
    const [projectsData, assignmentsData] = await Promise.all([
      api.getProjects(),
      api.getProjectUsers(),
    ])
    setProjects(projectsData)
    setAssignments(assignmentsData)
  }, [])

  useEffect(() => {
    refetch()
  }, [refetch])

  useEffect(() => {
    if (selectedProjectId === null && projects.length > 0) {
      setSelectedProjectId(projects[0].id)
    }
  }, [projects, selectedProjectId])

  const projectName = (id: string) => projects.find((p) => p.id === id)?.name ?? id

  const assignableUsers = users.filter((u) => u.role !== "admin")
  const assignedUserIds = new Set(
    assignments.filter((a) => a.projectId === selectedProjectId).map((a) => a.userId)
  )

  async function toggle(userId: string, checked: boolean) {
    if (!selectedProjectId) return
    try {
      setError(null)
      if (checked) {
        await api.grantProjectAccess(selectedProjectId, userId)
      } else {
        await api.revokeProjectAccess(selectedProjectId, userId)
      }
      await refetch()
    } catch (err) {
      setError(err instanceof Error ? err.message : t("projectAccess.saveFailed"))
    }
  }

  return (
    <div className="mt-6">
      <SectionLabel>{t("projectAccess.title")}</SectionLabel>
      <Hint>{t("projectAccess.description")}</Hint>

      {projects.length === 0 ? (
        <Hint>{t("projectAccess.noProjects")}</Hint>
      ) : (
        <div className="mt-2 flex flex-col gap-2">
          <Select
            value={selectedProjectId ?? undefined}
            onValueChange={(v) => setSelectedProjectId(v)}
          >
            <SelectTrigger className="w-full">
              <SelectValue placeholder={t("projectAccess.selectProject")}>
                {(v: string | null) => (v ? projectName(v) : t("projectAccess.selectProject"))}
              </SelectValue>
            </SelectTrigger>
            <SelectContent>
              {projects.map((project) => (
                <SelectItem key={project.id} value={project.id}>
                  {project.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>

          <div className="rounded-lg bg-muted/40 p-1.5">
            {assignableUsers.length === 0 ? (
              <Hint>{t("projectAccess.noUsers")}</Hint>
            ) : (
              <ul className="flex flex-col gap-0.5">
                {assignableUsers.map((u) => (
                  <li key={u.id}>
                    <label className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 text-sm hover:bg-accent">
                      <input
                        type="checkbox"
                        checked={assignedUserIds.has(u.id)}
                        onChange={(e) => toggle(u.id, e.target.checked)}
                        className="size-3.5 shrink-0 accent-primary"
                      />
                      <span className="truncate">
                        {u.displayName} <span className="text-muted-foreground">({u.username})</span>
                      </span>
                    </label>
                  </li>
                ))}
              </ul>
            )}
          </div>
          <FormError>{error}</FormError>
        </div>
      )}
    </div>
  )
}

export { ProjectAccessView }
