import type { Item } from "@/api/types"
import type { RecordType } from "@/features/items/record-type-select"

// Filtr rodzaju obejmuje Części ORAZ Złożenia, a oba mają własne napisy w
// properties.rodzaj ("Zakupowa" vs "Zakupowe"). Dlatego wartością filtra jest POJĘCIE, nie
// surowy napis — jeden wybór ma trafiać w kupioną część i kupione złożenie naraz.
export type KindFilter = "all" | "manufactured" | "purchased" | "standard" | "client"

export interface DatabaseFilters {
  search: string
  tag: string
  recordType: RecordType
  partKind: KindFilter
  manufacturer: string
  productType: string
  client: string
}

const RECORD_TYPES: RecordType[] = ["all", "project", "part", "assembly", "other"]
const PART_KINDS: KindFilter[] = ["all", "manufactured", "purchased", "standard", "client"]

// Napis properties.rodzaj -> pojęcie, osobno dla każdego typu elementu. "standard"
// (Normalia) istnieje wyłącznie dla Części — żadne złożenie się na nie nie mapuje.
const PART_KIND_BY_CONCEPT: Record<Exclude<KindFilter, "all">, string | null> = {
  manufactured: "Wykonywana",
  purchased: "Zakupowa",
  standard: "Normalia",
  client: "Klienta",
}

const ASSEMBLY_KIND_BY_CONCEPT: Record<Exclude<KindFilter, "all">, string | null> = {
  manufactured: "Wykonywane",
  purchased: "Zakupowe",
  standard: null,
  client: "Klienta",
}

export function matchesKindFilter(item: Item, kind: KindFilter): boolean {
  if (kind === "all") return true
  const expected =
    item.itemType === "part"
      ? PART_KIND_BY_CONCEPT[kind]
      : item.itemType === "assembly"
        ? ASSEMBLY_KIND_BY_CONCEPT[kind]
        : null
  return expected !== null && item.properties.rodzaj === expected
}

// Filtry zapisane wcześniej trzymane są jako luźno typowany JSONB (Record<string, unknown>
// po stronie API) — ta funkcja broni się przed nieoczekiwanym/przestarzałym kształtem,
// zamiast ufać, że dawno zapisane dane wciąż dokładnie pasują do aktualnego kształtu.
// Dotyczy to i "partKind": do 0.2 trzymał surowe napisy rodzaju Części ("Zakupowa"), a od
// kiedy filtr obejmuje też Złożenia, trzyma pojęcia ("purchased") — stare wartości po
// prostu nie przejdą walidacji i spadną do "all".
export function coerceDatabaseFilters(raw: Record<string, unknown>): DatabaseFilters {
  return {
    search: typeof raw.search === "string" ? raw.search : "",
    tag: typeof raw.tag === "string" ? raw.tag : "",
    recordType: RECORD_TYPES.includes(raw.recordType as RecordType)
      ? (raw.recordType as RecordType)
      : "all",
    partKind: PART_KINDS.includes(raw.partKind as KindFilter) ? (raw.partKind as KindFilter) : "all",
    manufacturer: typeof raw.manufacturer === "string" ? raw.manufacturer : "",
    productType: typeof raw.productType === "string" ? raw.productType : "",
    client: typeof raw.client === "string" ? raw.client : "",
  }
}
