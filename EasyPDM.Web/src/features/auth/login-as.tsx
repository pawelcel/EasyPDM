import { useEffect, useMemo, useState, type SVGProps } from "react"
import { UserRound } from "lucide-react"

import { api, ApiError } from "@/api/client"
import type { CurrentUser, LoginAsAccess, LoginAsLogLine, LoginAsPerson } from "@/api/types"
import { Button } from "@/components/ui/button"
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { FormError } from "@/components/ui/form-error"
import { Input } from "@/components/ui/input"
import { useLanguage } from "@/i18n/use-language"
import type { TranslationKey } from "@/i18n/translations"
import { cn } from "@/lib/utils"

// Log in as (LoginAsEndpoints.cs): an administrator works as another user, to see what they see.
// The top bar shows one person icon to an administrator; while switched it becomes one amber
// button, "Back to <administrator>", and the name beside it is marked amber. Switching remembers
// where the administrator was (sessionStorage), and Back returns there.
const RETURN_KEY = "pdm_login_as_return"

function rememberReturn() {
  try {
    sessionStorage.setItem(RETURN_KEY, window.location.pathname + window.location.search + window.location.hash)
  } catch {
    // storage blocked: Back returns to the start page
  }
}

// Only a path on this site: never "//host" or a full address someone put in storage.
function takeReturn(): string {
  let target = "/"
  try {
    const saved = sessionStorage.getItem(RETURN_KEY)
    sessionStorage.removeItem(RETURN_KEY)
    if (saved && saved.startsWith("/") && !saved.startsWith("//") && !saved.startsWith("/\\")) target = saved
  } catch {
    // storage blocked: the start page
  }
  return target
}

function errorText(err: unknown, fallback: string): string {
  return err instanceof ApiError && err.message ? err.message : fallback
}

function formatWhen(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    year: "numeric",
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
    second: "2-digit",
  })
}

const END_KEYS: Record<string, TranslationKey> = {
  back: "loginAs.end.back",
  logged_out: "loginAs.end.loggedOut",
  expired: "loginAs.end.expired",
}

const KIND_KEYS: Record<LoginAsLogLine["kind"], TranslationKey> = {
  start: "loginAs.kind.start",
  change: "loginAs.kind.change",
  end: "loginAs.kind.end",
}

// The header's entry or way back. Nothing for anyone who is neither an administrator nor switched.
function LoginAsControl({ user }: { user: CurrentUser }) {
  if (user.loginAs) return <LoginAsBack adminName={user.loginAs.adminName} />
  if (user.role !== "admin") return null
  return <LoginAsButton />
}

// A person with an arrow back, on a 24-unit grid in the line style of lucide icons.
function ReturnPersonIcon(props: SVGProps<SVGSVGElement>) {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2.2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden
      {...props}
    >
      <circle cx="10" cy="7.5" r="4" />
      <path d="M2.5 21v-.5a7.5 7.5 0 0 1 9-7.3" />
      <path d="M22 17.5h-7.5" />
      <path d="M17.5 14.5l-3 3 3 3" />
    </svg>
  )
}

function LoginAsBack({ adminName }: { adminName: string }) {
  const { t } = useLanguage()
  const [busy, setBusy] = useState(false)

  async function back() {
    setBusy(true)
    try {
      await api.loginAsBack()
    } catch {
      // The session may already be over (logged out elsewhere, or the administrator lost the
      // role): the reload below then shows the login page, which is the honest answer.
    }
    const target = takeReturn()
    const here = window.location.pathname + window.location.search + window.location.hash
    if (target === here) window.location.reload()
    else window.location.assign(target)
  }

  const label = t("loginAs.back", { admin: adminName })
  return (
    <Button
      type="button"
      size="icon-sm"
      onClick={back}
      disabled={busy}
      aria-label={label}
      title={label}
      className="bg-amber-400 text-[#1c1503] hover:bg-amber-300 focus-visible:border-amber-600 focus-visible:ring-amber-500/50"
    >
      <ReturnPersonIcon />
    </Button>
  )
}

function LoginAsButton() {
  const { t } = useLanguage()
  const [open, setOpen] = useState(false)

  return (
    <>
      <Button
        type="button"
        variant="ghost"
        size="icon-sm"
        onClick={() => setOpen(true)}
        aria-label={t("loginAs.open")}
        title={t("loginAs.open")}
      >
        <UserRound />
      </Button>
      {open && <LoginAsDialog onClose={() => setOpen(false)} />}
    </>
  )
}

function LoginAsDialog({ onClose }: { onClose: () => void }) {
  const { t } = useLanguage()
  const [people, setPeople] = useState<LoginAsPerson[] | null>(null)
  const [search, setSearch] = useState("")
  const [picked, setPicked] = useState<LoginAsPerson | null>(null)
  const [access, setAccess] = useState<LoginAsAccess | null>(null)
  const [showLog, setShowLog] = useState(false)
  const [error, setError] = useState("")
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    api
      .getLoginAsPeople()
      .then(setPeople)
      .catch((err) => setError(errorText(err, t("loginAs.loadFailed"))))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    setAccess(null)
    if (!picked) return
    let current = true
    api
      .getLoginAsAccess(picked.id)
      .then((a) => current && setAccess(a))
      .catch((err) => current && setError(errorText(err, t("loginAs.loadFailed"))))
    return () => {
      current = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [picked])

  const shown = useMemo(() => {
    const needle = search.trim().toLowerCase()
    if (!people) return []
    if (!needle) return people
    return people.filter(
      (p) => p.displayName.toLowerCase().includes(needle) || p.username.toLowerCase().includes(needle)
    )
  }, [people, search])

  async function switchTo() {
    if (!picked) return
    setBusy(true)
    setError("")
    try {
      rememberReturn()
      await api.loginAs(picked.id)
      window.location.assign("/")
    } catch (err) {
      setError(errorText(err, t("loginAs.switchFailed")))
      setBusy(false)
    }
  }

  return (
    <Dialog open onOpenChange={(next) => !next && onClose()}>
      <DialogContent className={cn(showLog ? "sm:max-w-3xl" : "sm:max-w-md")}>
        <DialogHeader>
          <DialogTitle>{showLog ? t("loginAs.logTitle") : t("loginAs.title")}</DialogTitle>
        </DialogHeader>

        {showLog ? (
          <LoginAsLog />
        ) : (
          <div className="flex flex-col gap-2">
            <Input
              autoFocus
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder={t("loginAs.search")}
              aria-label={t("loginAs.search")}
              className="h-8"
            />
            <ul
              role="listbox"
              aria-label={t("loginAs.title")}
              className="flex max-h-60 flex-col gap-0.5 overflow-y-auto rounded-lg border p-1"
            >
              {people === null && !error && <li className="px-2 py-1.5 text-muted-foreground">{t("common.loading")}</li>}
              {people !== null && shown.length === 0 && (
                <li className="px-2 py-1.5 text-muted-foreground">{t("loginAs.noPeople")}</li>
              )}
              {shown.map((p) => (
                <li key={p.id} role="option" aria-selected={picked?.id === p.id}>
                  <button
                    type="button"
                    onClick={() => setPicked(p)}
                    className={cn(
                      "flex w-full items-baseline gap-2 rounded-md px-2 py-1 text-left hover:bg-muted",
                      picked?.id === p.id && "bg-primary/10 text-foreground"
                    )}
                  >
                    <span className="truncate">{p.displayName}</span>
                    <span className="truncate text-[11.5px] text-muted-foreground">{p.username}</span>
                  </button>
                </li>
              ))}
            </ul>
            <p className="min-h-5 text-[12px] text-muted-foreground">
              {picked ? (access ? <AccessLine access={access} /> : t("common.loading")) : t("loginAs.pick")}
            </p>
          </div>
        )}

        <FormError>{error}</FormError>

        <DialogFooter className="items-center sm:justify-between">
          <button
            type="button"
            onClick={() => setShowLog((v) => !v)}
            className="text-[12px] text-muted-foreground underline-offset-2 hover:text-foreground hover:underline"
          >
            {showLog ? t("loginAs.logHide") : t("loginAs.log")}
          </button>
          <div className="flex gap-2">
            <Button variant="outline" onClick={onClose}>
              {showLog ? t("common.close") : t("common.cancel")}
            </Button>
            {!showLog && (
              <Button onClick={switchTo} disabled={!picked || busy}>
                {t("loginAs.switch")}
              </Button>
            )}
          </div>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function AccessLine({ access }: { access: LoginAsAccess }) {
  const { t } = useLanguage()
  if (access.projects.length === 0) return <>{t("loginAs.access", { projects: t("loginAs.noProjects") })}</>
  const named = access.projects.slice(0, 5).join(", ")
  const more = access.projectCount - Math.min(access.projects.length, 5)
  const projects = more > 0 ? t("loginAs.moreProjects", { named, more }) : named
  return <>{t("loginAs.access", { projects })}</>
}

function LoginAsLog() {
  const { t } = useLanguage()
  const [lines, setLines] = useState<LoginAsLogLine[] | null>(null)
  const [error, setError] = useState("")

  useEffect(() => {
    api
      .getLoginAsLog()
      .then(setLines)
      .catch((err) => setError(errorText(err, t("loginAs.logFailed"))))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  if (error) return <FormError>{error}</FormError>
  if (lines === null) return <p className="text-muted-foreground">{t("common.loading")}</p>
  if (lines.length === 0) return <p className="text-muted-foreground">{t("loginAs.logEmpty")}</p>

  return (
    <div className="max-h-96 overflow-y-auto rounded-lg border">
      <table className="w-full text-[12px]">
        <thead className="sticky top-0 bg-popover text-left text-muted-foreground">
          <tr>
            <th className="px-2 py-1 font-medium">{t("loginAs.logWhen")}</th>
            <th className="px-2 py-1 font-medium">{t("loginAs.logWho")}</th>
            <th className="px-2 py-1 font-medium">{t("loginAs.logWhat")}</th>
            <th className="px-2 py-1 font-medium">{t("loginAs.logFrom")}</th>
          </tr>
        </thead>
        <tbody>
          {lines.map((line) => (
            <tr key={line.id} className="border-t align-top">
              <td className="px-2 py-1 whitespace-nowrap">{formatWhen(line.at)}</td>
              <td className="px-2 py-1">{t("loginAs.logAs", { admin: line.adminName, name: line.targetName })}</td>
              <td className="px-2 py-1 break-all">
                {t(KIND_KEYS[line.kind])}
                {line.detail && `: ${line.kind === "end" && END_KEYS[line.detail] ? t(END_KEYS[line.detail]) : line.detail}`}
              </td>
              <td className="px-2 py-1 whitespace-nowrap text-muted-foreground">{line.clientAddress}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

export { LoginAsControl }
