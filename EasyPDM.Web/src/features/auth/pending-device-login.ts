import { useSyncExternalStore } from "react"

// A program on someone's computer -- the EasyPDM companion, later the FreeCAD and SolidWorks
// add-ins -- asks to sign in by opening "/?deviceLogin=<code>" (DeviceLoginEndpoints.cs). The
// code is read once, when this module is first imported, like the macro tickets in
// pending-create-ticket.ts, so it survives the login screen when the person is not logged in yet.

let current: string | null = (() => {
  const params = new URLSearchParams(window.location.search)
  const code = params.get("deviceLogin")
  if (!code) return null
  params.delete("deviceLogin")
  const rest = params.toString()
  window.history.replaceState(null, "", window.location.pathname + (rest ? `?${rest}` : ""))
  return code
})()
const listeners = new Set<() => void>()

function clearPendingDeviceLogin() {
  if (current === null) return
  current = null
  listeners.forEach((listener) => listener())
}

function usePendingDeviceLogin(): string | null {
  return useSyncExternalStore(
    (listener) => {
      listeners.add(listener)
      return () => listeners.delete(listener)
    },
    () => current
  )
}

export { clearPendingDeviceLogin, usePendingDeviceLogin }
