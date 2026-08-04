import { useState } from "react"

import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { cn } from "@/lib/utils"
import { useLanguage } from "@/i18n/use-language"

function AddTagRow({ onAdd, className }: { onAdd: (name: string) => void; className?: string }) {
  const { t } = useLanguage()
  const [value, setValue] = useState("")

  function submit() {
    const trimmed = value.trim()
    if (!trimmed) return
    onAdd(trimmed)
    setValue("")
  }

  return (
    <div className={cn("flex gap-1.5", className)}>
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
