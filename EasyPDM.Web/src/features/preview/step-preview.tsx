import { useEffect, useRef, useState } from "react"
import * as THREE from "three"
import { STLLoader } from "three/examples/jsm/loaders/STLLoader.js"

import { useLanguage } from "@/i18n/use-language"

let occtPromise: ReturnType<typeof loadOcct> | null = null

// occt-import-js jest ciężkim modułem WASM (parser OpenCASCADE) — inicjalizujemy go raz
// i współdzielimy Promise między wszystkimi podglądami 3D w tej samej sesji karty.
function loadOcct() {
  return import("occt-import-js").then(async ({ default: init }) => {
    const wasmUrl = (await import("occt-import-js/dist/occt-import-js.wasm?url")).default
    return init({ locateFile: () => wasmUrl })
  })
}

// STL to gotowa siatka (bez parametrycznej geometrii ani koloru) — three.js ma własny
// loader, nie potrzeba do tego OpenCASCADE/occt-import-js.
async function loadStlGeometries(buffer: Uint8Array): Promise<THREE.BufferGeometry[]> {
  const geometry = new STLLoader().parse(buffer.buffer as ArrayBuffer)
  if (!geometry.attributes.normal) geometry.computeVertexNormals()
  return [geometry]
}

// STEP/IGES mają parametryczną geometrię — occt-import-js (OpenCASCADE po WASM) triangularyzuje
// je do siatek, ewentualnie w kilku kawałkach (jedna bryła = jeden mesh w wyniku).
async function loadOcctGeometries(kind: "step" | "iges", buffer: Uint8Array): Promise<THREE.BufferGeometry[]> {
  occtPromise ??= loadOcct()
  const occt = await occtPromise
  const result = kind === "step" ? occt.ReadStepFile(buffer, null) : occt.ReadIgesFile(buffer, null)
  if (!result.success || result.meshes.length === 0) return []

  return result.meshes.map((mesh) => {
    const geometry = new THREE.BufferGeometry()
    geometry.setAttribute("position", new THREE.Float32BufferAttribute(mesh.attributes.position.array, 3))
    if (mesh.attributes.normal) {
      geometry.setAttribute("normal", new THREE.Float32BufferAttribute(mesh.attributes.normal.array, 3))
    } else {
      geometry.computeVertexNormals()
    }
    if (mesh.index) geometry.setIndex(mesh.index.array)
    return geometry
  })
}

function extensionOf(fileName: string): string {
  return fileName.toLowerCase().split(".").pop() ?? ""
}

// Statyczny podgląd bryły 3D pod stałym, izometrycznym kątem — bez OrbitControls i bez
// żadnej interakcji myszką (użytkownik chciał "jedynie podgląd", nie obracanie modelu).
// Obsługuje STEP/IGES (przez occt-import-js) i STL (przez wbudowany loader three.js) —
// wybór parsera na podstawie rozszerzenia w `fileName`.
function StepPreview({ url, fileName }: { url: string; fileName: string }) {
  const { t } = useLanguage()
  const containerRef = useRef<HTMLDivElement>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    let cancelled = false
    let renderer: THREE.WebGLRenderer | null = null

    async function run() {
      try {
        const ext = extensionOf(fileName)
        const res = await fetch(url)
        if (!res.ok) throw new Error(String(res.status))
        const buffer = new Uint8Array(await res.arrayBuffer())

        const geometries =
          ext === "stl"
            ? await loadStlGeometries(buffer)
            : await loadOcctGeometries(ext === "iges" || ext === "igs" ? "iges" : "step", buffer)
        if (cancelled) return
        if (geometries.length === 0) {
          setError(t("preview.stepParseFailed"))
          return
        }

        const container = containerRef.current
        if (!container) return

        const group = new THREE.Group()
        for (const geometry of geometries) {
          // Modele bez zdefiniowanego koloru (albo w formatach, które go w ogóle nie niosą,
          // jak STL) renderujemy w jednym, jasnym neutralnym kolorze — ciemniejszy zlewał
          // się z ciemnym tłem panelu.
          const material = new THREE.MeshStandardMaterial({ color: 0xe4e4e7, metalness: 0.05, roughness: 0.75 })
          group.add(new THREE.Mesh(geometry, material))

          // Czarne krawędzie (kontury ścian) — bez nich jasna, gładko cieniowana bryła
          // słabo się odróżnia od tła; to standardowy sposób na czytelny podgląd CAD.
          const edges = new THREE.EdgesGeometry(geometry, 30)
          group.add(new THREE.LineSegments(edges, new THREE.LineBasicMaterial({ color: 0x000000 })))
        }

        const box = new THREE.Box3().setFromObject(group)
        const center = box.getCenter(new THREE.Vector3())
        const size = box.getSize(new THREE.Vector3())
        const maxDim = Math.max(size.x, size.y, size.z) || 1

        const scene = new THREE.Scene()
        scene.add(group)
        scene.add(new THREE.AmbientLight(0xffffff, 0.8))
        const dirLight = new THREE.DirectionalLight(0xffffff, 1.1)
        dirLight.position.set(1, 2, 1.5)
        scene.add(dirLight)

        const width = container.clientWidth
        const height = container.clientHeight
        const aspect = width / height
        const viewSize = maxDim * 1.4
        const camera = new THREE.OrthographicCamera(
          (-viewSize * aspect) / 2,
          (viewSize * aspect) / 2,
          viewSize / 2,
          -viewSize / 2,
          0.01,
          maxDim * 10
        )
        const dir = new THREE.Vector3(1, 1, 1).normalize()
        camera.position.copy(center.clone().add(dir.multiplyScalar(maxDim * 3)))
        camera.lookAt(center)
        camera.up.set(0, 1, 0)

        renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true })
        renderer.setSize(width, height)
        renderer.setPixelRatio(window.devicePixelRatio)
        container.replaceChildren(renderer.domElement)
        renderer.render(scene, camera)

        setLoading(false)
      } catch {
        if (!cancelled) setError(t("preview.stepParseFailed"))
      }
    }

    run()

    return () => {
      cancelled = true
      renderer?.dispose()
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [url, fileName])

  return (
    <div className="relative h-full w-full">
      {loading && !error && (
        <div className="absolute inset-0 flex items-center justify-center text-sm text-muted-foreground">
          {t("preview.loading")}
        </div>
      )}
      {error && (
        <div className="absolute inset-0 flex items-center justify-center text-sm text-destructive">{error}</div>
      )}
      <div ref={containerRef} className="h-full w-full" />
    </div>
  )
}

export { StepPreview }
