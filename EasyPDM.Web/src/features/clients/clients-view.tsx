import { useState } from "react"
import { Plus, Trash2 } from "lucide-react"

import { api, ApiError } from "@/api/client"
import type { Client } from "@/api/types"
import { Button } from "@/components/ui/button"
import {
  Combobox,
  ComboboxContent,
  ComboboxEmpty,
  ComboboxInput,
  ComboboxItem,
  ComboboxList,
} from "@/components/ui/combobox"
import { ConfirmDialog } from "@/components/ui/confirm-dialog"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog"
import { FormError } from "@/components/ui/form-error"
import { Hint } from "@/components/ui/hint"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Table, TableBody, TableCell, TableRow } from "@/components/ui/table"
import { useDebouncedValue } from "@/hooks/use-debounced-value"
import { ClientDetailPanel } from "@/features/clients/client-detail-panel"
import { useClients } from "@/features/clients/use-clients"
import { useLanguage } from "@/i18n/use-language"

// Płaska tabela: klient bez nazw 2 daje jeden wiersz (Nazwa 2 puste), klient z nazwami 2 —
// po wierszu na każdą, tak żeby wszystkie warianty ("Bosch" / "Bosch Polska" / "Bosch
// Rexroth" / ...) były widoczne od razu w liście po lewej, bez wchodzenia w szczegóły
// klienta -- ten sam wzorzec co płaska tabela Seria/Typ + Podtyp w zakładce Producenci.
function buildRows(clients: Client[]) {
  const rows: {
    clientId: number
    clientName: string | null
    contactCount: number | null
    name2Id: number | null
    name2: string | null
  }[] = []
  for (const c of clients) {
    if (c.name2s.length === 0) {
      rows.push({ clientId: c.id, clientName: c.name, contactCount: c.contactCount, name2Id: null, name2: null })
      continue
    }
    c.name2s.forEach((n, index) => {
      rows.push({
        clientId: c.id,
        clientName: index === 0 ? c.name : null,
        contactCount: index === 0 ? c.contactCount : null,
        name2Id: n.id,
        name2: n.name2,
      })
    })
  }
  return rows
}

function ClientsView({ onNavigateToProject }: { onNavigateToProject?: (id: string) => void }) {
  const { t } = useLanguage()
  const [search, setSearch] = useState("")
  const debouncedSearch = useDebouncedValue(search, 300)
  const { clients, refetch } = useClients(debouncedSearch)
  const [selectedId, setSelectedId] = useState<number | null>(null)
  const rows = buildRows(clients)

  const [confirmingDeleteName2, setConfirmingDeleteName2] = useState<
    { clientId: number; name2Id: number; name2: string } | null
  >(null)
  const [name2DeletePending, setName2DeletePending] = useState(false)
  const [name2DeleteError, setName2DeleteError] = useState<string | null>(null)

  async function confirmRemoveName2() {
    if (!confirmingDeleteName2) return
    setName2DeletePending(true)
    setName2DeleteError(null)
    try {
      await api.removeClientName2(confirmingDeleteName2.clientId, confirmingDeleteName2.name2Id)
      setConfirmingDeleteName2(null)
      await refetch()
    } catch (err) {
      setName2DeleteError(err instanceof ApiError ? err.message : t("client.deleteName2Failed"))
    } finally {
      setName2DeletePending(false)
    }
  }

  return (
    <div className="grid grid-cols-3 gap-4">
      <div className="col-span-1 flex flex-col gap-2 rounded-xl bg-card p-2 ring-1 ring-foreground/10">
        <div className="flex items-center gap-2 p-0.5">
          <Input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder={t("client.searchPlaceholder")}
            className="flex-1"
          />
          <NewClientDialog
            clients={clients}
            onCreated={async (id) => {
              await refetch()
              setSelectedId(id)
            }}
          />
        </div>

        {rows.length > 0 ? (
          <Table>
            <TableBody>
              {rows.map((row) => (
                <TableRow
                  key={`${row.clientId}-${row.name2Id ?? "none"}`}
                  onClick={() => setSelectedId(row.clientId)}
                  data-state={selectedId === row.clientId ? "selected" : undefined}
                  className="cursor-pointer"
                >
                  <TableCell>
                    {row.clientName ? (
                      <>
                        {row.clientName}
                        {row.name2 && <span className="text-muted-foreground"> — {row.name2}</span>}
                      </>
                    ) : (
                      <span className="text-muted-foreground">↳ {row.name2}</span>
                    )}
                  </TableCell>
                  <TableCell>
                    {row.name2Id !== null && row.name2 !== null && (
                      <Button
                        size="icon-xs"
                        variant="ghost"
                        aria-label={t("client.deleteName2Aria")}
                        onClick={(e) => {
                          e.stopPropagation()
                          setConfirmingDeleteName2({ clientId: row.clientId, name2Id: row.name2Id!, name2: row.name2! })
                        }}
                      >
                        <Trash2 className="size-3.5 text-muted-foreground" />
                      </Button>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        ) : (
          <Hint>{search ? t("client.noMatches") : t("client.emptyAll")}</Hint>
        )}
      </div>

      {confirmingDeleteName2 && (
        <ConfirmDialog
          open
          title={t("client.deleteName2Aria")}
          description={t("client.deleteName2ConfirmDescription", { name: confirmingDeleteName2.name2 })}
          confirmLabel={t("common.delete")}
          variant="destructive"
          onConfirm={confirmRemoveName2}
          onCancel={() => setConfirmingDeleteName2(null)}
          pending={name2DeletePending}
          error={name2DeleteError}
        />
      )}

      <div className="col-span-2 rounded-xl bg-card p-4 ring-1 ring-foreground/10">
        {selectedId ? (
          <ClientDetailPanel
            key={selectedId}
            id={selectedId}
            onClientsRefetch={refetch}
            onDeleted={() => setSelectedId(null)}
            onNavigateToProject={onNavigateToProject}
          />
        ) : (
          <Hint>{t("client.selectHint")}</Hint>
        )}
      </div>
    </div>
  )
}

// Okno "dynamiczne": pole nazwy to Combobox — można wybrać istniejącego klienta z listy albo
// wpisać zupełnie nową nazwę. Wybrana/wpisana nazwa jest na bieżąco porównywana z katalogiem;
// jeśli pasuje do JUŻ ISTNIEJĄCEGO klienta, okno przełącza się w tryb "dodaj temu klientowi
// nazwę 2" zamiast próbować założyć drugiego klienta o tej samej nazwie (co i tak odbiłoby
// się od UNIQUE na clients.name) — dokładnie to, co pozwala jednemu "Bosch" mieć wiele nazw 2
// zamiast wielu osobnych, niepowiązanych ze sobą wpisów "Bosch" w katalogu.
function NewClientDialog({
  clients,
  onCreated,
}: {
  clients: Client[]
  onCreated: (id: number) => void | Promise<void>
}) {
  const { t } = useLanguage()
  const [open, setOpen] = useState(false)
  const [name, setName] = useState("")
  const [name2, setName2] = useState("")
  const [error, setError] = useState("")
  const [pending, setPending] = useState(false)
  const clientNames = clients.map((c) => c.name)

  const trimmedName = name.trim()
  const matchedClient = trimmedName
    ? (clients.find((c) => c.name.toLowerCase() === trimmedName.toLowerCase()) ?? null)
    : null

  function reset() {
    setName("")
    setName2("")
    setError("")
  }

  async function submit() {
    if (!trimmedName) {
      setError(t("client.nameRequired"))
      return
    }
    setError("")
    setPending(true)
    try {
      if (matchedClient) {
        const trimmedName2 = name2.trim()
        if (!trimmedName2) {
          setError(t("client.name2Required"))
          return
        }
        await api.addClientName2(matchedClient.id, trimmedName2)
        setOpen(false)
        reset()
        await onCreated(matchedClient.id)
      } else {
        const { id } = await api.createClient({ name: trimmedName, location: null })
        setOpen(false)
        reset()
        await onCreated(id)
      }
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        setError(matchedClient ? t("client.name2Conflict") : t("client.nameConflict"))
      } else {
        setError(matchedClient ? t("client.addName2Failed") : t("client.addFailed"))
      }
    } finally {
      setPending(false)
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
      <DialogTrigger render={<Button size="icon-sm" aria-label={t("client.addAria")}><Plus className="size-4" /></Button>} />
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{matchedClient ? t("client.addName2Title") : t("client.addTitle")}</DialogTitle>
        </DialogHeader>
        <div className="flex flex-col gap-2">
          <Label htmlFor="client-name">{t("common.name")}</Label>
          <Combobox
            items={clientNames}
            value={name || null}
            inputValue={name}
            onInputValueChange={(v) => setName(v)}
            onValueChange={(v) => setName((v as string | null) ?? "")}
          >
            <ComboboxInput id="client-name" placeholder={t("client.namePlaceholder")} showClear />
            <ComboboxContent>
              <ComboboxEmpty>{t("client.newClientHint")}</ComboboxEmpty>
              <ComboboxList>
                {(clientName: string) => (
                  <ComboboxItem key={clientName} value={clientName}>
                    {clientName}
                  </ComboboxItem>
                )}
              </ComboboxList>
            </ComboboxContent>
          </Combobox>
          {matchedClient && (
            <>
              <Hint>{t("client.existingClientHint")}</Hint>
              <Label htmlFor="client-new-name2">{t("client.name2Label")}</Label>
              <Input
                id="client-new-name2"
                value={name2}
                onChange={(e) => setName2(e.target.value)}
                placeholder={t("client.name2Placeholder")}
              />
            </>
          )}
          <FormError>{error}</FormError>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={() => setOpen(false)}>
            {t("common.cancel")}
          </Button>
          <Button onClick={submit} disabled={pending}>
            {matchedClient ? t("common.ok") : t("common.add")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export { ClientsView }
