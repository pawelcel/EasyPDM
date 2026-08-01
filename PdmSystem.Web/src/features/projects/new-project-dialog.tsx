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

function NewProjectDialog({ onCreated }: { onCreated: (project: Project) => void }) {
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
      setError("Nazwa projektu jest wymagana.")
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
        setError("Projekt o tej nazwie już istnieje.")
      } else {
        setError("Nie udało się utworzyć projektu.")
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
      <DialogTrigger render={<Button>+ Projekt</Button>} />
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Nowy projekt</DialogTitle>
        </DialogHeader>

        <div className="flex flex-col gap-2">
          <Label htmlFor="new-project-name">Nazwa projektu</Label>
          <Input
            id="new-project-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Nazwa projektu"
          />
          <Label htmlFor="new-project-desc">Opis (opcjonalnie)</Label>
          <Input
            id="new-project-desc"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder="Opis (opcjonalnie)"
          />
          <Label htmlFor="new-project-client">Klient (opcjonalnie)</Label>
          <Input
            id="new-project-client"
            value={client}
            onChange={(e) => setClient(e.target.value)}
            placeholder="Klient (opcjonalnie)"
          />
          <div className="flex gap-2">
            <div className="flex flex-1 flex-col gap-2">
              <Label htmlFor="new-project-start">Data rozpoczęcia</Label>
              <Input
                id="new-project-start"
                type="date"
                value={startDate}
                onChange={(e) => setStartDate(e.target.value)}
              />
            </div>
            <div className="flex flex-1 flex-col gap-2">
              <Label htmlFor="new-project-end">Data zakończenia</Label>
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
            Anuluj
          </Button>
          <Button onClick={handleCreate} disabled={submitting}>
            Utwórz
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export { NewProjectDialog }
