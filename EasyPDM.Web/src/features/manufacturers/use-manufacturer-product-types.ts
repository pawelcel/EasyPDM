import { useEffect, useState } from "react"

import { api } from "@/api/client"
import { useManufacturers } from "@/features/manufacturers/use-manufacturers"

// Typy produktów producenta wskazanego NAZWĄ — bo tak właśnie element trzyma producenta
// (properties.manufacturer to wolny tekst, nie klucz obcy), więc id trzeba najpierw
// odszukać po nazwie w katalogu; ten sam sposób dopasowania stosuje ManufacturerInfoButton
// w property-fields.tsx. Pusta nazwa (albo nazwa spoza katalogu) daje pustą listę.
// Używane w dwóch miejscach: pole "Typ produktu" przy elemencie i filtr w "Całej bazie".
export function useManufacturerProductTypes(manufacturerName: string) {
  const { manufacturers } = useManufacturers("")
  const manufacturerId = manufacturers.find((m) => m.name === manufacturerName)?.id ?? null
  const [productTypes, setProductTypes] = useState<string[]>([])

  useEffect(() => {
    if (manufacturerId === null) {
      setProductTypes([])
      return
    }
    let cancelled = false
    api
      .getManufacturer(manufacturerId)
      .then((detail) => {
        if (!cancelled) setProductTypes(detail.productTypes.map((p) => p.name))
      })
      .catch(() => {
        if (!cancelled) setProductTypes([])
      })
    return () => {
      cancelled = true
    }
  }, [manufacturerId])

  return { productTypes, manufacturerId }
}
