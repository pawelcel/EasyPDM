import { useState } from "react"

import { api, ApiError } from "@/api/client"
import type { Project } from "@/api/types"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog"
import { FormError } from "@/components/ui/form-error"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { useLanguage } from "@/i18n/use-language"

function NewProjectDialog({ onCreated }: { onCreated: (project: Project) => void }) {
  const { t } = useLanguage()
  const [open, setOpen] = useState(false)
  const [name, setName] = useState("")
  const [description, setDescription] = useState("")
  const [client, setClient] = useState("")
  const [startDate, setStartDate] = useState("")
  const [endDate, setEndDate] = useState("")
  const [error, setError] = useState("")
  const [submitting, setSubmitting] = useState(false)

  function reset() {
    setName("")
    setDescription("")
    setClient("")
    setStartDate("")
    setEndDate("")
    setError("")
  }

  async function handleCreate() {
    const trimmed = name.trim()
    if (!trimmed) {
      setError(t("project.newNameRequired"))
      return
    }

    setSubmitting(true)
    setError("")
    try {
      const project = await api.createProject({
        name: trimmed,
        description: description.trim() || null,
        client: client.trim() || null,
        startDate: startDate || null,
        endDate: endDate || null,
      })
      setOpen(false)
      reset()
      onCreated(project)
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        setError(t("project.nameConflict"))
      } else {
        setError(t("project.createFailed"))
      }
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next)
        if (!next) reset()
      }}
    >
      <DialogTrigger render={<Button>{t("project.newButton")}</Button>} />
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("project.newTitle")}</DialogTitle>
        </DialogHeader>

        <div className="flex flex-col gap-2">
          <Label htmlFor="new-project-name">{t("project.nameLabel")}</Label>
          <Input
            id="new-project-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder={t("project.nameLabel")}
          />
          <Label htmlFor="new-project-desc">{t("project.descriptionOptional")}</Label>
          <Input
            id="new-project-desc"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder={t("project.descriptionOptional")}
          />
          <Label htmlFor="new-project-client">{t("project.clientOptional")}</Label>
          <Input
            id="new-project-client"
            value={client}
            onChange={(e) => setClient(e.target.value)}
            placeholder={t("project.clientOptional")}
          />
          <div className="flex gap-2">
            <div className="flex flex-1 flex-col gap-2">
              <Label htmlFor="new-project-start">{t("project.startDate")}</Label>
              <Input
                id="new-project-start"
                type="date"
                value={startDate}
                onChange={(e) => setStartDate(e.target.value)}
              />
            </div>
            <div className="flex flex-1 flex-col gap-2">
              <Label htmlFor="new-project-end">{t("project.endDate")}</Label>
              <Input
                id="new-project-end"
                type="date"
                value={endDate}
                onChange={(e) => setEndDate(e.target.value)}
              />
            </div>
          </div>
          <FormError>{error}</FormError>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => setOpen(false)}>
            {t("common.cancel")}
          </Button>
          <Button onClick={handleCreate} disabled={submitting}>
            {t("project.createButton")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export { NewProjectDialog }
