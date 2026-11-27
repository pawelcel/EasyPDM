import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { useLanguage } from "@/i18n/use-language"

interface ContactDetails {
  firstName: string | null
  lastName: string | null
  position: string | null
  phone: string | null
  email: string | null
  address: string | null
}

// A client contact's details without leaving the project: the project lead's and those of the
// people on the client's side (the ⓘ next to each).
function ContactInfoDialog({ contact, onClose }: { contact: ContactDetails; onClose: () => void }) {
  const { t } = useLanguage()
  const rows: [string, string | null][] = [
    [t("common.position"), contact.position],
    [t("common.phone"), contact.phone],
    [t("common.email"), contact.email],
    [t("common.address"), contact.address],
  ]
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{[contact.firstName, contact.lastName].filter(Boolean).join(" ")}</DialogTitle>
        </DialogHeader>
        <div className="flex flex-col gap-2 text-sm">
          {rows.map(([label, value]) => (
            <div key={label} className="flex justify-between gap-4">
              <span className="text-muted-foreground">{label}</span>
              <span>{value || "-"}</span>
            </div>
          ))}
        </div>
      </DialogContent>
    </Dialog>
  )
}

export { ContactInfoDialog }
