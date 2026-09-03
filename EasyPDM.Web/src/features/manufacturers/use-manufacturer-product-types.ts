import { useEffect, useState } from "react"

import { api } from "@/api/client"
import type { ManufacturerProductType } from "@/api/types"
import { useManufacturers } from "@/features/manufacturers/use-manufacturers"

// Serie/typy (wraz z podtypami) producenta wskazanego NAZWĄ — bo tak właśnie element trzyma
// producenta (properties.manufacturer to wolny tekst, nie klucz obcy), więc id trzeba
// najpierw odszukać po nazwie w katalogu; ten sam sposób dopasowania stosuje
// ManufacturerInfoButton w property-fields.tsx. Pusta nazwa (albo nazwa spoza katalogu) daje
// pustą listę. Używane w czterech miejscach: pola Seria/Typ i Podtyp przy elemencie oraz oba
// odpowiadające im filtry w "Całej bazie".
export function useManufacturerProductTypes(manufacturerName: string) {
  const { manufacturers } = useManufacturers("")
  const manufacturerId = manufacturers.find((m) => m.name === manufacturerName)?.id ?? null
  const [productTypes, setProductTypes] = useState<ManufacturerProductType[]>([])

  useEffect(() => {
    if (manufacturerId === null) {
      setProductTypes([])
      return
    }
    let cancelled = false
    api
      .getManufacturer(manufacturerId)
      .then((detail) => {
        if (!cancelled) setProductTypes(detail.productTypes)
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

// Podtypy JEDNEJ serii/typu tego producenta, wskazanej nazwą — pusta lista, dopóki typ nie
// jest wybrany albo nie ma żadnych podtypów.
export function useManufacturerProductSubtypes(manufacturerName: string, productTypeName: string) {
  const { productTypes } = useManufacturerProductTypes(manufacturerName)
  const subtypes = productTypes.find((p) => p.name === productTypeName)?.subtypes ?? []
  return { subtypes }
}
