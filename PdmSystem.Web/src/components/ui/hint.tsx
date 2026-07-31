import type { ReactNode } from "react"

function Hint({ children }: { children: ReactNode }) {
  return <p className="text-muted-foreground">{children}</p>
}

export { Hint }
