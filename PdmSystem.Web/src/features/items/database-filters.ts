import type { PartKindFilter } from "@/features/items/part-kind-select"
import type { RecordType } from "@/features/items/record-type-select"

export interface DatabaseFilters {
  search: string
  tag: string
  recordType: RecordType
  partKind: PartKindFilter
  manufacturer: string
}

const RECORD_TYPES: RecordType[] = ["all", "project", "part", "assembly", "other"]
const PART_KINDS: PartKindFilter[] = ["all", "Zakupowa", "Wykonywana", "Normalia"]

// Filtry zapisane wcześniej trzymane są jako luźno typowany JSONB (Record<string, unknown>
// po stronie API) — ta funkcja broni się przed nieoczekiwanym/przestarzałym kształtem (np.
// gdyby kiedyś zmieniły się dozwolone wartości "recordType"/"partKind"), zamiast ufać, że
// dawno zapisane dane wciąż dokładnie pasują do aktualnego kształtu.
export function coerceDatabaseFilters(raw: Record<string, unknown>): DatabaseFilters {
  return {
    search: typeof raw.search === "string" ? raw.search : "",
    tag: typeof raw.tag === "string" ? raw.tag : "",
    recordType: RECORD_TYPES.includes(raw.recordType as RecordType)
      ? (raw.recordType as RecordType)
      : "all",
    partKind: PART_KINDS.includes(raw.partKind as PartKindFilter)
      ? (raw.partKind as PartKindFilter)
      : "all",
    manufacturer: typeof raw.manufacturer === "string" ? raw.manufacturer : "",
  }
}
