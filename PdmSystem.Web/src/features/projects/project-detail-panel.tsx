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
}: {
  project: Project
  isAdmin: boolean
  onUpdated: () => void | Promise<void>
  onDeleted: () => void | Promise<void>
}) {
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
      setError(err instanceof Error ? err.message : "Nie udało się zapisać zmian.")
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
        {isAdmin && (
          <Button size="sm" variant="destructive" onClick={() => setConfirmingDelete(true)}>
            Usuń projekt
          </Button>
        )}
      </div>
      <div className="text-[12.5px] text-muted-foreground">
        Projekt · utworzono {created} · {project.itemCount}{" "}
        {project.itemCount === 1 ? "element" : "elementów"}
      </div>

      <SectionLabel>Właściwości</SectionLabel>
      <div className="flex flex-col gap-2">
        <Label htmlFor="project-name">Nazwa</Label>
        <Input
          id="project-name"
          value={form.name}
          disabled={!isAdmin}
          onChange={(e) => setForm((f) => ({ ...f, name: e.target.value }))}
          onBlur={() => save(form)}
        />

        <Label htmlFor="project-description">Opis</Label>
        {isAdmin ? (
          <Input
            id="project-description"
            value={form.description}
            onChange={(e) => setForm((f) => ({ ...f, description: e.target.value }))}
            onBlur={() => save(form)}
            placeholder="brak opisu"
          />
        ) : project.description ? (
          <p className="text-sm whitespace-pre-wrap">{project.description}</p>
        ) : (
          <Hint>brak opisu</Hint>
        )}

        <Label htmlFor="project-client">Klient</Label>
        <Input
          id="project-client"
          value={form.client}
          disabled={!isAdmin}
          onChange={(e) => setForm((f) => ({ ...f, client: e.target.value }))}
          onBlur={() => save(form)}
          placeholder={isAdmin ? "brak" : undefined}
        />

        <div className="flex gap-2">
          <div className="flex flex-1 flex-col gap-2">
            <Label htmlFor="project-start-date">Data rozpoczęcia</Label>
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
            <Label htmlFor="project-end-date">Data zakończenia</Label>
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
          title="Usuń projekt"
          description={`Na pewno usunąć projekt „${project.name}” wraz ze wszystkimi jego elementami (${project.itemCount})? Tej operacji nie można cofnąć.`}
          confirmLabel="Usuń projekt"
          variant="destructive"
          onConfirm={confirmDelete}
          onCancel={() => setConfirmingDelete(false)}
        />
      )}
    </div>
  )
}

export { ProjectDetailPanel }
