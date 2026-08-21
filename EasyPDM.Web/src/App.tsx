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
import { PendingTicketBanner } from "@/features/items/pending-ticket-banner"
import { useItems } from "@/features/items/use-items"
import { MaterialsView } from "@/features/materials/materials-view"
import { ManufacturersView } from "@/features/manufacturers/manufacturers-view"
import { AppearanceSettingsView } from "@/features/settings/appearance-settings-view"
import { AuthorSettingsView } from "@/features/settings/author-settings-view"
import { LanguageSettingsView } from "@/features/settings/language-settings-view"
import { LogsView } from "@/features/settings/logs-view"
import { MyProjectsView } from "@/features/settings/my-projects-view"
import { SettingsSidebar } from "@/features/settings/settings-sidebar"
import { NamingSettingsView } from "@/features/settings/naming-settings-view"
import { StorageSettingsView } from "@/features/settings/storage-settings-view"
import { TagFilterSelect } from "@/features/tags/tag-filter-select"
import { useTags } from "@/features/tags/use-tags"
import { ProjectTreeView } from "@/features/tree/project-tree-view"
import { UsersView } from "@/features/users/users-view"
import { WelcomeView } from "@/features/welcome/welcome-view"
import { LanguageSelect } from "@/i18n/language-select"
import { useLanguage } from "@/i18n/use-language"
import { APP_VERSION } from "@/version"

type View = "welcome" | "projects" | "database" | "materials" | "manufacturers" | "settings"

function App() {
  const { user, loading: authLoading, refetch: refetchAuth, logout } = useAuth()
  const { t } = useLanguage()
  const [view, setView] = useState<View>("welcome")
  const [settingsSection, setSettingsSection] = useState("users")
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

  // "Użytkownicy"/"Magazyn plików" są dostępne tylko dla admina, ale każdy może wejść w
  // Ustawienia (Wygląd/Język) — gdyby zwykły użytkownik trafił tu z domyślnym
  // settingsSection="users", zamiast tego lądował na ekranie "Brak uprawnień".
  useEffect(() => {
    if (
      !isAdmin &&
      (settingsSection === "users" ||
        settingsSection === "storage" ||
        settingsSection === "logs" ||
        settingsSection === "naming")
    ) {
      setSettingsSection("appearance")
    }
  }, [isAdmin, settingsSection])

  async function refreshAfterMutation() {
    await refetchProjects()
    await refetchItems()
    setTreeRefreshKey((k) => k + 1)
  }

  if (authLoading) return null
  if (!user) return <LoginView onLoggedIn={refetchAuth} />

  return (
    <div className="flex h-screen overflow-hidden bg-background text-foreground">
      <AppSidebar activeId={view} onSelect={(id) => setView(id as View)} />

      {view === "settings" && (
        <SettingsSidebar
          activeId={settingsSection}
          isAdmin={isAdmin}
          myLabel={user.displayName}
          onSelect={setSettingsSection}
        />
      )}

      <div className="min-w-0 flex-1 overflow-y-auto">
        <header className="sticky top-0 z-10 border-b bg-background px-8 py-5">
          <div className="mb-3.5 flex items-center justify-between">
            <div className="flex items-baseline gap-1.5">
              <button
                type="button"
                onClick={() => setView("welcome")}
                className="text-xl font-semibold tracking-tight hover:text-primary"
              >
                easyPDM
              </button>
              <span className="text-[12.5px] text-muted-foreground/60">v{APP_VERSION}</span>
            </div>
            <div className="flex items-center gap-2.5 text-[13px] text-muted-foreground">
              <span>
                {user.displayName} ({t(user.role === "admin" ? "app.role.admin" : "app.role.user")})
              </span>
              <LanguageSelect className="w-24" />
              <Button size="sm" variant="outline" onClick={logout}>
                {t("app.logout")}
              </Button>
            </div>
          </div>

          <PendingTicketBanner />

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
                  trigger={<Button>{t("addNode.triggerButton")}</Button>}
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
                {t("common.refresh")}
              </Button>
            </div>
          )}

          {view === "database" && (
            <div className="flex flex-wrap gap-2.5">
              <Input
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder={t("app.searchPlaceholder")}
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
                {t("common.refresh")}
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
              <Hint>{t("app.selectProjectHint")}</Hint>
            ))}

          {view === "database" && (
            <ItemList
              items={items}
              loading={loading}
              error={error}
              projects={projects}
              search={debouncedSearch}
              tag={tag}
              onSearchChange={setSearch}
              onTagChange={setTag}
              isAdmin={isAdmin}
              onItemsRefetch={refetchItems}
              onTagsRefetch={refetchTags}
              onProjectsRefetch={refetchProjects}
              onNavigateToProject={(id) => {
                setProjectId(id)
                setView("projects")
              }}
            />
          )}

          {view === "materials" && <MaterialsView />}

          {view === "manufacturers" && <ManufacturersView />}

          {view === "settings" &&
            settingsSection === "users" &&
            (isAdmin ? <UsersView /> : <Hint>{t("settings.noPermission")}</Hint>)}

          {view === "settings" &&
            settingsSection === "storage" &&
            (isAdmin ? <StorageSettingsView /> : <Hint>{t("settings.noPermission")}</Hint>)}

          {view === "settings" &&
            settingsSection === "logs" &&
            (isAdmin ? <LogsView /> : <Hint>{t("settings.noPermission")}</Hint>)}

          {view === "settings" &&
            settingsSection === "naming" &&
            (isAdmin ? <NamingSettingsView /> : <Hint>{t("settings.noPermission")}</Hint>)}

          {view === "settings" && settingsSection === "appearance" && <AppearanceSettingsView />}

          {view === "settings" && settingsSection === "language" && <LanguageSettingsView />}

          {view === "settings" && settingsSection === "author" && <AuthorSettingsView />}

          {view === "settings" && settingsSection === "myProjects" && (
            <MyProjectsView displayName={user.displayName} projects={projects} />
          )}
        </main>
      </div>
    </div>
  )
}

export default App
