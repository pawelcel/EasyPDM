import { Fragment, useState } from "react"
import { Plus } from "lucide-react"

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
import { ClientName2DetailPanel } from "@/features/clients/client-name2-detail-panel"
import { useClients } from "@/features/clients/use-clients"
import { useLanguage } from "@/i18n/use-language"

// Zaznaczenie w liście po lewej -- klient sam (name2Id null) albo jedna konkretna jego
// Nazwa 2. Ten sam clientId niezależnie od tego, co dokładnie zaznaczono, bo obie ścieżki
// (klient/Nazwa 2) trzeba umieć powiązać z powrotem z klientem-rodzicem.
type Selection = { clientId: number; name2Id: number | null }

function ClientsView({ onNavigateToProject }: { onNavigateToProject?: (id: string) => void }) {
  const { t } = useLanguage()
  const [search, setSearch] = useState("")
  const debouncedSearch = useDebouncedValue(search, 300)
  const { clients, refetch } = useClients(debouncedSearch)
  const [selection, setSelection] = useState<Selection | null>(null)
  const [quickAddClient, setQuickAddClient] = useState<Client | null>(null)

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
              setSelection({ clientId: id, name2Id: null })
            }}
          />
        </div>

        {/* Grupowane: nazwa klienta jako nagłówek (pogrubiony, klikalny -> zaznacza samego
            klienta), a pod nią, wcięte, po jednym wierszu na każdą jego Nazwę 2 (klikalny ->
            zaznacza tę konkretną Nazwę 2, z ikonką kosza). Klient bez Nazw 2 to sam nagłówek,
            bez niczego pod spodem. */}
        {clients.length > 0 ? (
          <Table>
            <TableBody>
              {clients.map((c) => (
                <Fragment key={c.id}>
                  <TableRow
                    onClick={() => setSelection({ clientId: c.id, name2Id: null })}
                    data-state={
                      selection?.clientId === c.id && selection.name2Id === null ? "selected" : undefined
                    }
                    className="group cursor-pointer"
                  >
                    <TableCell className="font-medium">{c.name}</TableCell>
                    <TableCell className="w-8">
                      {/* Widoczny tylko po najechaniu -- szybki skrót do dodania kolejnej
                          Nazwy 2 temu klientowi bez przechodzenia przez wyszukiwanie w
                          NewClientDialog (ten sam mechanizm co Trash2 przy Nazwie 2 niżej). */}
                      <Button
                        size="icon-xs"
                        variant="ghost"
                        aria-label={t("client.quickAddName2Aria")}
                        className="opacity-0 group-hover:opacity-100"
                        onClick={(e) => {
                          e.stopPropagation()
                          setQuickAddClient(c)
                        }}
                      >
                        <Plus className="size-3.5 text-muted-foreground" />
                      </Button>
                    </TableCell>
                  </TableRow>
                  {c.name2s.map((n) => (
                    <TableRow
                      key={`${c.id}-${n.id}`}
                      onClick={() => setSelection({ clientId: c.id, name2Id: n.id })}
                      data-state={
                        selection?.clientId === c.id && selection.name2Id === n.id ? "selected" : undefined
                      }
                      className="cursor-pointer"
                    >
                      {/* Bez przycisku usunięcia tutaj -- usuwanie Nazwy 2 jest teraz w jej
                          własnym panelu szczegółów, w tym samym miejscu co usuwanie klienta
                          (zob. client-name2-detail-panel.tsx), zamiast dublować tę akcję
                          jeszcze raz w samej liście. */}
                      <TableCell colSpan={2} className="pl-6 text-muted-foreground">
                        {n.name2}
                      </TableCell>
                    </TableRow>
                  ))}
                </Fragment>
              ))}
            </TableBody>
          </Table>
        ) : (
          <Hint>{search ? t("client.noMatches") : t("client.emptyAll")}</Hint>
        )}
      </div>

      {quickAddClient && (
        <QuickAddName2Dialog
          client={quickAddClient}
          onClose={() => setQuickAddClient(null)}
          onCreated={refetch}
        />
      )}

      <div className="col-span-2 rounded-xl bg-card p-4 ring-1 ring-foreground/10">
        {selection === null ? (
          <Hint>{t("client.selectHint")}</Hint>
        ) : selection.name2Id === null ? (
          <ClientDetailPanel
            key={selection.clientId}
            id={selection.clientId}
            onClientsRefetch={refetch}
            onDeleted={() => setSelection(null)}
            onNavigateToProject={onNavigateToProject}
          />
        ) : (
          <ClientName2DetailPanel
            key={`${selection.clientId}-${selection.name2Id}`}
            clientId={selection.clientId}
            name2Id={selection.name2Id}
            onClientsRefetch={refetch}
            onDeleted={() => setSelection({ clientId: selection.clientId, name2Id: null })}
          />
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
      const trimmedName2 = name2.trim()
      // Nazwa 2 jest opcjonalna w obu gałęziach -- zarówno przy zakładaniu zupełnie nowego
      // klienta (może dostać swoją pierwszą Nazwę 2 od razu, ale nie musi), jak i przy
      // wskazaniu już istniejącego (puste pole = po prostu zaznacz go, bez dopisywania
      // niczego -- skrót zamiast osobnego wyszukiwania go na liście po lewej).
      let clientId: number
      if (matchedClient) {
        clientId = matchedClient.id
        if (trimmedName2) await api.addClientName2(clientId, trimmedName2)
      } else {
        clientId = (await api.createClient({ name: trimmedName, location: null })).id
        if (trimmedName2) await api.addClientName2(clientId, trimmedName2)
      }
      setOpen(false)
      reset()
      await onCreated(clientId)
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
          {/* Nazwa nad Nazwą 2 (nie obok siebie), ZAWSZE oba widoczne -- Nazwa 2 jest
              opcjonalna niezależnie od tego, czy nazwa jest nowa (nowy klient dostaje od
              razu swoją pierwszą Nazwę 2, jeśli ją wpisano) czy pasuje do istniejącego
              klienta (dopisanie mu kolejnej Nazwy 2, albo samo zaznaczenie go, jeśli pole
              zostawiono puste). */}
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
          <Label htmlFor="client-new-name2">{t("client.name2Label")}</Label>
          <Input
            id="client-new-name2"
            value={name2}
            onChange={(e) => setName2(e.target.value)}
            placeholder={t("client.name2Placeholder")}
          />
          {matchedClient && <Hint>{t("client.existingClientHint")}</Hint>}
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

// Skrót z listy po lewej (widoczny tylko po najechaniu na wiersz klienta, zob. przycisk "+"
// wyżej) -- ten sam efekt co NewClientDialog wpisujące już istniejącą nazwę klienta, tylko
// bez przechodzenia przez wyszukiwanie: klient jest już znany (to JEGO wiersz), więc pole
// nazwy jest tu z góry wypełnione i zablokowane, można wpisać tylko Nazwę 2. W odróżnieniu od
// NewClientDialog Nazwa 2 jest tu WYMAGANA (to jedyny sens tego przycisku -- samo zaznaczenie
// klienta i tak jest już jedno kliknięcie na jego wiersz obok).
function QuickAddName2Dialog({
  client,
  onClose,
  onCreated,
}: {
  client: Client
  onClose: () => void
  onCreated: () => void | Promise<void>
}) {
  const { t } = useLanguage()
  const [name2, setName2] = useState("")
  const [error, setError] = useState("")
  const [pending, setPending] = useState(false)

  async function submit() {
    const trimmedName2 = name2.trim()
    if (!trimmedName2) return
    setError("")
    setPending(true)
    try {
      await api.addClientName2(client.id, trimmedName2)
      onClose()
      await onCreated()
    } catch (err) {
      setError(err instanceof ApiError && err.status === 409 ? t("client.name2Conflict") : t("client.addName2Failed"))
    } finally {
      setPending(false)
    }
  }

  return (
    <Dialog open onOpenChange={(next) => !next && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("client.addName2Title")}</DialogTitle>
        </DialogHeader>
        <div className="flex flex-col gap-2">
          <Label htmlFor="quick-add-name2-client">{t("common.name")}</Label>
          <Input id="quick-add-name2-client" value={client.name} disabled />
          <Label htmlFor="quick-add-name2-value">{t("client.name2Label")}</Label>
          <Input
            id="quick-add-name2-value"
            value={name2}
            onChange={(e) => setName2(e.target.value)}
            placeholder={t("client.quickAddName2Placeholder")}
            autoFocus
          />
          <FormError>{error}</FormError>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            {t("common.cancel")}
          </Button>
          <Button onClick={submit} disabled={pending || !name2.trim()}>
            {t("common.ok")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export { ClientsView }
