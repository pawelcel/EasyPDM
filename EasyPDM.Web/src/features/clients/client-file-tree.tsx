import { useEffect, useRef, useState, type ReactElement } from "react"
import {
  ChevronDown,
  ChevronRight,
  Download,
  File as FileIcon,
  Folder,
  FolderPlus,
  Pencil,
  Trash2,
  Upload,
} from "lucide-react"

import { api } from "@/api/client"
import type { ClientNode } from "@/api/types"
import { Button } from "@/components/ui/button"
import { ConfirmDialog } from "@/components/ui/confirm-dialog"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog"
import { FormError } from "@/components/ui/form-error"
import { Hint } from "@/components/ui/hint"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { useLanguage } from "@/i18n/use-language"

type TreeNode = ClientNode & { children: TreeNode[] }

function formatSize(size: number | null): string {
  if (size === null) return ""
  if (size < 1024) return `${size} B`
  if (size < 1024 * 1024) return `${(size / 1024).toFixed(1)} KB`
  return `${(size / (1024 * 1024)).toFixed(1)} MB`
}

function buildTree(nodes: ClientNode[]): TreeNode[] {
  const byId = new Map<string, TreeNode>()
  nodes.forEach((n) => byId.set(n.id, { ...n, children: [] }))
  const roots: TreeNode[] = []
  byId.forEach((node) => {
    const parent = node.parentId ? byId.get(node.parentId) : undefined
    if (parent) parent.children.push(node)
    else roots.push(node)
  })
  const sortFn = (a: TreeNode, b: TreeNode) =>
    a.nodeType !== b.nodeType ? (a.nodeType === "folder" ? -1 : 1) : a.name.localeCompare(b.name)
  const sortRecursive = (list: TreeNode[]) => {
    list.sort(sortFn)
    list.forEach((n) => sortRecursive(n.children))
  }
  sortRecursive(roots)
  return roots
}

function countDescendants(node: TreeNode): number {
  return node.children.reduce((sum, child) => sum + 1 + countDescendants(child), 0)
}

function ClientFileTree({ clientId }: { clientId: number }) {
  const { t } = useLanguage()
  const [nodes, setNodes] = useState<ClientNode[]>([])
  const [loading, setLoading] = useState(true)
  const [expanded, setExpanded] = useState<Set<string>>(new Set())
  const [error, setError] = useState("")
  const [deletingNode, setDeletingNode] = useState<TreeNode | null>(null)
  const [deletingPending, setDeletingPending] = useState(false)
  const fileInputRef = useRef<HTMLInputElement>(null)
  const uploadParentId = useRef<string | null>(null)

  async function refetch() {
    setLoading(true)
    try {
      setNodes(await api.getClientNodes(clientId))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    refetch()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [clientId])

  function toggle(id: string) {
    setExpanded((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  function triggerUpload(parentId: string | null) {
    uploadParentId.current = parentId
    fileInputRef.current?.click()
  }

  async function handleFileSelected(file: File) {
    setError("")
    try {
      await api.uploadClientFile(clientId, uploadParentId.current, file)
      await refetch()
    } catch {
      setError(t("client.uploadFailed"))
    }
  }

  async function confirmDelete() {
    if (!deletingNode) return
    setDeletingPending(true)
    setError("")
    try {
      await api.removeClientNode(clientId, deletingNode.id)
      setDeletingNode(null)
      await refetch()
    } catch {
      setError(t("client.deleteNodeFailed"))
    } finally {
      setDeletingPending(false)
    }
  }

  const tree = buildTree(nodes)

  return (
    <div>
      <input
        ref={fileInputRef}
        type="file"
        className="hidden"
        onChange={(e) => {
          const file = e.target.files?.[0]
          e.target.value = ""
          if (file) handleFileSelected(file)
        }}
      />

      <div className="mb-1 flex items-center justify-between">
        <div className="flex items-center gap-1">
          <NewFolderDialog
            trigger={
              <Button size="icon-xs" variant="ghost" aria-label={t("client.newFolderAria")}>
                <FolderPlus className="size-3.5" />
              </Button>
            }
            onCreate={async (name) => {
              await api.createClientFolder(clientId, null, name)
              await refetch()
            }}
          />
          <Button
            size="icon-xs"
            variant="ghost"
            aria-label={t("client.uploadFileAria")}
            onClick={() => triggerUpload(null)}
          >
            <Upload className="size-3.5" />
          </Button>
        </div>
      </div>

      <FormError>{error}</FormError>

      {!loading && tree.length === 0 ? (
        <Hint>{t("client.emptyTree")}</Hint>
      ) : (
        <ul className="flex flex-col gap-0.5">
          {tree.map((node) => (
            <TreeRow
              key={node.id}
              node={node}
              depth={0}
              expanded={expanded}
              onToggle={toggle}
              onNewFolder={async (parentId, name) => {
                await api.createClientFolder(clientId, parentId, name)
                await refetch()
              }}
              onUpload={triggerUpload}
              onRename={async (nodeId, name) => {
                await api.renameClientNode(clientId, nodeId, name)
                await refetch()
              }}
              onDelete={(node) => {
                setError("")
                setDeletingNode(node)
              }}
              onDownload={(nodeId) => window.open(api.clientNodeDownloadUrl(clientId, nodeId), "_blank")}
            />
          ))}
        </ul>
      )}

      {deletingNode && (
        <ConfirmDialog
          open
          title={t("client.deleteNodeAria")}
          description={
            deletingNode.nodeType === "folder"
              ? t("client.deleteFolderConfirmDescription", {
                  name: deletingNode.name,
                  count: countDescendants(deletingNode),
                })
              : t("client.deleteFileConfirmDescription", { name: deletingNode.name })
          }
          confirmLabel={t("common.delete")}
          variant="destructive"
          onConfirm={confirmDelete}
          onCancel={() => setDeletingNode(null)}
          pending={deletingPending}
          error={error}
        />
      )}
    </div>
  )
}

function TreeRow({
  node,
  depth,
  expanded,
  onToggle,
  onNewFolder,
  onUpload,
  onRename,
  onDelete,
  onDownload,
}: {
  node: TreeNode
  depth: number
  expanded: Set<string>
  onToggle: (id: string) => void
  onNewFolder: (parentId: string, name: string) => void | Promise<void>
  onUpload: (parentId: string) => void
  onRename: (nodeId: string, name: string) => void | Promise<void>
  onDelete: (node: TreeNode) => void
  onDownload: (nodeId: string) => void
}) {
  const { t } = useLanguage()
  const isFolder = node.nodeType === "folder"
  const isOpen = expanded.has(node.id)

  return (
    <li>
      <div
        className="group flex items-center gap-1 rounded-md px-1 py-1 text-sm hover:bg-accent"
        style={{ paddingLeft: `${depth * 16 + 4}px` }}
      >
        {isFolder ? (
          <button type="button" onClick={() => onToggle(node.id)} className="shrink-0 text-muted-foreground">
            {isOpen ? <ChevronDown className="size-3.5" /> : <ChevronRight className="size-3.5" />}
          </button>
        ) : (
          <span className="inline-block w-3.5 shrink-0" />
        )}

        {isFolder ? (
          <Folder className="size-3.5 shrink-0 text-muted-foreground" />
        ) : (
          <FileIcon className="size-3.5 shrink-0 text-muted-foreground" />
        )}

        <span className="flex-1 truncate">{node.name}</span>
        {!isFolder && (
          <span className="shrink-0 text-[12px] text-muted-foreground">{formatSize(node.fileSize)}</span>
        )}

        <div className="flex shrink-0 items-center gap-0.5 opacity-0 group-hover:opacity-100">
          {isFolder && (
            <>
              <NewFolderDialog
                trigger={
                  <Button size="icon-xs" variant="ghost" aria-label={t("client.newFolderAria")}>
                    <FolderPlus className="size-3.5" />
                  </Button>
                }
                onCreate={(name) => onNewFolder(node.id, name)}
              />
              <Button
                size="icon-xs"
                variant="ghost"
                aria-label={t("client.uploadFileAria")}
                onClick={() => onUpload(node.id)}
              >
                <Upload className="size-3.5" />
              </Button>
            </>
          )}
          {!isFolder && (
            <Button
              size="icon-xs"
              variant="ghost"
              aria-label={t("client.downloadAria")}
              onClick={() => onDownload(node.id)}
            >
              <Download className="size-3.5" />
            </Button>
          )}
          <RenameDialog
            trigger={
              <Button size="icon-xs" variant="ghost" aria-label={t("client.renameAria")}>
                <Pencil className="size-3.5" />
              </Button>
            }
            initialName={node.name}
            onRename={(name) => onRename(node.id, name)}
          />
          <Button size="icon-xs" variant="ghost" aria-label={t("client.deleteNodeAria")} onClick={() => onDelete(node)}>
            <Trash2 className="size-3.5" />
          </Button>
        </div>
      </div>

      {isFolder && isOpen && node.children.length > 0 && (
        <ul className="flex flex-col gap-0.5">
          {node.children.map((child) => (
            <TreeRow
              key={child.id}
              node={child}
              depth={depth + 1}
              expanded={expanded}
              onToggle={onToggle}
              onNewFolder={onNewFolder}
              onUpload={onUpload}
              onRename={onRename}
              onDelete={onDelete}
              onDownload={onDownload}
            />
          ))}
        </ul>
      )}
      {isFolder && isOpen && node.children.length === 0 && (
        <div className="text-[12px] text-muted-foreground" style={{ paddingLeft: `${(depth + 1) * 16 + 22}px` }}>
          {t("client.emptyFolder")}
        </div>
      )}
    </li>
  )
}

function NewFolderDialog({
  trigger,
  onCreate,
}: {
  trigger: ReactElement
  onCreate: (name: string) => void | Promise<void>
}) {
  const { t } = useLanguage()
  const [open, setOpen] = useState(false)
  const [name, setName] = useState("")
  const [error, setError] = useState("")

  async function submit() {
    const trimmed = name.trim()
    if (!trimmed) {
      setError(t("client.folderNameRequired"))
      return
    }
    setError("")
    try {
      await onCreate(trimmed)
      setOpen(false)
      setName("")
    } catch {
      setError(t("client.createFolderFailed"))
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next)
        if (!next) {
          setName("")
          setError("")
        }
      }}
    >
      <DialogTrigger render={trigger} />
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("client.newFolderTitle")}</DialogTitle>
        </DialogHeader>
        <div className="flex flex-col gap-2">
          <Label htmlFor="new-folder-name">{t("common.name")}</Label>
          <Input
            id="new-folder-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder={t("client.newFolderPlaceholder")}
          />
          <FormError>{error}</FormError>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={() => setOpen(false)}>
            {t("common.cancel")}
          </Button>
          <Button onClick={submit}>{t("common.add")}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function RenameDialog({
  trigger,
  initialName,
  onRename,
}: {
  trigger: ReactElement
  initialName: string
  onRename: (name: string) => void | Promise<void>
}) {
  const { t } = useLanguage()
  const [open, setOpen] = useState(false)
  const [name, setName] = useState(initialName)
  const [error, setError] = useState("")

  async function submit() {
    const trimmed = name.trim()
    if (!trimmed) {
      setError(t("client.folderNameRequired"))
      return
    }
    setError("")
    try {
      await onRename(trimmed)
      setOpen(false)
    } catch {
      setError(t("client.renameFailed"))
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next)
        if (next) setName(initialName)
        else setError("")
      }}
    >
      <DialogTrigger render={trigger} />
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("client.renameTitle")}</DialogTitle>
        </DialogHeader>
        <div className="flex flex-col gap-2">
          <Label htmlFor="rename-node-name">{t("common.name")}</Label>
          <Input id="rename-node-name" value={name} onChange={(e) => setName(e.target.value)} />
          <FormError>{error}</FormError>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={() => setOpen(false)}>
            {t("common.cancel")}
          </Button>
          <Button onClick={submit}>{t("common.save")}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export { ClientFileTree }
