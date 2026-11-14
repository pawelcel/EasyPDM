import type { TranslationKey } from "@/i18n/translations"

// Czytelny opis zmiany właściwości w Historii elementu: klucze properties (rodzaj, orderNumber,
// processes...) to nazwy wewnętrzne, a wartości obróbek i tłumaczeń to całe obiekty JSON —
// pokazane wprost były nieczytelne. Tu zamieniamy je na nazwy pól z formularza i krótkie opisy.
// Dane w bazie (item_change_history) zostają surowe; to tylko sposób wyświetlania.

type T = (key: TranslationKey, vars?: Record<string, string | number>) => string

const PROPERTY_LABELS: Record<string, TranslationKey> = {
  rodzaj: "part.kind",
  material: "part.material",
  norm: "part.norm",
  notes: "part.notes",
  mass: "part.mass",
  manufacturer: "part.manufacturer",
  productType: "part.productType",
  productSubtype: "part.productSubtype",
  orderNumber: "part.orderNumber",
  orderNumber2: "part.orderNumber2",
  client: "part.client",
  clientName2: "part.clientName2",
  price: "part.price",
  currency: "history.prop.currency",
  priceType: "history.prop.priceType",
  priceDate: "history.prop.priceDate",
  processes: "part.processes",
  nameTexts: "history.prop.nameTexts",
}

// Rodzaj zapisany jest polską wartością wewnętrzną — pokazujemy etykietę w języku interfejsu.
const KIND_LABELS: Record<string, TranslationKey> = {
  Wykonywana: "part.kindManufactured",
  Zakupowa: "part.kindPurchased",
  Normalia: "part.kindStandard",
  Klienta: "part.kindClient",
  Wykonywane: "assembly.kindManufactured",
  Zakupowe: "assembly.kindPurchased",
}

export function propertyLabel(key: string | null, t: T): string {
  if (!key) return ""
  const label = PROPERTY_LABELS[key]
  return label ? t(label) : key
}

function shorten(text: string): string {
  return text.length > 60 ? `${text.slice(0, 57)}…` : text
}

function parseList(text: string | null): unknown[] {
  if (!text) return []
  try {
    const value = JSON.parse(text)
    return Array.isArray(value) ? value : []
  } catch {
    return []
  }
}

interface Process {
  key: string
  label: string
}

function processes(text: string | null): Process[] {
  return parseList(text).flatMap((entry): Process[] => {
    if (typeof entry === "string") return entry ? [{ key: `s:${entry}`, label: entry }] : []
    if (!entry || typeof entry !== "object") return []
    const e = entry as { id?: unknown; name?: unknown; description?: unknown }
    const name = typeof e.name === "string" ? e.name : ""
    const description = typeof e.description === "string" && e.description.trim() ? e.description : ""
    const label = description ? `${name} (${description})` : name
    return [{ key: e.id !== undefined ? `id:${String(e.id)}` : `n:${name}`, label }]
  })
}

// "dodano Hartowanie (do 50 HRC); usunięto Malowanie" — zmiana listy obróbek względem poprzedniej.
function describeProcesses(oldText: string | null, newText: string | null, t: T): string {
  const before = processes(oldText)
  const after = processes(newText)
  const beforeByKey = new Map(before.map((p) => [p.key, p]))
  const afterKeys = new Set(after.map((p) => p.key))
  const parts: string[] = []
  for (const p of after) {
    const old = beforeByKey.get(p.key)
    if (!old) parts.push(t("history.listAdded", { item: p.label }))
    else if (old.label !== p.label) parts.push(t("history.listChanged", { item: p.label }))
  }
  for (const p of before) if (!afterKeys.has(p.key)) parts.push(t("history.listRemoved", { item: p.label }))
  // Ta sama lista z innymi tłumaczeniami albo kolejnością.
  return parts.length ? parts.join("; ") : t("history.listUpdated")
}

// "EN: Cube, DE: Würfel" — kody języków z listy języków dokumentacji.
function nameTexts(text: string | null, codeOf: (languageId: number) => string): string {
  const items = parseList(text).flatMap((entry): string[] => {
    if (!entry || typeof entry !== "object") return []
    const e = entry as { languageId?: unknown; name?: unknown }
    if (typeof e.languageId !== "number" || typeof e.name !== "string" || !e.name.trim()) return []
    return [`${codeOf(e.languageId)}: ${e.name}`]
  })
  return items.length ? items.join(", ") : "—"
}

function plainValue(key: string | null, text: string | null, t: T, codeOf: (id: number) => string): string {
  if (text === null || text === "") return "—"
  if (key === "rodzaj" && KIND_LABELS[text]) return t(KIND_LABELS[text])
  if (key === "nameTexts") return shorten(nameTexts(text, codeOf))
  return shorten(text)
}

export function describePropertySet(
  key: string | null,
  oldValue: string | null,
  newValue: string | null,
  t: T,
  codeOf: (languageId: number) => string
): string {
  if (key === "processes") {
    return t("history.propertyListChange", { key: propertyLabel(key, t), changes: describeProcesses(oldValue, newValue, t) })
  }
  return t("history.propertySet", {
    key: propertyLabel(key, t),
    from: plainValue(key, oldValue, t, codeOf),
    to: plainValue(key, newValue, t, codeOf),
  })
}

export function describePropertyRemoved(
  key: string | null,
  oldValue: string | null,
  t: T,
  codeOf: (languageId: number) => string
): string {
  const from = key === "processes" ? processes(oldValue).map((p) => p.label).join(", ") || "—" : plainValue(key, oldValue, t, codeOf)
  return t("history.propertyRemoved", { key: propertyLabel(key, t), from: shorten(from) })
}
