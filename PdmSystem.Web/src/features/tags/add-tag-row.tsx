import { useState } from "react"

import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { useLanguage } from "@/i18n/use-language"

function AddTagRow({ onAdd }: { onAdd: (name: string) => void }) {
  const { t } = useLanguage()
  const [value, setValue] = useState("")

  function submit() {
    const trimmed = value.trim()
    if (!trimmed) return
    onAdd(trimmed)
    setValue("")
  }

  return (
    <div className="mt-2 flex gap-1.5">
      <Input
        value={value}
        onChange={(e) => setValue(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === "Enter") submit()
        }}
        placeholder={t("item.newTagPlaceholder")}
        className="h-7 text-[13px]"
      />
      <Button size="sm" variant="secondary" onClick={submit}>
        {t("common.add")}
      </Button>
    </div>
  )
}

export { AddTagRow }
