import { useState } from "react"
import { KeyRound, Trash2 } from "lucide-react"

import { api, ApiError } from "@/api/client"
import { ROLE_LABEL_KEYS, type ManagedUser, type UserRole } from "@/api/types"
import { Button } from "@/components/ui/button"
import { ConfirmDialog } from "@/components/ui/confirm-dialog"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog"
import { FormError } from "@/components/ui/form-error"
import { Hint } from "@/components/ui/hint"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { useAuth } from "@/features/auth/use-auth"
import { ProjectAccessView } from "@/features/users/project-access-view"
import { useUsers } from "@/features/users/use-users"
import { useLanguage } from "@/i18n/use-language"

function UsersView() {
  const { t } = useLanguage()
  const { users, refetch } = useUsers()
  const { user: me } = useAuth()
  const [error, setError] = useState("")
  const [deletingId, setDeletingId] = useState<string | null>(null)
  const [resettingId, setResettingId] = useState<string | null>(null)

  async function changeRole(u: ManagedUser, role: UserRole) {
    if (role === u.role) return
    setError("")
    try {
      await api.updateUser(u.id, { role })
      await refetch()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t("users.roleChangeFailed"))
    }
  }

  async function confirmDelete() {
    if (!deletingId) return
    const id = deletingId
    setDeletingId(null)
    try {
      await api.deleteUser(id)
      await refetch()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t("users.deleteFailed"))
    }
  }

  const deletingUser = users.find((u) => u.id === deletingId)
  const resettingUser = users.find((u) => u.id === resettingId)

  return (
    <div className="mx-auto max-w-4xl">
      <h2 className="mb-4 text-lg font-semibold tracking-tight">{t("settings.users")}</h2>

      <div className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
        <div className="mb-3 flex items-center justify-between gap-2">
          <AddUserDialog onAdded={refetch} />
        </div>

        <FormError>{error}</FormError>

        {users.length > 0 ? (
          <ul className="flex flex-col gap-0.5">
            {users.map((u) => (
              <li
                key={u.id}
                className="flex items-center justify-between gap-2 rounded-md px-2 py-1.5 text-sm hover:bg-accent"
              >
                <div className="min-w-0 flex-1">
                  <div className="truncate font-medium">
                    {u.displayName} <span className="text-muted-foreground">({u.username})</span>
                    {u.id === me?.id && (
                      <span className="ml-1.5 text-[12.5px] text-muted-foreground">({t("users.you")})</span>
                    )}
                  </div>
                  {u.email && (
                    <div className="truncate text-[12.5px] text-muted-foreground">{u.email}</div>
                  )}
                </div>

                <Select value={u.role} onValueChange={(v) => changeRole(u, v as UserRole)}>
                  <SelectTrigger className="w-40 shrink-0">
                    <SelectValue>{(v: string) => t(ROLE_LABEL_KEYS[v as UserRole])}</SelectValue>
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="admin">{t(ROLE_LABEL_KEYS.admin)}</SelectItem>
                    <SelectItem value="user">{t(ROLE_LABEL_KEYS.user)}</SelectItem>
                  </SelectContent>
                </Select>

                <Button
                  size="icon-xs"
                  variant="ghost"
                  aria-label={t("users.changePasswordAria", { username: u.username })}
                  onClick={() => setResettingId(u.id)}
                >
                  <KeyRound className="size-3.5 text-muted-foreground" />
                </Button>
                <Button
                  size="icon-xs"
                  variant="ghost"
                  aria-label={t("common.deleteNamed", { name: u.username })}
                  disabled={u.id === me?.id}
                  onClick={() => setDeletingId(u.id)}
                >
                  <Trash2 className="size-3.5 text-muted-foreground" />
                </Button>
              </li>
            ))}
          </ul>
        ) : (
          <Hint>{t("users.empty")}</Hint>
        )}

        <ProjectAccessView users={users} />
      </div>

      {deletingUser && (
        <ConfirmDialog
          open
          title={t("users.deleteTitle")}
          description={t("users.deleteConfirmDescription", { username: deletingUser.username })}
          confirmLabel={t("common.delete")}
          variant="destructive"
          onConfirm={confirmDelete}
          onCancel={() => setDeletingId(null)}
        />
      )}

      {resettingUser && (
        <ResetPasswordDialog user={resettingUser} onClose={() => setResettingId(null)} />
      )}
    </div>
  )
}

function AddUserDialog({ onAdded }: { onAdded: () => void | Promise<void> }) {
  const { t } = useLanguage()
  const [open, setOpen] = useState(false)
  const [username, setUsername] = useState("")
  const [password, setPassword] = useState("")
  const [displayName, setDisplayName] = useState("")
  const [email, setEmail] = useState("")
  const [role, setRole] = useState<UserRole>("user")
  const [error, setError] = useState("")

  function reset() {
    setUsername("")
    setPassword("")
    setDisplayName("")
    setEmail("")
    setRole("user")
    setError("")
  }

  async function add() {
    if (!username.trim() || !password || !displayName.trim()) {
      setError(t("users.addValidation"))
      return
    }
    try {
      await api.createUser({
        username: username.trim(),
        password,
        displayName: displayName.trim(),
        email: email.trim() || null,
        role,
      })
      setOpen(false)
      reset()
      await onAdded()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t("users.addFailed"))
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
      <DialogTrigger render={<Button>{t("users.addButton")}</Button>} />
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("users.addTitle")}</DialogTitle>
        </DialogHeader>

        <div className="flex flex-col gap-2">
          <Label htmlFor="user-username">{t("users.usernameLabel")}</Label>
          <Input id="user-username" value={username} onChange={(e) => setUsername(e.target.value)} />

          <Label htmlFor="user-display-name">{t("users.displayNameLabel")}</Label>
          <Input
            id="user-display-name"
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
          />

          <Label htmlFor="user-email">{t("users.emailOptionalLabel")}</Label>
          <Input id="user-email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} />

          <Label htmlFor="user-password">{t("users.passwordLabel")}</Label>
          <Input
            id="user-password"
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />

          <Label>{t("users.roleLabel")}</Label>
          <Select value={role} onValueChange={(v) => setRole(v as UserRole)}>
            <SelectTrigger className="w-full">
              <SelectValue>{(v: string) => t(ROLE_LABEL_KEYS[v as UserRole])}</SelectValue>
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="admin">{t(ROLE_LABEL_KEYS.admin)}</SelectItem>
              <SelectItem value="user">{t(ROLE_LABEL_KEYS.user)}</SelectItem>
            </SelectContent>
          </Select>

          <FormError>{error}</FormError>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => setOpen(false)}>
            {t("common.cancel")}
          </Button>
          <Button onClick={add}>{t("common.add")}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function ResetPasswordDialog({ user, onClose }: { user: ManagedUser; onClose: () => void }) {
  const { t } = useLanguage()
  const [password, setPassword] = useState("")
  const [error, setError] = useState("")
  const [submitting, setSubmitting] = useState(false)

  async function submit() {
    if (!password) {
      setError(t("users.newPasswordRequired"))
      return
    }
    setSubmitting(true)
    setError("")
    try {
      await api.updateUser(user.id, { password })
      onClose()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t("users.resetPasswordFailed"))
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Dialog open onOpenChange={(next) => !next && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("users.resetPasswordTitle", { username: user.username })}</DialogTitle>
        </DialogHeader>
        <div className="flex flex-col gap-2">
          <Label htmlFor="reset-password">{t("users.newPasswordLabel")}</Label>
          <Input
            id="reset-password"
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
          <FormError>{error}</FormError>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            {t("common.cancel")}
          </Button>
          <Button onClick={submit} disabled={submitting}>
            {submitting ? t("common.saving") : t("common.save")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export { UsersView }
