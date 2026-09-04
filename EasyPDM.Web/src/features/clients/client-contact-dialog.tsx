import { useState, type ReactElement } from "react"

import type { ClientContact } from "@/api/types"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog"
import { FormError } from "@/components/ui/form-error"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { useLanguage } from "@/i18n/use-language"

// Formularz osoby kontaktowej — wspólny dla kontaktów klienta-rodzica
// (client-detail-panel.tsx) i kontaktów własnych jednej Nazwy 2
// (client-name2-detail-panel.tsx), które są osobnymi zbiorami wierszy w bazie
// (client_contacts.name2_id NULL vs ustawiony), ale mają identyczny kształt/formularz.
export type ContactFormBody = {
  firstName: string | null
  lastName: string | null
  phone: string | null
  position: string | null
  email: string | null
  address: string | null
}

function ContactDialog({
  trigger,
  title,
  confirmLabel,
  initial,
  onSubmit,
}: {
  trigger: ReactElement
  title: string
  confirmLabel: string
  initial?: ClientContact
  onSubmit: (body: ContactFormBody) => Promise<void>
}) {
  const { t } = useLanguage()
  const [open, setOpen] = useState(false)
  const [firstName, setFirstName] = useState(initial?.firstName ?? "")
  const [lastName, setLastName] = useState(initial?.lastName ?? "")
  const [phone, setPhone] = useState(initial?.phone ?? "")
  const [position, setPosition] = useState(initial?.position ?? "")
  const [email, setEmail] = useState(initial?.email ?? "")
  const [address, setAddress] = useState(initial?.address ?? "")
  const [error, setError] = useState("")

  function reset() {
    setFirstName(initial?.firstName ?? "")
    setLastName(initial?.lastName ?? "")
    setPhone(initial?.phone ?? "")
    setPosition(initial?.position ?? "")
    setEmail(initial?.email ?? "")
    setAddress(initial?.address ?? "")
    setError("")
  }

  async function submit() {
    setError("")
    try {
      await onSubmit({
        firstName: firstName.trim() || null,
        lastName: lastName.trim() || null,
        phone: phone.trim() || null,
        position: position.trim() || null,
        email: email.trim() || null,
        address: address.trim() || null,
      })
      setOpen(false)
      reset()
    } catch {
      setError(t("client.saveContactFailed"))
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next)
        if (!next) reset()
      }}
    >
      <DialogTrigger render={trigger} />
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
        </DialogHeader>

        <div className="flex flex-col gap-2">
          <div className="flex gap-2">
            <div className="flex flex-1 flex-col gap-2">
              <Label htmlFor="client-contact-first-name">{t("common.firstName")}</Label>
              <Input id="client-contact-first-name" value={firstName} onChange={(e) => setFirstName(e.target.value)} />
            </div>
            <div className="flex flex-1 flex-col gap-2">
              <Label htmlFor="client-contact-last-name">{t("common.lastName")}</Label>
              <Input id="client-contact-last-name" value={lastName} onChange={(e) => setLastName(e.target.value)} />
            </div>
          </div>

          <Label htmlFor="client-contact-position">{t("common.position")}</Label>
          <Input id="client-contact-position" value={position} onChange={(e) => setPosition(e.target.value)} />

          <Label htmlFor="client-contact-phone">{t("common.phoneNumber")}</Label>
          <Input id="client-contact-phone" value={phone} onChange={(e) => setPhone(e.target.value)} />

          <Label htmlFor="client-contact-email">{t("common.email")}</Label>
          <Input id="client-contact-email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} />

          <Label htmlFor="client-contact-address">{t("common.address")}</Label>
          <Input id="client-contact-address" value={address} onChange={(e) => setAddress(e.target.value)} />

          <FormError>{error}</FormError>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => setOpen(false)}>
            {t("common.cancel")}
          </Button>
          <Button onClick={submit}>{confirmLabel}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export { ContactDialog }
