import { Database, FolderKanban } from "lucide-react"

import { Card, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"

function WelcomeView({ onNavigate }: { onNavigate: (view: "projects" | "database") => void }) {
  return (
    <div className="mx-auto flex max-w-2xl flex-col gap-6 py-10 text-center">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Witaj w PdmSystem</h1>
        <p className="mt-1.5 text-sm text-muted-foreground">
          Wybierz, od czego chcesz zacząć.
        </p>
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        <button type="button" className="text-left" onClick={() => onNavigate("projects")}>
          <Card className="h-full transition-colors hover:bg-accent">
            <CardHeader>
              <FolderKanban className="mb-1 size-5 text-primary" />
              <CardTitle>Projekty</CardTitle>
              <CardDescription>Przeglądaj strukturę i elementy konkretnego projektu.</CardDescription>
            </CardHeader>
          </Card>
        </button>

        <button type="button" className="text-left" onClick={() => onNavigate("database")}>
          <Card className="h-full transition-colors hover:bg-accent">
            <CardHeader>
              <Database className="mb-1 size-5 text-primary" />
              <CardTitle>Cała baza</CardTitle>
              <CardDescription>Przeszukaj wszystkie zapisane elementy, niezależnie od projektu.</CardDescription>
            </CardHeader>
          </Card>
        </button>
      </div>
    </div>
  )
}

export { WelcomeView }
