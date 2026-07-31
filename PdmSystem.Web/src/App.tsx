import { useState } from "react"

import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { useDebouncedValue } from "@/hooks/use-debounced-value"
import { NewProjectDialog } from "@/features/projects/new-project-dialog"
import { ProjectSelect } from "@/features/projects/project-select"
import { useProjects } from "@/features/projects/use-projects"
import { AddNodeDialog } from "@/features/items/add-node-dialog"
import { ItemList } from "@/features/items/item-list"
import { useItems } from "@/features/items/use-items"
import { TagFilterSelect } from "@/features/tags/tag-filter-select"
import { useTags } from "@/features/tags/use-tags"
import { ProjectTreeView } from "@/features/tree/project-tree-view"

function App() {
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
  } = useItems({ search: debouncedSearch, tag, projectId })

  const selectedProject = projects.find((p) => p.id === projectId) ?? null

  async function refreshAfterMutation() {
    await refetchProjects()
    await refetchItems()
    setTreeRefreshKey((k) => k + 1)
  }

  return (
    <div className="min-h-screen bg-background text-foreground">
      <header className="sticky top-0 z-10 border-b bg-background px-8 py-5">
        <h1 className="mb-3.5 text-xl font-semibold tracking-tight">PdmSystem</h1>
        <div className="flex flex-wrap gap-2.5">
          <ProjectSelect projects={projects} value={projectId} onChange={setProjectId} />
          <NewProjectDialog
            onCreated={async (project) => {
              setProjectId(project.id)
              await refreshAfterMutation()
            }}
          />
          <Input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Szukaj po nazwie lub właściwościach…"
            className="min-w-52 flex-1"
          />
          <TagFilterSelect tags={tags} value={tag} onChange={setTag} />
          {selectedProject ? (
            <AddNodeDialog
              trigger={<Button>+ Element</Button>}
              projectId={selectedProject.id}
              parentId={null}
              existingItems={items}
              onCreated={refreshAfterMutation}
            />
          ) : (
            <Button disabled title="Najpierw wybierz projekt z listy.">
              + Element
            </Button>
          )}
          <Button
            variant="outline"
            onClick={() => {
              refetchTags()
              refreshAfterMutation()
            }}
          >
            Odśwież
          </Button>
        </div>
      </header>

      <main className={selectedProject ? "px-8 py-6" : "mx-auto max-w-3xl px-8 py-6"}>
        {selectedProject ? (
          <ProjectTreeView
            key={`${selectedProject.id}-${treeRefreshKey}`}
            projectId={selectedProject.id}
            projectName={selectedProject.name}
            onTagsRefetch={refetchTags}
          />
        ) : (
          <ItemList
            items={items}
            loading={loading}
            error={error}
            projects={projects}
            onItemsRefetch={refetchItems}
            onTagsRefetch={refetchTags}
          />
        )}
      </main>
    </div>
  )
}

export default App
