import { useState } from "react"
import { Trash2 } from "lucide-react"

import { api } from "@/api/client"
import { Button } from "@/components/ui/button"
import { Hint } from "@/components/ui/hint"
import { Input } from "@/components/ui/input"
import { Table, TableBody, TableCell, TableRow } from "@/components/ui/table"
import { useLanguage } from "@/i18n/use-language"

function PropertyEditor({
  itemId,
  properties,
  locked = false,
  onChanged,
}: {
  itemId: string
  properties: Record<string, unknown>
  locked?: boolean
  onChanged: () => void
}) {
  const { t } = useLanguage()
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
      {locked && <Hint>{t("item.propertiesLockedHint")}</Hint>}

      {entries.length > 0 ? (
        <Table>
          <TableBody>
            {entries.map(([key, value]) => (
              <PropertyRow
                key={key}
                propKey={key}
                value={String(value)}
                disabled={locked}
                onSave={(v) => updateValue(key, v)}
                onDelete={() => removeKey(key)}
              />
            ))}
          </TableBody>
        </Table>
      ) : (
        <Hint>{t("item.noProperties")}</Hint>
      )}

      <div className="mt-2 flex gap-1.5">
        <Input
          value={newKey}
          onChange={(e) => setNewKey(e.target.value)}
          placeholder={t("item.propKeyPlaceholder")}
          disabled={locked}
          className="h-7 w-28 text-[13px]"
        />
        <Input
          value={newValue}
          onChange={(e) => setNewValue(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter") addProperty()
          }}
          placeholder={t("item.propValuePlaceholder")}
          disabled={locked}
          className="h-7 text-[13px]"
        />
        <Button size="sm" variant="secondary" onClick={addProperty} disabled={locked}>
          {t("common.add")}
        </Button>
      </div>
    </div>
  )
}

function PropertyRow({
  propKey,
  value,
  disabled,
  onSave,
  onDelete,
}: {
  propKey: string
  value: string
  disabled: boolean
  onSave: (value: string) => void
  onDelete: () => void
}) {
  const { t } = useLanguage()
  const [draft, setDraft] = useState(value)

  return (
    <TableRow>
      <TableCell className="w-[35%] text-muted-foreground">{propKey}</TableCell>
      <TableCell>
        <Input
          value={draft}
          disabled={disabled}
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
        <Button
          size="icon-sm"
          variant="ghost"
          onClick={onDelete}
          disabled={disabled}
          aria-label={t("common.deleteNamed", { name: propKey })}
        >
          <Trash2 className="size-3.5 text-muted-foreground" />
        </Button>
      </TableCell>
    </TableRow>
  )
}

export { PropertyEditor }
