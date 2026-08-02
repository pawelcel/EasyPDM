import { useEffect, useState } from "react"

import { api } from "@/api/client"
import type { Project } from "@/api/types"
import { Button } from "@/components/ui/button"
import { ConfirmDialog } from "@/components/ui/confirm-dialog"
import { FormError } from "@/components/ui/form-error"
import { Hint } from "@/components/ui/hint"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { SectionLabel } from "@/components/ui/section-label"
import { useLanguage } from "@/i18n/use-language"

type ProjectForm = {
  name: string
  description: string
  client: string
  startDate: string
  endDate: string
}

function formFromProject(project: Project): ProjectForm {
  return {
    name: project.name,
    description: project.description ?? "",
    client: project.client ?? "",
    startDate: project.startDate ?? "",
    endDate: project.endDate ?? "",
  }
}

function ProjectDetailPanel({
  project,
  isAdmin,
  onUpdated,
  onDeleted,
  onNavigateToProject,
}: {
  project: Project
  isAdmin: boolean
  onUpdated: () => void | Promise<void>
  onDeleted: () => void | Promise<void>
  onNavigateToProject?: () => void
}) {
  const { t } = useLanguage()
  const [form, setForm] = useState(() => formFromProject(project))
  const [error, setError] = useState("")
  const [confirmingDelete, setConfirmingDelete] = useState(false)

  // Odśwież formularz, gdy z zewnątrz przyjdą nowe dane projektu (np. po zapisie albo
  // przełączeniu na inny projekt) — nie ma osobnego trybu "edycji", pola są edytowalne
  // od razu (dla administratora), więc trzymamy je zsynchronizowane z danymi z serwera.
  useEffect(() => {
    setForm(formFromProject(project))
  }, [project])

  async function save(next: ProjectForm) {
    if (!next.name.trim()) return
    setError("")
    try {
      await api.updateProject(project.id, {
        name: next.name.trim(),
        description: next.description.trim() || null,
        client: next.client.trim() || null,
        startDate: next.startDate || null,
        endDate: next.endDate || null,
      })
      await onUpdated()
    } catch (err) {
      setError(err instanceof Error ? err.message : t("project.saveFailed"))
    }
  }

  async function confirmDelete() {
    await api.deleteProject(project.id)
    setConfirmingDelete(false)
    await onDeleted()
  }

  const created = new Date(project.createdAt).toLocaleString("pl-PL")

  return (
    <div>
      <div className="mb-1 flex items-start justify-between gap-2">
        <div className="text-[15px] font-semibold">{project.name}</div>
        <div className="flex shrink-0 gap-1.5">
          {onNavigateToProject && (
            <Button size="sm" variant="outline" onClick={onNavigateToProject}>
              {t("project.goToProject")}
            </Button>
          )}
          {isAdmin && (
            <Button size="sm" variant="destructive" onClick={() => setConfirmingDelete(true)}>
              {t("project.deleteButton")}
            </Button>
          )}
        </div>
      </div>
      <div className="text-[12.5px] text-muted-foreground">
        {t("project.subtitle", {
          date: created,
          count: project.itemCount,
          itemWord: t(project.itemCount === 1 ? "project.itemSingular" : "project.itemPlural"),
        })}
      </div>

      <SectionLabel>{t("item.properties")}</SectionLabel>
      <div className="flex flex-col gap-2">
        <Label htmlFor="project-name">{t("common.name")}</Label>
        <Input
          id="project-name"
          value={form.name}
          disabled={!isAdmin}
          onChange={(e) => setForm((f) => ({ ...f, name: e.target.value }))}
          onBlur={() => save(form)}
        />

        <Label htmlFor="project-description">{t("project.description")}</Label>
        {isAdmin ? (
          <Input
            id="project-description"
            value={form.description}
            onChange={(e) => setForm((f) => ({ ...f, description: e.target.value }))}
            onBlur={() => save(form)}
            placeholder={t("project.noDescription")}
          />
        ) : project.description ? (
          <p className="text-sm whitespace-pre-wrap">{project.description}</p>
        ) : (
          <Hint>{t("project.noDescription")}</Hint>
        )}

        <Label htmlFor="project-client">{t("project.client")}</Label>
        <Input
          id="project-client"
          value={form.client}
          disabled={!isAdmin}
          onChange={(e) => setForm((f) => ({ ...f, client: e.target.value }))}
          onBlur={() => save(form)}
          placeholder={isAdmin ? t("project.nonePlaceholder") : undefined}
        />

        <div className="flex gap-2">
          <div className="flex flex-1 flex-col gap-2">
            <Label htmlFor="project-start-date">{t("project.startDate")}</Label>
            <Input
              id="project-start-date"
              type="date"
              value={form.startDate}
              disabled={!isAdmin}
              onChange={(e) => setForm((f) => ({ ...f, startDate: e.target.value }))}
              onBlur={() => save(form)}
            />
          </div>
          <div className="flex flex-1 flex-col gap-2">
            <Label htmlFor="project-end-date">{t("project.endDate")}</Label>
            <Input
              id="project-end-date"
              type="date"
              value={form.endDate}
              disabled={!isAdmin}
              onChange={(e) => setForm((f) => ({ ...f, endDate: e.target.value }))}
              onBlur={() => save(form)}
            />
          </div>
        </div>

        <FormError>{error}</FormError>
      </div>

      {confirmingDelete && (
        <ConfirmDialog
          open
          title={t("project.deleteButton")}
          description={t("project.deleteConfirmDescription", {
            name: project.name,
            count: project.itemCount,
          })}
          confirmLabel={t("project.deleteButton")}
          variant="destructive"
          onConfirm={confirmDelete}
          onCancel={() => setConfirmingDelete(false)}
        />
      )}
    </div>
  )
}

export { ProjectDetailPanel }
