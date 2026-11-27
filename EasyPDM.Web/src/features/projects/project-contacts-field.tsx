import { useEffect, useState } from "react"
import { Info, X } from "lucide-react"

import { api } from "@/api/client"
import type { ClientContact, ProjectContact } from "@/api/types"
import { Button } from "@/components/ui/button"
import {
  Combobox,
  ComboboxContent,
  ComboboxEmpty,
  ComboboxInput,
  ComboboxItem,
  ComboboxList,
} from "@/components/ui/combobox"
import { FormError } from "@/components/ui/form-error"
import { Hint } from "@/components/ui/hint"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { ContactInfoDialog } from "@/features/projects/contact-info-dialog"
import { useLanguage } from "@/i18n/use-language"

function personLabel(contact: { firstName: string | null; lastName: string | null } | undefined): string {
  if (!contact) return ""
  return [contact.firstName, contact.lastName].filter(Boolean).join(" ")
}

// People on the client's side involved in the project, each with a role (purchasing,
// acceptance...), under the project lead and picked from the same contacts: the client's own and
// those of the project's Name 2 (availableContacts, loaded by the panel for the lead). One person
// can be added several times with different roles, so a row is addressed by its own id. The server
// drops the ones who no longer belong when the client or Name 2 changes, so the list is reloaded
// whenever those change. Only an administrator edits it, as the other project fields.
function ProjectContactsField({
  projectId,
  clientId,
  clientName2Id,
  availableContacts,
  isAdmin,
}: {
  projectId: string
  clientId: number | null
  clientName2Id: number | null
  availableContacts: ClientContact[]
  isAdmin: boolean
}) {
  const { t } = useLanguage()
  const [people, setPeople] = useState<ProjectContact[]>([])
  const [roles, setRoles] = useState<Record<number, string>>({})
  const [newContactId, setNewContactId] = useState<number | null>(null)
  const [newRole, setNewRole] = useState("")
  const [error, setError] = useState("")
  const [showing, setShowing] = useState<ProjectContact | null>(null)

  async function reload() {
    try {
      const list = await api.getProjectContacts(projectId)
      setPeople(list)
      setRoles(Object.fromEntries(list.map((p) => [p.id, p.role])))
    } catch {
      setPeople([])
    }
  }

  useEffect(() => {
    void reload()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [projectId, clientId, clientName2Id])

  const candidates = availableContacts

  async function add() {
    if (newContactId === null) return
    setError("")
    try {
      await api.addProjectContact(projectId, newContactId, newRole)
      setNewContactId(null)
      setNewRole("")
      await reload()
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    }
  }

  async function saveRole(id: number) {
    const role = (roles[id] ?? "").trim()
    if (role === people.find((p) => p.id === id)?.role) return
    setError("")
    try {
      await api.updateProjectContactRole(projectId, id, role)
      await reload()
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    }
  }

  async function remove(id: number) {
    setError("")
    try {
      await api.removeProjectContact(projectId, id)
      await reload()
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    }
  }

  return (
    <div className="flex flex-col gap-2">
      <Label>{t("project.contacts")}</Label>
      {!clientId ? (
        <Hint>{t("project.leadContactNeedsClientHint")}</Hint>
      ) : (
        <>
          {people.length === 0 ? (
            <Hint>{t("project.noContacts")}</Hint>
          ) : (
            <ul className="flex flex-col gap-1.5">
              {people.map((p) => (
                <li key={p.id} className="flex items-center gap-2">
                  <div className="w-1/3 min-w-0 truncate text-sm">
                    {personLabel(p)}
                    {p.position && <span className="ml-1.5 text-[12.5px] text-muted-foreground">{p.position}</span>}
                  </div>
                  {isAdmin ? (
                    <Input
                      className="flex-1"
                      value={roles[p.id] ?? ""}
                      placeholder={t("project.contactRolePlaceholder")}
                      aria-label={t("project.contactRole")}
                      onChange={(e) => setRoles((r) => ({ ...r, [p.id]: e.target.value }))}
                      onBlur={() => saveRole(p.id)}
                    />
                  ) : (
                    <span className="flex-1 truncate text-sm text-muted-foreground">{p.role || "-"}</span>
                  )}
                  <Button
                    type="button"
                    size="icon"
                    variant="outline"
                    aria-label={t("project.contactDetailsAria")}
                    title={t("project.contactDetailsAria")}
                    onClick={() => setShowing(p)}
                  >
                    <Info className="size-4" />
                  </Button>
                  {isAdmin && (
                    <Button
                      type="button"
                      size="icon-xs"
                      variant="ghost"
                      aria-label={t("project.removeContactAria")}
                      title={t("project.removeContactAria")}
                      onClick={() => remove(p.id)}
                    >
                      <X />
                    </Button>
                  )}
                </li>
              ))}
            </ul>
          )}
          {isAdmin &&
            (candidates.length > 0 ? (
              <div className="flex items-center gap-2">
                <div className="w-1/3 min-w-0">
                  <Combobox
                    items={candidates.map((c) => c.id)}
                    value={newContactId}
                    onValueChange={(v) => setNewContactId((v as number | null) ?? null)}
                    itemToStringLabel={(id: number) => personLabel(candidates.find((c) => c.id === id))}
                  >
                    <ComboboxInput placeholder={t("project.addContactPlaceholder")} showClear />
                    <ComboboxContent>
                      <ComboboxEmpty>{t("project.noMatchingLeadContact")}</ComboboxEmpty>
                      <ComboboxList>
                        {(id: number) => (
                          <ComboboxItem key={id} value={id}>
                            {personLabel(candidates.find((c) => c.id === id))}
                          </ComboboxItem>
                        )}
                      </ComboboxList>
                    </ComboboxContent>
                  </Combobox>
                </div>
                <Input
                  className="flex-1"
                  value={newRole}
                  placeholder={t("project.contactRolePlaceholder")}
                  aria-label={t("project.contactRole")}
                  onChange={(e) => setNewRole(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === "Enter") void add()
                  }}
                />
                <Button type="button" size="sm" variant="outline" disabled={newContactId === null} onClick={add}>
                  {t("project.addContact")}
                </Button>
              </div>
            ) : (
              availableContacts.length === 0 && <Hint>{t("project.noLeadContactsHint")}</Hint>
            ))}
          <FormError>{error}</FormError>
          {showing && <ContactInfoDialog contact={showing} onClose={() => setShowing(null)} />}
        </>
      )}
    </div>
  )
}

export { ProjectContactsField }
