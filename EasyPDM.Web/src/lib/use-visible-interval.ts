import { useEffect } from "react"

// Odpytuje co `intervalMs`, ale tylko gdy karta jest widoczna; po powrocie do karty odświeża od
// razu. Zakładka zostawiona w tle na cały dzień nie wysyła wtedy kilku tysięcy zbędnych
// zapytań, a po powrocie i tak pokazuje aktualny stan.
//
// Nie dla prośby o formularz z makra CAD (use-cad-requests): ta musi działać właśnie wtedy, gdy
// karta jest w tle, bo użytkownik patrzy na CAD-a.
export function useVisibleInterval(callback: () => void, intervalMs: number) {
  useEffect(() => {
    callback()
    const id = setInterval(() => {
      if (!document.hidden) callback()
    }, intervalMs)
    function onVisibilityChange() {
      if (!document.hidden) callback()
    }
    document.addEventListener("visibilitychange", onVisibilityChange)
    return () => {
      clearInterval(id)
      document.removeEventListener("visibilitychange", onVisibilityChange)
    }
  }, [callback, intervalMs])
}
