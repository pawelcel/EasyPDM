import { useState } from "react"
import { KeyRound, Trash2 } from "lucide-react"

import { api, ApiError } from "@/api/client"
import { ROLE_LABELS, type ManagedUser, type UserRole } from "@/api/types"
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
import { useUsers } from "@/features/users/use-users"

function UsersView() {
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
      setError(err instanceof ApiError ? err.message : "Nie udało się zmienić roli.")
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
      setError(err instanceof ApiError ? err.message : "Nie udało się usunąć użytkownika.")
    }
  }

  const deletingUser = users.find((u) => u.id === deletingId)
  const resettingUser = users.find((u) => u.id === resettingId)

  return (
    <div className="mx-auto max-w-2xl">
      <h2 className="mb-4 text-lg font-semibold tracking-tight">Użytkownicy</h2>

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
                    {u.id === me?.id && <span className="ml-1.5 text-[12.5px] text-muted-foreground">(Ty)</span>}
                  </div>
                  {u.email && (
                    <div className="truncate text-[12.5px] text-muted-foreground">{u.email}</div>
                  )}
                </div>

                <Select value={u.role} onValueChange={(v) => changeRole(u, v as UserRole)}>
                  <SelectTrigger className="w-40 shrink-0">
                    <SelectValue>{(v: string) => ROLE_LABELS[v as UserRole]}</SelectValue>
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="admin">{ROLE_LABELS.admin}</SelectItem>
                    <SelectItem value="user">{ROLE_LABELS.user}</SelectItem>
                  </SelectContent>
                </Select>

                <Button
                  size="icon-xs"
                  variant="ghost"
                  aria-label={`Zmień hasło ${u.username}`}
                  onClick={() => setResettingId(u.id)}
                >
                  <KeyRound className="size-3.5 text-muted-foreground" />
                </Button>
                <Button
                  size="icon-xs"
                  variant="ghost"
                  aria-label={`Usuń ${u.username}`}
                  disabled={u.id === me?.id}
                  onClick={() => setDeletingId(u.id)}
                >
                  <Trash2 className="size-3.5 text-muted-foreground" />
                </Button>
              </li>
            ))}
          </ul>
        ) : (
          <Hint>Brak użytkowników.</Hint>
        )}
      </div>

      {deletingUser && (
        <ConfirmDialog
          open
          title="Usuń użytkownika"
          description={`Na pewno usunąć konto „${deletingUser.username}”? Tej operacji nie można cofnąć.`}
          confirmLabel="Usuń"
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
      setError("Nazwa użytkownika, hasło i wyświetlana nazwa są wymagane.")
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
      setError(err instanceof ApiError ? err.message : "Nie udało się dodać użytkownika.")
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
      <DialogTrigger render={<Button>+ Dodaj użytkownika</Button>} />
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Dodaj użytkownika</DialogTitle>
        </DialogHeader>

        <div className="flex flex-col gap-2">
          <Label htmlFor="user-username">Nazwa użytkownika (login)</Label>
          <Input id="user-username" value={username} onChange={(e) => setUsername(e.target.value)} />

          <Label htmlFor="user-display-name">Wyświetlana nazwa</Label>
          <Input
            id="user-display-name"
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
          />

          <Label htmlFor="user-email">Email (opcjonalnie)</Label>
          <Input id="user-email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} />

          <Label htmlFor="user-password">Hasło</Label>
          <Input
            id="user-password"
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />

          <Label>Rola</Label>
          <Select value={role} onValueChange={(v) => setRole(v as UserRole)}>
            <SelectTrigger className="w-full">
              <SelectValue>{(v: string) => ROLE_LABELS[v as UserRole]}</SelectValue>
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="admin">{ROLE_LABELS.admin}</SelectItem>
              <SelectItem value="user">{ROLE_LABELS.user}</SelectItem>
            </SelectContent>
          </Select>

          <FormError>{error}</FormError>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => setOpen(false)}>
            Anuluj
          </Button>
          <Button onClick={add}>Dodaj</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function ResetPasswordDialog({ user, onClose }: { user: ManagedUser; onClose: () => void }) {
  const [password, setPassword] = useState("")
  const [error, setError] = useState("")
  const [submitting, setSubmitting] = useState(false)

  async function submit() {
    if (!password) {
      setError("Podaj nowe hasło.")
      return
    }
    setSubmitting(true)
    setError("")
    try {
      await api.updateUser(user.id, { password })
      onClose()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Nie udało się zmienić hasła.")
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Dialog open onOpenChange={(next) => !next && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Zmień hasło — {user.username}</DialogTitle>
        </DialogHeader>
        <div className="flex flex-col gap-2">
          <Label htmlFor="reset-password">Nowe hasło</Label>
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
            Anuluj
          </Button>
          <Button onClick={submit} disabled={submitting}>
            {submitting ? "Zapisywanie…" : "Zapisz"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export { UsersView }
