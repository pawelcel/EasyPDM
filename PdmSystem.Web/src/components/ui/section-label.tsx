import type { ReactNode } from "react"

function SectionLabel({ children }: { children: ReactNode }) {
  return (
    <h4 className="mt-3.5 mb-1 text-[11.5px] font-medium tracking-wide text-muted-foreground uppercase">
      {children}
    </h4>
  )
}

export { SectionLabel }
