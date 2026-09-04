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
  singleAckOnError = false,
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
  // Dla akcji, których błąd oznacza trwałą przeszkodę (np. próba wydania złożenia z
  // anulowanym elementem w BOM-ie) — ponowne kliknięcie "Potwierdź" bez zmiany czegokolwiek
  // gdzie indziej zawsze skończy się tym samym błędem, więc para Anuluj/Potwierdź po
  // niepowodzeniu wygląda jak dwie opcje, z których jedna (Potwierdź) nic realnie nie robi.
  // Ten prop, gdy true, zamienia OBA przyciski w JEDEN "OK" (zamyka dialog jak Anuluj) w
  // chwili pojawienia się błędu — celowo opt-in, żeby nie zmieniać domyślnego zachowania
  // pozostałych 11 miejsc korzystających z tego komponentu, gdzie ponowna próba (np. usuwania
  // po przejściowym błędzie sieci) ma sens.
  singleAckOnError?: boolean
}) {
  const { t } = useLanguage()
  const showOnlyAck = singleAckOnError && !!error

  return (
    <Dialog open={open} onOpenChange={(next) => !next && !pending && onCancel()}>
      <DialogContent showCloseButton={!pending}>
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>{description}</DialogDescription>
        </DialogHeader>
        {error && <FormError>{error}</FormError>}
        <DialogFooter>
          {showOnlyAck ? (
            <Button variant="outline" onClick={onCancel} disabled={pending}>
              {t("common.ok")}
            </Button>
          ) : (
            <>
              <Button variant="outline" onClick={onCancel} disabled={pending}>
                {cancelLabel ?? t("common.cancel")}
              </Button>
              <Button variant={variant} onClick={onConfirm} disabled={pending}>
                {confirmLabel ?? t("common.confirm")}
              </Button>
            </>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export { ConfirmDialog }
