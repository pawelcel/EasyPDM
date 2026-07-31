import { useState } from "react"
import { Trash2 } from "lucide-react"

import { api } from "@/api/client"
import { Button } from "@/components/ui/button"
import { Hint } from "@/components/ui/hint"
import { Input } from "@/components/ui/input"
import { Table, TableBody, TableCell, TableRow } from "@/components/ui/table"

function PropertyEditor({
  itemId,
  properties,
  onChanged,
}: {
  itemId: string
  properties: Record<string, unknown>
  onChanged: () => void
}) {
  const [newKey, setNewKey] = useState("")
  const [newValue, setNewValue] = useState("")
  const entries = Object.entries(properties)

  async function updateValue(key: string, value: string) {
    await api.updateProperties(itemId, { [key]: value })
    onChanged()
  }

  async function removeKey(key: string) {
    await api.deleteProperty(itemId, key)
    onChanged()
  }

  async function addProperty() {
    const key = newKey.trim()
    if (!key) return
    await api.updateProperties(itemId, { [key]: newValue.trim() })
    setNewKey("")
    setNewValue("")
    onChanged()
  }

  return (
    <div>
      {entries.length > 0 ? (
        <Table>
          <TableBody>
            {entries.map(([key, value]) => (
              <PropertyRow
                key={key}
                propKey={key}
                value={String(value)}
                onSave={(v) => updateValue(key, v)}
                onDelete={() => removeKey(key)}
              />
            ))}
          </TableBody>
        </Table>
      ) : (
        <Hint>brak właściwości</Hint>
      )}

      <div className="mt-2 flex gap-1.5">
        <Input
          value={newKey}
          onChange={(e) => setNewKey(e.target.value)}
          placeholder="klucz"
          className="h-7 w-28 text-[13px]"
        />
        <Input
          value={newValue}
          onChange={(e) => setNewValue(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter") addProperty()
          }}
          placeholder="wartość"
          className="h-7 text-[13px]"
        />
        <Button size="sm" variant="secondary" onClick={addProperty}>
          Dodaj
        </Button>
      </div>
    </div>
  )
}

function PropertyRow({
  propKey,
  value,
  onSave,
  onDelete,
}: {
  propKey: string
  value: string
  onSave: (value: string) => void
  onDelete: () => void
}) {
  const [draft, setDraft] = useState(value)

  return (
    <TableRow>
      <TableCell className="w-[35%] text-muted-foreground">{propKey}</TableCell>
      <TableCell>
        <Input
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          onBlur={() => {
            if (draft !== value) onSave(draft)
          }}
          onKeyDown={(e) => {
            if (e.key === "Enter") (e.target as HTMLInputElement).blur()
          }}
          className="h-7 text-[13px]"
        />
      </TableCell>
      <TableCell className="w-8">
        <Button size="icon-sm" variant="ghost" onClick={onDelete} aria-label={`Usuń ${propKey}`}>
          <Trash2 className="size-3.5 text-muted-foreground" />
        </Button>
      </TableCell>
    </TableRow>
  )
}

export { PropertyEditor }
