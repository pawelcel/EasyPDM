import type { TeamUser } from "@/api/types"
import {
  Combobox,
  ComboboxContent,
  ComboboxEmpty,
  ComboboxInput,
  ComboboxItem,
  ComboboxList,
} from "@/components/ui/combobox"
import { useLanguage } from "@/i18n/use-language"

// Picks one EasyPDM user out of a given list (the project's candidates or its team).
function UserCombobox({
  users,
  value,
  onChange,
  placeholder,
  disabled,
  id,
}: {
  users: TeamUser[]
  value: string | null
  onChange: (userId: string | null) => void
  placeholder: string
  disabled?: boolean
  id?: string
}) {
  const { t } = useLanguage()
  const nameOf = (userId: string) => users.find((u) => u.userId === userId)?.name ?? ""
  return (
    <Combobox
      items={users.map((u) => u.userId)}
      value={value}
      onValueChange={(v) => onChange((v as string | null) ?? null)}
      itemToStringLabel={nameOf}
      disabled={disabled}
    >
      <ComboboxInput id={id} placeholder={placeholder} showClear />
      <ComboboxContent>
        <ComboboxEmpty>{t("team.noMatchingUser")}</ComboboxEmpty>
        <ComboboxList>
          {(userId: string) => (
            <ComboboxItem key={userId} value={userId}>
              {nameOf(userId)}
            </ComboboxItem>
          )}
        </ComboboxList>
      </ComboboxContent>
    </Combobox>
  )
}

export { UserCombobox }
