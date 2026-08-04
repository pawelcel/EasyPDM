import type { ReactNode } from "react"

import { cn } from "@/lib/utils"

function FormError({ className, children }: { className?: string; children?: ReactNode }) {
  if (!children) return null
  return <p className={cn("text-sm text-destructive", className)}>{children}</p>
}

export { FormError }
