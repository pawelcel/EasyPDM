import { type ReactElement, useEffect, useRef, useState } from "react"
import { Upload } from "lucide-react"

import { api, ApiError } from "@/api/client"
import { itemDisplayLabel, type Item, type ItemType, type ProcessRef, type Project } from "@/api/types"
import { Button } from "@/components/ui/button"
import {
  Combobox,
  ComboboxContent,
  ComboboxEmpty,
  ComboboxInput,
  ComboboxItem,
  ComboboxList,
} from "@/components/ui/combobox"
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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import {
  ClientAndName2Fields,
  ManufacturerField,
  MaterialField,
  ProductTypeAndSubtypeFields,
  PropField,
} from "@/features/items/property-fields"
import { ASSEMBLY_KIND_FIELDS, PART_KIND_FIELDS } from "@/features/items/part-property-form"
import { ProcessSelect } from "@/features/processes/process-select"
import { DocumentationLanguageProvider } from "@/features/project-languages/documentation-language"
import type { TranslationKey } from "@/i18n/translations"
import { useLanguage } from "@/i18n/use-language"

type Mode = ItemType | "existing"

// Próg, powyżej którego ostrzegamy, że eksport STEP potrwa. 10 MB to nie jest granica
// "pliku dużego" w sensie transferu -- sam upload tyle nie boli. Chodzi o to, co robi CAD,
// ZANIM cokolwiek wyśle: przeliczenie geometrii do STEP-a przy modelu tej wielkości potrafi
// zająć minuty, a przy złożeniu idzie przez całe drzewo. Użytkownik widzi wtedy tylko
// "zawieszony" CAD i nie wie, że sam to zlecił tym checkboxem.
const STEP_SLOW_EXPORT_BYTES = 10 * 1024 * 1024

const MODE_LABEL_KEYS: Record<Mode, TranslationKey> = {
  folder: "itemType.folder",
  part: "itemType.part",
  assembly: "itemType.assembly",
  file: "addNode.modeFile",
  existing: "addNode.modeExisting",
}

// Elementy o tej samej nazwie mogą pochodzić z różnych projektów — numer odróżnia je.
function candidateLabel(item: Item | undefined): string | undefined {
  return item ? itemDisplayLabel(item) : undefined
}

// Co wolno dodać jako dziecko pod czym w strukturze:
//   projekt (brak rodzica) / folder -> wszystko
//   złożenie                        -> tylko część i złożenie (BOM)
//   część / plik                    -> nic (są liśćmi struktury)
function modesForParent(parentType: ItemType | null): Mode[] {
  if (parentType === null || parentType === "folder")
    return ["folder", "part", "assembly", "file", "existing"]
  if (parentType === "assembly") return ["part", "assembly", "existing"]
  return []
}

function AddNodeDialog({
  trigger,
  projectId: fixedProjectId,
  parentId: fixedParentId,
  parentType: fixedParentType,
  lockMode,
  initialOpen,
  initialName,
  initialMode,
  initialProperties,
  materialLocked,
  documentSize,
  ticket,
  assemblyComponent,
  onOpenChange,
  onCreated,
}: {
  trigger: ReactElement
  // Gdy podane (zwykłe użycie — pasek narzędzi projektu, węzeł drzewa, panel szczegółów):
  // dialog tworzy od razu w TYM projekcie/pod TYM rodzicem, bez pytania o nic dodatkowego.
  // Gdy POMINIĘTE albo `null` (element bez projektu — zob. Item.projectId): dialog SAM
  // pyta o projekt i (opcjonalnie) element nadrzędny jako pierwszy krok — nie wymaga
  // wcześniejszej nawigacji po panelu projektów, wszystko dzieje się w tym jednym okienku.
  projectId?: string | null
  parentId?: string | null
  parentType?: ItemType | null
  lockMode?: ItemType
  // Wymusza otwarcie od razu po zamontowaniu — używane przez PendingTicketBanner, które
  // montuje tę instancję dopiero po kliknięciu "Nowy element", z ukrytym triggerem.
  initialOpen?: boolean
  initialName?: string
  // Wstępnie wybrany tryb (Część/Złożenie) — używane przez "Duplikuj" w PendingTicketBanner,
  // żeby od razu otworzyć się na tym samym typie co skopiowany element (nadal można zmienić
  // ręcznie, w odróżnieniu od lockMode, który chowa przyciski trybu całkowicie).
  initialMode?: ItemType
  // Właściwości, którymi wypełnia się pola Część/Złożenie od razu po otwarciu — używane przez
  // "Duplikuj" (zob. PendingTicketBanner): kopiuje rodzaj/materiał/producenta/numery/normę/masę
  // ZE WSKAZANEGO elementu, bez kopiowania żadnych plików — dalej można je tu edytować przed
  // zapisem, to zwykłe tworzenie NOWEGO elementu, tylko wstępnie wypełnione.
  initialProperties?: Record<string, unknown>
  // Materiał w initialProperties przyszedł z dokumentu CAD, nie z wyboru użytkownika — pole
  // Materiał pokazuje go wtedy, ale nie daje go zmienić (zob. MaterialField). Ustawia to
  // PendingTicketBanner tylko dla materiału z biletu; materiał skopiowany przez "Duplikuj"
  // został wybrany świadomie i zostaje edytowalny.
  materialLocked?: boolean
  // Rozmiar dokumentu CAD w bajtach (z biletu makra) -- tylko po to, żeby ostrzec przy opcji
  // eksportu STEP, że dla dużego modelu potrwa to długo. Zob. STEP_SLOW_EXPORT_BYTES.
  documentSize?: number
  // Bilet z makra CAD (zob. EasyPDM.FreeCad/EasyPDMUpload.FCMacro) — doklejany do POST
  // /nodes, żeby makro mogło się dowiedzieć (GET /create-tickets/{ticket}), że element
  // powstał. Podawany JAWNIE przez wywołującego (PendingTicketBanner), nie czytany z
  // żadnego globalnego stanu — zwykłe, ręczne użycia tego dialogu (bez tego propa) nigdy
  // nie mają z biletem nic wspólnego, niezależnie od tego, czy jakiś akurat czeka.
  ticket?: string
  // Bilet dotyczy komponentu złożenia wysyłanego z makra CAD (zob. PendingTicket.component):
  // bez wyboru projektu i rodzica — element powstaje bez projektu, makro podpina go pod
  // złożenie według drzewa CAD, a serwer przypisuje mu wtedy projekt złożenia.
  assemblyComponent?: boolean
  // Wołane przy KAŻDEJ zmianie otwarcia/zamknięcia — PendingTicketBanner używa tego, żeby
  // wrócić do wyboru "Nowy/Istniejący", gdy użytkownik zamknie to okno bez tworzenia.
  onOpenChange?: (open: boolean) => void
  onCreated: () => void | Promise<void>
}) {
  function seedRodzaj(): string {
    const v = initialProperties?.rodzaj
    return typeof v === "string" ? v : ""
  }
  function seedMass(): string {
    const v = initialProperties?.mass
    return v === undefined || v === null ? "" : String(v)
  }
  function seedExtraProps(): Record<string, string> {
    const result: Record<string, string> = {}
    for (const key of ["material", "manufacturer", "productType", "productSubtype", "orderNumber", "orderNumber2", "norm", "client", "clientName2"]) {
      const v = initialProperties?.[key]
      if (typeof v === "string") result[key] = v
    }
    return result
  }

  const { t } = useLanguage()
  const needsProjectPicker = fixedProjectId == null && !assemblyComponent
  // Checkbox "Dodaj do projektu" -- zob. skipProject niżej (obok "mode") po pełne
  // wyjaśnienie, co robi odznaczenie.
  const [addToProject, setAddToProject] = useState(true)
  const [open, setOpenState] = useState(initialOpen ?? false)
  function setOpen(next: boolean) {
    setOpenState(next)
    onOpenChange?.(next)
  }

  // Projekt/rodzic — albo z góry ustalone (zwykłe użycie), albo wybierane W TYM OKNIE
  // (zob. needsProjectPicker wyżej).
  const [projects, setProjects] = useState<Project[]>([])
  const [selectedProjectId, setSelectedProjectId] = useState("")
  const projectId = fixedProjectId ?? selectedProjectId
  const [parentCandidates, setParentCandidates] = useState<Item[]>([])
  const [selectedParentId, setSelectedParentId] = useState<string | null>(null)
  const parentId = fixedParentId !== undefined ? fixedParentId : selectedParentId
  const parentType =
    fixedParentType !== undefined
      ? fixedParentType
      : (parentCandidates.find((i) => i.id === selectedParentId)?.itemType ?? null)

  useEffect(() => {
    // Zamknięty projekt nie ma się tu pojawić -- nie ma sensu dorzucać nowych elementów do
    // projektu, który jest odłożony na bok.
    if (needsProjectPicker && open) {
      api.getProjects().then((list) => setProjects(list.filter((p) => !p.closed)))
    }
  }, [needsProjectPicker, open])

  // Główny język projektu, do którego trafi element — w nim pokazują się obróbki do wyboru.
  // Bez projektu (albo zanim się wczyta) obowiązuje domyślny język dokumentacji.
  const [mainLanguageId, setMainLanguageId] = useState<number | undefined>(undefined)
  useEffect(() => {
    if (!open || !projectId) {
      setMainLanguageId(undefined)
      return
    }
    let cancelled = false
    api.getProjects().then((list) => {
      if (!cancelled) setMainLanguageId(list.find((p) => p.id === projectId)?.mainLanguageId)
    })
    return () => {
      cancelled = true
    }
  }, [open, projectId])

  useEffect(() => {
    if (!needsProjectPicker || !projectId) {
      setParentCandidates([])
      return
    }
    let cancelled = false
    api.getItems({ projectId }).then((items) => {
      if (cancelled) return
      setParentCandidates(items.filter((i) => i.itemType === "folder" || i.itemType === "assembly"))
    })
    return () => {
      cancelled = true
    }
  }, [needsProjectPicker, projectId])

  // Bilet doklejony do POST /nodes wymaga sensownego typu (Część/Złożenie) — makro nigdy
  // nie prosi o Folder/Plik/Istniejący, więc ten dialog, gdy ma podpięty bilet, w ogóle nie
  // pokazuje tych opcji (dokładnie tak, jak dawny natywny dialog makra ograniczał wybór do
  // Część/Złożenie).
  const rawAvailableModes = modesForParent(parentType)
  const availableModes = ticket
    ? rawAvailableModes.filter((m): m is "part" | "assembly" => m === "part" || m === "assembly")
    : rawAvailableModes
  const [mode, setMode] = useState<Mode>(initialMode ?? lockMode ?? availableModes[0] ?? "folder")
  // Checkbox "Dodaj do projektu" ograniczony do Część/Złożenie -- Folder bez projektu nie
  // miałby gdzie istnieć (foldery organizują drzewo WEWNĄTRZ projektu), a "Plik"/"Istniejący"
  // idą osobnymi ścieżkami (upload multipart / dowiązanie już istniejącego elementu), których
  // to pole na razie nie obejmuje. Odznaczenie: element trafia do bazy z project_id = NULL
  // (POST /nodes zamiast /projects/{id}/nodes), widoczny wyłącznie przez "Cała baza" —
  // dokładnie ten sam stan co "Usuń ze struktury" + odpięcie od projektu. Przydatne przy
  // komponentach złożenia tworzonych automatycznie przez makro CAD: makro samo dogrywa nowy
  // element do BOM-u rodzica osobnym wywołaniem PO utworzeniu (niezależnie od project_id),
  // więc odznaczenie tego dla komponentów pozwala uniknąć zaśmiecania drzewa projektu każdym
  // z osobna — widoczne będą TYLKO jako pozycje BOM złożenia, nie jako korzenie projektu.
  const projectOptional = mode === "part" || mode === "assembly"
  const skipProject = assemblyComponent || (needsProjectPicker && projectOptional && !addToProject)
  // Zmiana wybranego rodzica (albo projektu, co czyści rodzica) może uczynić bieżący "mode"
  // niedostępnym pod nowym rodzicem (np. Złożenie nie przyjmuje Folderu) — bez tego
  // przyciski trybu poprawnie by się ukryły, ale stan "mode" zostałby przy starym,
  // niedostępnym wyborze i formularz dalej by go renderował, prowadząc do odrzuconego przez
  // backend zgłoszenia zamiast zablokowania tego w samym UI.
  const availableModesKey = availableModes.join(",")
  useEffect(() => {
    if (!lockMode && !availableModes.includes(mode)) {
      setMode(availableModes[0] ?? "folder")
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [availableModesKey, lockMode])
  const [allItems, setAllItems] = useState<Item[]>([])

  // Folder / Część
  const [name, setName] = useState(initialName ?? "")
  const [mass, setMass] = useState(seedMass)
  const [rodzaj, setRodzaj] = useState(seedRodzaj)
  // Materiał/Producent/Numery zamówieniowe/Norma — pola zależne od rodzaju, te same co w
  // panelu szczegółów już istniejącego elementu (PartPropertyForm), tylko zbierane lokalnie
  // przed pierwszym zapisem zamiast od razu wysyłane przez API (element jeszcze nie istnieje).
  const [extraProps, setExtraProps] = useState<Record<string, string>>(seedExtraProps)
  function setExtraField(key: string, value: string) {
    setExtraProps((prev) => ({ ...prev, [key]: value }))
  }
  // Zmiana rodzaju W TRAKCIE tworzenia (przed pierwszym zapisem) — pola POPRZEDNIEGO
  // rodzaju czyszczone z lokalnego stanu, inaczej zostałyby zebrane przy submit mimo że
  // ich pola już się nie pokazują (extraProps przeżywa przełączenie rodzaju, bo to jeden
  // wspólny Record dla wszystkich rodzajów na raz — zob. PART_KIND_FIELDS/ASSEMBLY_KIND_FIELDS).
  // Obróbki — lista, więc trzymana osobno od tekstowych extraProps. Mają je tylko elementy
  // wykonywane; zmiana rodzaju na inny czyści wybór, tak jak w panelu elementu.
  const [processes, setProcesses] = useState<ProcessRef[]>([])
  function changeRodzaj(next: string) {
    if (next !== "Wykonywana" && next !== "Wykonywane") setProcesses([])
    const staleFields = (mode === "assembly" ? ASSEMBLY_KIND_FIELDS : PART_KIND_FIELDS)[rodzaj] ?? []
    if (staleFields.length > 0) {
      setExtraProps((prev) => {
        const copy = { ...prev }
        for (const key of staleFields) delete copy[key]
        return copy
      })
    }
    setRodzaj(next)
  }
  // Widoczny tylko, gdy tworzenie ma przypięty bilet z makra (zob. props "ticket" wyżej) —
  // w normalnym, ręcznym dodawaniu przez aplikację webową nie ma żadnego lokalnego pliku
  // CAD, więc nie ma czego eksportować. Domyślnie zaznaczony (tak jak dawniej w natywnym
  // dialogu makra).
  const [exportStep, setExportStep] = useState(true)
  const [exportPdf, setExportPdf] = useState(false)

  // Inny plik
  const [file, setFile] = useState<File | null>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)
  const [propsText, setPropsText] = useState("")

  // Istniejący element
  const [childId, setChildId] = useState("")
  const [quantity, setQuantity] = useState("1")

  const [error, setError] = useState("")
  const [submitting, setSubmitting] = useState(false)

  // Kandydaci do "Istniejący element" — pobierane leniwie z całej bazy, nie tylko z tego
  // projektu, bo Część/Złożenie mogą być współdzielone jako komponent w wielu projektach.
  useEffect(() => {
    if (!open) return
    api.getItems({}).then(setAllItems)
  }, [open])

  const partsAndAssemblies = allItems.filter(
    (i) => i.itemType === "part" || i.itemType === "assembly"
  )
  // Pod folderem i w korzeniu dochodzą foldery i pliki z całej bazy (pod złożeniem nie
  // wolno -- BOM to tylko Części i Złożenia, zob. modesForParent i IsChildTypeAllowed). Są
  // współdzielone tak samo jak Część: ten sam folder albo plik może stać w wielu miejscach
  // i projektach (decyzja użytkownika). Wcześniej lista miała tylko Części i Złożenia, więc
  // folderu czy pliku po "Usuń ze struktury" nie dało się nigdzie wstawić z powrotem.
  // Wstawienie folderu do jego własnego podfolderu serwer odrzuca jako cykl.
  const foldersAndFiles =
    parentType === "assembly" ? [] : allItems.filter((i) => i.itemType === "folder" || i.itemType === "file")

  // Dowolna Część/Złożenie z całej bazy (poza samym sobą, gdy dodajemy pod konkretnym
  // rodzicem) — ten sam komponent można wykorzystać w wielu złożeniach/projektach.
  // Na poziomie głównym (bez rodzica): wybór elementu z innego projektu przenosi go
  // (project_id) i pokazuje jako korzeń tego projektu; wybór elementu już należącego
  // do tego projektu po prostu przywraca go, jeśli był odpięty („Usuń ze struktury”).
  const candidates = [...partsAndAssemblies, ...foldersAndFiles].filter((i) => i.id !== parentId)

  // Folder i plik nie mają numeru, więc sama nazwa nic nie mówi o typie -- dopisujemy go.
  function existingLabel(item: Item | undefined): string | undefined {
    if (!item) return undefined
    const label = candidateLabel(item)
    return item.itemType === "folder" || item.itemType === "file"
      ? `${label} — ${t(item.itemType === "folder" ? "itemType.folder" : "itemType.file")}`
      : label
  }

  function reset() {
    setMode(initialMode ?? lockMode ?? availableModes[0] ?? "folder")
    setName(initialName ?? "")
    setMass(seedMass())
    setRodzaj(seedRodzaj())
    setExtraProps(seedExtraProps())
    setExportStep(true)
    setFile(null)
    setPropsText("")
    setProcesses([])
    setChildId("")
    setQuantity("1")
    setError("")
    if (needsProjectPicker) {
      setSelectedProjectId("")
      setSelectedParentId(null)
      setAddToProject(true)
    }
  }

  async function handleCreateContainer(itemType: "folder" | "part" | "assembly") {
    // Brak projektu jest tu poprawnym, świadomym wyborem, gdy skipProject (checkbox "Dodaj
    // do projektu" odznaczony) -- błąd walidacji ma sens tylko, gdy projekt jest faktycznie
    // wymagany, a po prostu nie został jeszcze wybrany.
    if (needsProjectPicker && !skipProject && !projectId) {
      setError(t("addNode.projectRequired"))
      return
    }
    if (itemType === "part" && !rodzaj) {
      setError(
        t("addNode.selectKindError", {
          manufactured: t("part.kindManufactured"),
          purchased: t("part.kindPurchased"),
          standard: t("part.kindStandard"),
          client: t("part.kindClient"),
        })
      )
      return
    }
    if (itemType === "assembly" && !rodzaj) {
      setError(
        t("addNode.selectAssemblyKindError", {
          manufactured: t("assembly.kindManufactured"),
          purchased: t("assembly.kindPurchased"),
          client: t("assembly.kindClient"),
        })
      )
      return
    }

    const trimmed = name.trim()
    if (!trimmed) {
      setError(t("addNode.nameRequired"))
      return
    }

    const properties: Record<string, unknown> = {}
    if (itemType === "part") {
      properties.rodzaj = rodzaj
      if (rodzaj === "Zakupowa" && mass.trim()) properties.mass = mass.trim()
      for (const [key, value] of Object.entries(extraProps)) {
        if (value.trim()) properties[key] = value.trim()
      }
      if (rodzaj === "Wykonywana" && processes.length > 0) properties.processes = processes
    }
    if (itemType === "assembly") {
      properties.rodzaj = rodzaj
      if (mass.trim()) properties.mass = mass.trim()
      if (rodzaj === "Wykonywane" && processes.length > 0) properties.processes = processes
      if (rodzaj === "Zakupowe") {
        for (const key of ["manufacturer", "productType", "productSubtype"]) {
          if (extraProps[key]?.trim()) properties[key] = extraProps[key].trim()
        }
      }
      if (rodzaj === "Klienta") {
        for (const key of ["client", "clientName2"]) {
          if (extraProps[key]?.trim()) properties[key] = extraProps[key].trim()
        }
      }
    }

    setSubmitting(true)
    setError("")
    try {
      const nodeBody = {
        name: trimmed,
        itemType,
        properties,
        // Bez projektu nie ma z czego wybrać rodzica (picker rodzica pokazuje wyłącznie
        // foldery/złożenia z WYBRANEGO projektu, zob. efekt fetchujący parentCandidates) --
        // parentId zawsze null w tej gałęzi, żadnej utraty funkcjonalności.
        parentId: skipProject ? null : parentId,
        ticket,
        exportStep: ticket ? exportStep : undefined,
        exportPdf: ticket ? exportPdf : undefined,
      }
      if (skipProject) {
        await api.createNodeWithoutProject(nodeBody)
      } else {
        await api.createNode(projectId, nodeBody)
      }
      setOpen(false)
      reset()
      await onCreated()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t("addNode.createFailed"))
    } finally {
      setSubmitting(false)
    }
  }

  async function handleUploadFile() {
    const trimmedName = name.trim()
    if (!file && !trimmedName) {
      setError(t("addNode.fileOrNameRequired"))
      return
    }

    const trimmedProps = propsText.trim()
    let parsedProps: Record<string, unknown> = {}
    if (trimmedProps) {
      try {
        parsedProps = JSON.parse(trimmedProps)
      } catch {
        setError(t("addNode.invalidJson"))
        return
      }
    }

    setSubmitting(true)
    setError("")
    try {
      if (file) {
        const formData = new FormData()
        formData.append("file", file)
        if (trimmedProps) formData.append("properties", trimmedProps)
        if (parentId) formData.append("parentId", parentId)
        await api.uploadItem(projectId, formData)
      } else {
        await api.createNode(projectId, {
          name: trimmedName,
          itemType: "file",
          properties: parsedProps,
          parentId,
        })
      }
      setOpen(false)
      reset()
      await onCreated()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t("addNode.addFailed"))
    } finally {
      setSubmitting(false)
    }
  }

  async function handleLinkExisting() {
    if (!childId) {
      setError(t("addNode.selectItemRequired"))
      return
    }

    setSubmitting(true)
    setError("")
    try {
      if (parentId) {
        const qty = Number(quantity)
        if (!Number.isFinite(qty) || qty <= 0) {
          setError(t("addNode.quantityInvalid"))
          setSubmitting(false)
          return
        }
        await api.addChild(parentId, childId, qty)
      } else {
        // Bez rodzica: element z innego projektu trzeba przenieść (project_id) i pokazać jako
        // korzeń; element już należący do tego projektu — po prostu przywracamy jego widoczność.
        const target = candidates.find((c) => c.id === childId)
        if (target && target.projectId !== projectId) {
          await api.moveItemToProject(childId, projectId)
        } else {
          await api.setShowInTree(childId, true)
        }
      }
      setOpen(false)
      reset()
      await onCreated()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t("addNode.addFailed"))
    } finally {
      setSubmitting(false)
    }
  }

  function handleSubmit() {
    if (mode === "folder" || mode === "part" || mode === "assembly")
      return handleCreateContainer(mode)
    if (mode === "file") return handleUploadFile()
    return handleLinkExisting()
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next)
        if (!next) reset()
      }}
    >
      <DialogTrigger render={trigger} />
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{lockMode === "file" ? t("item.uploadFile") : t("addNode.title")}</DialogTitle>
        </DialogHeader>

        {/* Widoczne tylko gdy ten dialog obsługuje bilet z makra CAD (ticket podany) --
            makro może przetwarzać CAŁE drzewo złożenia, jeden nowy komponent na bilet, więc
            bez tego przypomnienia łatwo stracić orientację, KTÓREGO lokalnego pliku dotyczy
            akurat wypełniany formularz (zgłoszone jako mylące przy wyborze rodzaju elementu). */}
        {ticket && initialName && (
          <Hint>{t("addNode.currentFileHint", { name: initialName })}</Hint>
        )}
        {assemblyComponent && <Hint>{t("addNode.assemblyComponentHint")}</Hint>}

        {needsProjectPicker && (
          <div className="flex flex-col gap-2">
            {projectOptional && (
              <label className="flex cursor-pointer items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={addToProject}
                  onChange={(e) => {
                    setAddToProject(e.target.checked)
                    // Odznaczenie czyści wcześniej wybrany projekt/rodzica -- inaczej
                    // ponowne zaznaczenie pokazałoby stary wybór, mimo że w międzyczasie
                    // element miał już powstać BEZ projektu.
                    if (!e.target.checked) {
                      setSelectedProjectId("")
                      setSelectedParentId(null)
                    }
                  }}
                  className="size-3.5 shrink-0 accent-primary"
                />
                {t("addNode.addToProjectLabel")}
              </label>
            )}

            {!skipProject && (
              <>
                <Label>{t("addNode.projectLabel")}</Label>
                <Select
                  value={selectedProjectId || "none"}
                  onValueChange={(v) => {
                    setSelectedProjectId(v === "none" ? "" : (v as string))
                    setSelectedParentId(null)
                  }}
                >
                  <SelectTrigger className="w-full">
                    <SelectValue>
                      {(v: string) =>
                        v === "none" || !v
                          ? t("addNode.selectProjectPlaceholder")
                          : (projects.find((p) => p.id === v)?.name ?? v)
                      }
                    </SelectValue>
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="none">{t("addNode.selectProjectPlaceholder")}</SelectItem>
                    {projects.map((p) => (
                      <SelectItem key={p.id} value={p.id}>
                        {p.name}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>

                {projectId && (
                  <>
                    <Label>{t("addNode.parentLabel")}</Label>
                    <Select
                      value={selectedParentId ?? "root"}
                      onValueChange={(v) => setSelectedParentId(v === "root" ? null : (v as string))}
                    >
                      <SelectTrigger className="w-full">
                        <SelectValue>
                          {(v: string) =>
                            v === "root" || !v
                              ? t("addNode.noParent")
                              : (candidateLabel(parentCandidates.find((i) => i.id === v)) ?? v)
                          }
                        </SelectValue>
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="root">{t("addNode.noParent")}</SelectItem>
                        {parentCandidates.map((i) => (
                          <SelectItem key={i.id} value={i.id}>
                            {candidateLabel(i)}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  </>
                )}
              </>
            )}
          </div>
        )}

        {(!needsProjectPicker || projectId || skipProject) && !lockMode && (
          <div className="flex flex-wrap gap-1.5">
            {availableModes.map((m) => (
              <Button
                key={m}
                type="button"
                size="sm"
                variant={mode === m ? "default" : "outline"}
                onClick={() => {
                  setMode(m)
                  setError("")
                }}
              >
                {t(MODE_LABEL_KEYS[m])}
              </Button>
            ))}
          </div>
        )}

        {(!needsProjectPicker || projectId || skipProject) && mode === "part" && (
          <div className="flex flex-col gap-2">
            <Label>{t("part.kind")}</Label>
            <div className="flex flex-wrap gap-1.5">
              <Button
                type="button"
                size="sm"
                variant={rodzaj === "Wykonywana" ? "default" : "outline"}
                onClick={() => changeRodzaj("Wykonywana")}
              >
                {t("part.kindManufactured")}
              </Button>
              <Button
                type="button"
                size="sm"
                variant={rodzaj === "Zakupowa" ? "default" : "outline"}
                onClick={() => changeRodzaj("Zakupowa")}
              >
                {t("part.kindPurchased")}
              </Button>
              <Button
                type="button"
                size="sm"
                variant={rodzaj === "Normalia" ? "default" : "outline"}
                onClick={() => changeRodzaj("Normalia")}
              >
                {t("part.kindStandard")}
              </Button>
              <Button
                type="button"
                size="sm"
                variant={rodzaj === "Klienta" ? "default" : "outline"}
                onClick={() => changeRodzaj("Klienta")}
              >
                {t("part.kindClient")}
              </Button>
            </div>
            <Label htmlFor="node-name">{t("common.name")}</Label>
            <Input id="node-name" value={name} onChange={(e) => setName(e.target.value)} />

            {(rodzaj === "Wykonywana" || rodzaj === "Normalia") && (
              <MaterialField
                value={extraProps.material ?? ""}
                onSave={setExtraField}
                disabled={false}
                locked={materialLocked}
              />
            )}
            {rodzaj === "Wykonywana" && (
              <>
                <Label>{t("part.processes")}</Label>
                <DocumentationLanguageProvider languageId={mainLanguageId}>
                  <ProcessSelect target="part" value={processes} onChange={setProcesses} />
                </DocumentationLanguageProvider>
              </>
            )}
            {rodzaj === "Zakupowa" && (
              <>
                <ManufacturerField value={extraProps.manufacturer ?? ""} onSave={setExtraField} disabled={false} />
                <ProductTypeAndSubtypeFields
                  manufacturerName={extraProps.manufacturer ?? ""}
                  productType={extraProps.productType ?? ""}
                  productSubtype={extraProps.productSubtype ?? ""}
                  onSave={setExtraField}
                  disabled={false}
                />
                <PropField
                  label={t("part.orderNumber")}
                  propKey="orderNumber"
                  value={extraProps.orderNumber ?? ""}
                  onSave={setExtraField}
                  disabled={false}
                />
                <PropField
                  label={t("part.orderNumber2")}
                  propKey="orderNumber2"
                  value={extraProps.orderNumber2 ?? ""}
                  onSave={setExtraField}
                  disabled={false}
                />
                <Label htmlFor="node-part-mass">{t("part.mass")}</Label>
                <Input
                  id="node-part-mass"
                  type="number"
                  step="any"
                  value={mass}
                  onChange={(e) => setMass(e.target.value)}
                  className="[appearance:textfield] [&::-webkit-inner-spin-button]:appearance-none [&::-webkit-outer-spin-button]:appearance-none"
                />
              </>
            )}
            {rodzaj === "Normalia" && (
              <PropField
                label={t("part.norm")}
                propKey="norm"
                value={extraProps.norm ?? ""}
                onSave={setExtraField}
                disabled={false}
              />
            )}
            {rodzaj === "Klienta" && (
              <ClientAndName2Fields
                clientName={extraProps.client ?? ""}
                clientName2={extraProps.clientName2 ?? ""}
                onSave={setExtraField}
                disabled={false}
              />
            )}

            {ticket && (
              <>
                <label className="flex cursor-pointer items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    checked={exportStep}
                    onChange={(e) => setExportStep(e.target.checked)}
                    className="size-3.5 shrink-0 accent-primary"
                  />
                  {t("addNode.exportStepOptional")}
                </label>
                {/* Eksport STEP robi sam CAD, zanim cokolwiek poleci na serwer, i dla dużego
                    modelu potrafi trwać bardzo długo -- przy złożeniu idzie przez całe drzewo.
                    Ostrzegamy ZANIM użytkownik zatwierdzi okno, bo potem jest już tylko czekanie
                    przy zajętym CAD-zie. Ostrzeżenie, nie blokada: czasem ten STEP jest po prostu
                    potrzebny. Tylko przy ZAZNACZONEJ opcji -- przy odznaczonej nic się nie
                    eksportuje, więc nie ma przed czym ostrzegać. */}
                {exportStep && documentSize !== undefined && documentSize > STEP_SLOW_EXPORT_BYTES && (
                  <Hint>
                    {t("addNode.exportStepSlowWarning", {
                      size: Math.round(documentSize / (1024 * 1024)).toString(),
                    })}
                  </Hint>
                )}
                <label className="flex cursor-pointer items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    checked={exportPdf}
                    onChange={(e) => setExportPdf(e.target.checked)}
                    className="size-3.5 shrink-0 accent-primary"
                  />
                  {t("addNode.exportPdfOptional")}
                </label>
              </>
            )}

            <FormError>{error}</FormError>
          </div>
        )}

        {(!needsProjectPicker || projectId || skipProject) && (mode === "folder" || mode === "assembly") && (
          <div className="flex flex-col gap-2">
            {mode === "assembly" && (
              <>
                <Label>{t("part.kind")}</Label>
                <div className="flex flex-wrap gap-1.5">
                  <Button
                    type="button"
                    size="sm"
                    variant={rodzaj === "Wykonywane" ? "default" : "outline"}
                    onClick={() => changeRodzaj("Wykonywane")}
                  >
                    {t("assembly.kindManufactured")}
                  </Button>
                  <Button
                    type="button"
                    size="sm"
                    variant={rodzaj === "Zakupowe" ? "default" : "outline"}
                    onClick={() => changeRodzaj("Zakupowe")}
                  >
                    {t("assembly.kindPurchased")}
                  </Button>
                  <Button
                    type="button"
                    size="sm"
                    variant={rodzaj === "Klienta" ? "default" : "outline"}
                    onClick={() => changeRodzaj("Klienta")}
                  >
                    {t("assembly.kindClient")}
                  </Button>
                </div>
              </>
            )}

            <Label htmlFor="node-name">{t("common.name")}</Label>
            <Input id="node-name" value={name} onChange={(e) => setName(e.target.value)} />

            {mode === "assembly" && rodzaj === "Wykonywane" && (
              <>
                <Label>{t("part.processes")}</Label>
                <DocumentationLanguageProvider languageId={mainLanguageId}>
                  <ProcessSelect target="assembly" value={processes} onChange={setProcesses} />
                </DocumentationLanguageProvider>
              </>
            )}
            {mode === "assembly" && rodzaj === "Zakupowe" && (
              <>
                <ManufacturerField
                  value={extraProps.manufacturer ?? ""}
                  onSave={setExtraField}
                  disabled={false}
                />
                <ProductTypeAndSubtypeFields
                  manufacturerName={extraProps.manufacturer ?? ""}
                  productType={extraProps.productType ?? ""}
                  productSubtype={extraProps.productSubtype ?? ""}
                  onSave={setExtraField}
                  disabled={false}
                />
              </>
            )}
            {mode === "assembly" && rodzaj === "Klienta" && (
              <ClientAndName2Fields
                clientName={extraProps.client ?? ""}
                clientName2={extraProps.clientName2 ?? ""}
                onSave={setExtraField}
                disabled={false}
              />
            )}

            {mode === "assembly" && (
              <>
                <Label htmlFor="node-mass">{t("addNode.massOptional")}</Label>
                <Input
                  id="node-mass"
                  type="number"
                  step="any"
                  value={mass}
                  onChange={(e) => setMass(e.target.value)}
                  className="[appearance:textfield] [&::-webkit-inner-spin-button]:appearance-none [&::-webkit-outer-spin-button]:appearance-none"
                />
              </>
            )}
            {mode === "assembly" && ticket && (
              <>
                <label className="flex cursor-pointer items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    checked={exportStep}
                    onChange={(e) => setExportStep(e.target.checked)}
                    className="size-3.5 shrink-0 accent-primary"
                  />
                  {t("addNode.exportStepOptional")}
                </label>
                {/* Eksport STEP robi sam CAD, zanim cokolwiek poleci na serwer, i dla dużego
                    modelu potrafi trwać bardzo długo -- przy złożeniu idzie przez całe drzewo.
                    Ostrzegamy ZANIM użytkownik zatwierdzi okno, bo potem jest już tylko czekanie
                    przy zajętym CAD-zie. Ostrzeżenie, nie blokada: czasem ten STEP jest po prostu
                    potrzebny. Tylko przy ZAZNACZONEJ opcji -- przy odznaczonej nic się nie
                    eksportuje, więc nie ma przed czym ostrzegać. */}
                {exportStep && documentSize !== undefined && documentSize > STEP_SLOW_EXPORT_BYTES && (
                  <Hint>
                    {t("addNode.exportStepSlowWarning", {
                      size: Math.round(documentSize / (1024 * 1024)).toString(),
                    })}
                  </Hint>
                )}
                <label className="flex cursor-pointer items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    checked={exportPdf}
                    onChange={(e) => setExportPdf(e.target.checked)}
                    className="size-3.5 shrink-0 accent-primary"
                  />
                  {t("addNode.exportPdfOptional")}
                </label>
              </>
            )}
            <FormError>{error}</FormError>
          </div>
        )}

        {(!needsProjectPicker || projectId || skipProject) && mode === "file" && (
          <div className="flex flex-col gap-2">
            <Label htmlFor="node-file">{t("addNode.fileOptional")}</Label>
            {/* Natywne pole pliku pokazuje "Przeglądaj… / Nie wybrano pliku" w języku
                PRZEGLĄDARKI, nie aplikacji, i strona nie ma jak tego przetłumaczyć (zgłoszone:
                polski napis przy angielskim interfejsie). Dlatego ukryte, a obok własny
                przycisk z tłumaczeń i nazwa wybranego pliku -- jak w pozostałych miejscach. */}
            <input
              ref={fileInputRef}
              id="node-file"
              type="file"
              className="hidden"
              onChange={(e) => {
                const picked = e.target.files?.[0] ?? null
                e.target.value = ""
                if (picked) setFile(picked)
              }}
            />
            <div className="flex min-w-0 items-center gap-2">
              <Button type="button" size="sm" variant="outline" onClick={() => fileInputRef.current?.click()}>
                <Upload className="size-3.5" /> {t("addNode.chooseFile")}
              </Button>
              <span className="truncate text-sm text-muted-foreground">
                {file ? file.name : t("addNode.noFileChosen")}
              </span>
            </div>
            {!file && (
              <>
                <Label htmlFor="node-file-name">{t("addNode.nameIfNoFile")}</Label>
                <Input
                  id="node-file-name"
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  placeholder={t("addNode.fileNamePlaceholder")}
                />
              </>
            )}
            <Label htmlFor="node-props">{t("addNode.propertiesJsonOptional")}</Label>
            <Input
              id="node-props"
              value={propsText}
              onChange={(e) => setPropsText(e.target.value)}
              placeholder={t("addNode.propertiesJsonPlaceholder")}
            />
            <FormError>{error}</FormError>
          </div>
        )}

        {(!needsProjectPicker || projectId || skipProject) && mode === "existing" && (
          <div className="flex flex-col gap-2">
            {parentId && parentType === "assembly" ? (
              <Hint>{t("addNode.existingHintWithParent")}</Hint>
            ) : parentId ? (
              <Hint>{t("addNode.existingHintFolder")}</Hint>
            ) : (
              <Hint>{t("addNode.existingHintNoParent")}</Hint>
            )}
            {candidates.length === 0 ? (
              <Hint>{t("addNode.noCandidates")}</Hint>
            ) : (
              <>
                <Label>{t("addNode.elementLabel")}</Label>
                <Combobox
                  items={candidates.map((c) => c.id)}
                  value={childId || null}
                  onValueChange={(v) => setChildId((v as string | null) ?? "")}
                  itemToStringLabel={(id: string) =>
                    existingLabel(candidates.find((c) => c.id === id)) ?? ""
                  }
                >
                  <ComboboxInput placeholder={t("part.searchPlaceholder")} />
                  <ComboboxContent>
                    <ComboboxEmpty>{t("addNode.noMatchingItems")}</ComboboxEmpty>
                    <ComboboxList>
                      {(id: string) => (
                        <ComboboxItem key={id} value={id}>
                          {existingLabel(candidates.find((c) => c.id === id))}
                        </ComboboxItem>
                      )}
                    </ComboboxList>
                  </ComboboxContent>
                </Combobox>
                {parentId && (
                  <>
                    <Label htmlFor="node-quantity">{t("common.quantity")}</Label>
                    <Input
                      id="node-quantity"
                      type="number"
                      min="0"
                      step="any"
                      value={quantity}
                      onChange={(e) => setQuantity(e.target.value)}
                    />
                  </>
                )}
              </>
            )}
            <FormError>{error}</FormError>
          </div>
        )}

        <DialogFooter>
          <Button variant="outline" onClick={() => setOpen(false)}>
            {t("common.cancel")}
          </Button>
          <Button onClick={handleSubmit} disabled={submitting || (needsProjectPicker && !skipProject && !projectId)}>
            {submitting ? t("common.saving") : t("common.add")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export { AddNodeDialog }
