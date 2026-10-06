import { useCallback, useEffect, useState } from "react"

import { api } from "@/api/client"
import type { NotificationEntry } from "@/api/types"
import { TRANSFER_FINISHED_EVENT } from "@/features/transfer/transfer-finished-event"

// Odświeżanie co 30s (pierwszy tego typu polling w tym kodzie — nie ma websocketów) +
// natychmiastowe odświeżenie na żądanie (np. przy otwarciu panelu dzwonka).
const POLL_INTERVAL_MS = 30_000

export function useNotifications() {
  const [unreadCount, setUnreadCount] = useState(0)
  const [items, setItems] = useState<NotificationEntry[]>([])

  const refetch = useCallback(async () => {
    try {
      const result = await api.getNotifications()
      setUnreadCount(result.unreadCount)
      setItems(result.items)
    } catch {
      // Cichy błąd — dzwonek nie ma czym pokazać awarii, zostaje po prostu przy
      // ostatnim znanym stanie do kolejnego udanego odświeżenia.
    }
  }, [])

  useEffect(() => {
    refetch()
    const id = setInterval(refetch, POLL_INTERVAL_MS)
    return () => clearInterval(id)
  }, [refetch])

  // Koniec biegu makra zostawia raport w powiadomieniach i jest jedynym momentem, w którym
  // 30 s to zdecydowanie za długo — użytkownik patrzy wtedy prosto na listę plików, która
  // właśnie się domknęła.
  useEffect(() => {
    window.addEventListener(TRANSFER_FINISHED_EVENT, refetch)
    return () => window.removeEventListener(TRANSFER_FINISHED_EVENT, refetch)
  }, [refetch])

  async function markRead(id: string) {
    setItems((prev) => prev.map((n) => (n.id === id ? { ...n, readAt: n.readAt ?? new Date().toISOString() } : n)))
    setUnreadCount((prev) => Math.max(0, prev - 1))
    try {
      await api.markNotificationRead(id)
    } catch {
      await refetch()
    }
  }

  async function markAllRead() {
    const now = new Date().toISOString()
    setItems((prev) => prev.map((n) => ({ ...n, readAt: n.readAt ?? now })))
    setUnreadCount(0)
    try {
      await api.markAllNotificationsRead()
    } catch {
      await refetch()
    }
  }

  async function deleteNotification(id: string) {
    const target = items.find((n) => n.id === id)
    setItems((prev) => prev.filter((n) => n.id !== id))
    if (target && !target.readAt) setUnreadCount((prev) => Math.max(0, prev - 1))
    try {
      await api.deleteNotification(id)
    } catch {
      await refetch()
    }
  }

  return { unreadCount, items, refetch, markRead, markAllRead, deleteNotification }
}
