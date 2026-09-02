import { type MouseEvent, useState } from "react"

// Szerokość panelu przeciąganego uchwytem (zob. ResizeHandle) — zapamiętywana w
// localStorage pod danym kluczem, żeby użytkownik ustawił ją raz i miał tak samo przy
// kolejnych wizytach. Nasłuchy przeciągania wieszane na `document` (nie na samym
// uchwycie) — bez tego szybki ruch myszką poza wąski pasek uchwytu przerwałby
// przeciąganie w połowie ruchu.
function useResizableWidth(storageKey: string, min: number, max: number, defaultWidth: number) {
  const [width, setWidth] = useState(() => {
    try {
      const saved = Number(localStorage.getItem(storageKey))
      if (saved >= min && saved <= max) return saved
    } catch {
      // np. tryb prywatny bez dostępu do localStorage — po prostu użyj domyślnej
    }
    return defaultWidth
  })

  function startResize(e: MouseEvent) {
    e.preventDefault()
    const startX = e.clientX
    const startWidth = width
    let latestWidth = startWidth

    function onMouseMove(moveEvent: globalThis.MouseEvent) {
      latestWidth = Math.min(max, Math.max(min, startWidth + moveEvent.clientX - startX))
      setWidth(latestWidth)
    }
    function onMouseUp() {
      document.removeEventListener("mousemove", onMouseMove)
      document.removeEventListener("mouseup", onMouseUp)
      window.removeEventListener("blur", onMouseUp)
      try {
        localStorage.setItem(storageKey, String(latestWidth))
      } catch {
        // tryb prywatny itp. — szerokość po prostu nie zostanie zapamiętana na później
      }
    }
    document.addEventListener("mousemove", onMouseMove)
    document.addEventListener("mouseup", onMouseUp)
    // Puszczenie przycisku myszy POZA oknem przeglądarki (np. przeciągnięcie na pasek
    // zadań/inną aplikację) nie generuje "mouseup" w tym dokumencie w ogóle — bez tego
    // nasłuchy zostałyby podpięte na zawsze, a kolejne przeciągnięcie dokładałoby drugi
    // komplet na wierzch. "blur" (okno traci fokus) to najbliższy niezawodny sygnał
    // "przeciąganie na pewno się skończyło" dostępny bez Pointer Capture API.
    window.addEventListener("blur", onMouseUp)
  }

  return { width, startResize }
}

export { useResizableWidth }
