import type { ReactNode } from "react"

import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { FormError } from "@/components/ui/form-error"
import { useLanguage } from "@/i18n/use-language"

function ConfirmDialog({
  open,
  title,
  description,
  confirmLabel,
  cancelLabel,
  variant = "default",
  onConfirm,
  onCancel,
  pending = false,
  error,
}: {
  open: boolean
  title: string
  description: ReactNode
  confirmLabel?: string
  cancelLabel?: string
  variant?: "default" | "destructive"
  onConfirm: () => void
  onCancel: () => void
  // Blokuje oba przyciski na czas trwania onConfirm i pokazuje error (jeśli podany) zamiast
  // pozwolić zamknąć/kliknąć dialog ponownie w trakcie — bez tego np. przy usuwaniu wielu
  // zaznaczonych elementów naraz nieudana operacja w środku pętli zostawiała okno "zawieszone"
  // (bez komunikatu, z aktywnym przyciskiem — ryzyko podwójnego kliknięcia).
  pending?: boolean
  error?: string | null
}) {
  const { t } = useLanguage()

  return (
    <Dialog open={open} onOpenChange={(next) => !next && !pending && onCancel()}>
      <DialogContent showCloseButton={!pending}>
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>{description}</DialogDescription>
        </DialogHeader>
        {error && <FormError>{error}</FormError>}
        <DialogFooter>
          <Button variant="outline" onClick={onCancel} disabled={pending}>
            {cancelLabel ?? t("common.cancel")}
          </Button>
          <Button variant={variant} onClick={onConfirm} disabled={pending}>
            {confirmLabel ?? t("common.confirm")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export { ConfirmDialog }
