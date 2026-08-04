import { useState } from "react"

import { api } from "@/api/client"
import { isOwnerLocked, type Item } from "@/api/types"
import { Button } from "@/components/ui/button"
import { FormError } from "@/components/ui/form-error"
import { useAuth } from "@/features/auth/use-auth"
import { useLanguage } from "@/i18n/use-language"

// Właściciel Części/Złożenia — niezależne od statusu 'w_pracy'/isLocked. Dopóki
// ownerLocked=true, tylko ownerId może edytować element (nawet administrator nie omija
// tego — patrz backend). Każdy może zablokować zwolniony element, stając się przy tym
// jego nowym właścicielem; tylko aktualny właściciel może go z powrotem zwolnić.
// Wydana Część/Złożenie (status "wydany") nie może mieć właściciela ani być blokowana —
// zawsze pokazuje się jako zwolniona, niezależnie od tego, co faktycznie zapisano w bazie
// (backend też to wymusza przy zmianie statusu na "wydany" i odrzuca /lock, /release wtedy).
function OwnerControl({ item, onChanged }: { item: Item; onChanged: () => void | Promise<void> }) {
  const { t } = useLanguage()
  const { user } = useAuth()
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)
  const isIssued = item.status === "wydany"
  const locked = !isIssued && isOwnerLocked(item)
  const ownerName = isIssued ? null : item.ownerDisplayName
  const isOwner = !isIssued && item.ownerId !== null && item.ownerId === user?.id

  async function handleLock() {
    setError(null)
    setPending(true)
    try {
      await api.lockItem(item.id)
      await onChanged()
    } catch (err) {
      setError(err instanceof Error ? err.message : t("item.lockFailed"))
    } finally {
      setPending(false)
    }
  }

  async function handleRelease() {
    setError(null)
    setPending(true)
    try {
      await api.releaseItem(item.id)
      await onChanged()
    } catch (err) {
      setError(err instanceof Error ? err.message : t("item.releaseFailed"))
    } finally {
      setPending(false)
    }
  }

  return (
    <div className="mt-2 flex flex-col gap-1">
      <span className="text-[12.5px] text-muted-foreground">
        {t("item.owner")}: {ownerName ?? t("item.ownerNone")}
      </span>

      <div className="flex gap-1.5">
        <Button
          size="sm"
          variant={locked ? "default" : "outline"}
          disabled={isIssued || pending || locked}
          onClick={handleLock}
        >
          {t("item.lock")}
        </Button>
        <Button
          size="sm"
          variant={!locked ? "default" : "outline"}
          disabled={isIssued || pending || !locked || !isOwner}
          onClick={handleRelease}
        >
          {t("item.release")}
        </Button>
      </div>

      {isIssued && <span className="text-[12.5px] text-muted-foreground">{t("item.ownerNotAvailableIssued")}</span>}
      {!isIssued && locked && !isOwner && (
        <span className="text-[12.5px] text-muted-foreground">{t("item.onlyOwnerCanRelease")}</span>
      )}
      <FormError>{error}</FormError>
    </div>
  )
}

export { OwnerControl }
