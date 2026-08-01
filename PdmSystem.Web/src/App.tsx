import { useEffect, useState } from "react"

import { AppSidebar } from "@/components/app-sidebar"
import { Button } from "@/components/ui/button"
import { Hint } from "@/components/ui/hint"
import { Input } from "@/components/ui/input"
import { useDebouncedValue } from "@/hooks/use-debounced-value"
import { useAuth } from "@/features/auth/use-auth"
import { LoginView } from "@/features/auth/login-view"
import { NewProjectDialog } from "@/features/projects/new-project-dialog"
import { ProjectSelect } from "@/features/projects/project-select"
import { useProjects } from "@/features/projects/use-projects"
import { AddNodeDialog } from "@/features/items/add-node-dialog"
import { ItemList } from "@/features/items/item-list"
import { useItems } from "@/features/items/use-items"
import { MaterialsView } from "@/features/materials/materials-view"
import { TagFilterSelect } from "@/features/tags/tag-filter-select"
import { useTags } from "@/features/tags/use-tags"
import { ProjectTreeView } from "@/features/tree/project-tree-view"
import { UsersView } from "@/features/users/users-view"
import { WelcomeView } from "@/features/welcome/welcome-view"

type View = "welcome" | "projects" | "database" | "materials" | "users"

function App() {
  const { user, loading: authLoading, refetch: refetchAuth, logout } = useAuth()
  const [view, setView] = useState<View>("welcome")
  const [projectId, setProjectId] = useState("")
  const [tag, setTag] = useState("")
  const [search, setSearch] = useState("")
  const [treeRefreshKey, setTreeRefreshKey] = useState(0)
  const debouncedSearch = useDebouncedValue(search, 300)

  const { projects, refetch: refetchProjects } = useProjects()
  const { tags, refetch: refetchTags } = useTags()
  const {
    items,
    loading,
    error,
    refetch: refetchItems,
  } = useItems({ search: debouncedSearch, tag })

  const selectedProject = projects.find((p) => p.id === projectId) ?? null
  const isAdmin = user?.role === "admin"

  // useProjects/useTags/useItems montują się (i odpalają swój jedyny fetch) razem z App,
  // czyli JESZCZE PRZED zalogowaniem — ten pierwszy fetch dostaje 401 i nigdy się sam nie
  // powtarza. Dopiero to odświeża dane naprawdę PO udanym logowaniu (user zmienia się z
  // null na realnego użytkownika).
  useEffect(() => {
    if (!user) return
    refetchProjects()
    refetchTags()
    refetchItems()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [user])

  async function refreshAfterMutation() {
    await refetchProjects()
    await refetchItems()
    setTreeRefreshKey((k) => k + 1)
  }

  if (authLoading) return null
  if (!user) return <LoginView onLoggedIn={refetchAuth} />

  return (
    <div className="flex min-h-screen bg-background text-foreground">
      <AppSidebar
        activeId={view}
        onSelect={(id) => setView(id as View)}
        showUsers={isAdmin}
      />

      <div className="min-w-0 flex-1">
        <header className="sticky top-0 z-10 border-b bg-background px-8 py-5">
          <div className="mb-3.5 flex items-center justify-between">
            <button
              type="button"
              onClick={() => setView("welcome")}
              className="text-xl font-semibold tracking-tight hover:text-primary"
            >
              PdmSystem
            </button>
            <div className="flex items-center gap-2.5 text-[13px] text-muted-foreground">
              <span>
                {user.displayName} ({user.role === "admin" ? "administrator" : "użytkownik"})
              </span>
              <Button size="sm" variant="outline" onClick={logout}>
                Wyloguj
              </Button>
            </div>
          </div>

          {view === "projects" && (
            <div className="flex flex-wrap gap-2.5">
              <ProjectSelect projects={projects} value={projectId} onChange={setProjectId} />
              {isAdmin && (
                <NewProjectDialog
                  onCreated={async (project) => {
                    setProjectId(project.id)
                    await refreshAfterMutation()
                  }}
                />
              )}
              {selectedProject && (
                <AddNodeDialog
                  trigger={<Button>+ Element</Button>}
                  projectId={selectedProject.id}
                  parentId={null}
                  parentType={null}
                  onCreated={refreshAfterMutation}
                />
              )}
              <Button
                variant="outline"
                onClick={() => {
                  refetchProjects()
                  setTreeRefreshKey((k) => k + 1)
                }}
              >
                Odśwież
              </Button>
            </div>
          )}

          {view === "database" && (
            <div className="flex flex-wrap gap-2.5">
              <Input
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Szukaj po nazwie lub właściwościach…"
                className="min-w-52 flex-1"
              />
              <TagFilterSelect tags={tags} value={tag} onChange={setTag} />
              <Button
                variant="outline"
                onClick={() => {
                  refetchTags()
                  refetchItems()
                }}
              >
                Odśwież
              </Button>
            </div>
          )}
        </header>

        <main className="px-8 py-6">
          {view === "welcome" && <WelcomeView onNavigate={setView} />}

          {view === "projects" &&
            (selectedProject ? (
              <ProjectTreeView
                key={`${selectedProject.id}-${treeRefreshKey}`}
                project={selectedProject}
                isAdmin={isAdmin}
                onTagsRefetch={refetchTags}
                onProjectUpdated={refetchProjects}
                onProjectDeleted={async () => {
                  setProjectId("")
                  await refetchProjects()
                }}
              />
            ) : (
              <Hint>Wybierz projekt z listy powyżej albo utwórz nowy.</Hint>
            ))}

          {view === "database" && (
            <ItemList
              items={items}
              loading={loading}
              error={error}
              projects={projects}
              onItemsRefetch={refetchItems}
              onTagsRefetch={refetchTags}
            />
          )}

          {view === "materials" && <MaterialsView />}

          {view === "users" && (isAdmin ? <UsersView /> : <Hint>Brak uprawnień.</Hint>)}
        </main>
      </div>
    </div>
  )
}

export default App
