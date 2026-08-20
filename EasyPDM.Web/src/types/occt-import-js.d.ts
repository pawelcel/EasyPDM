declare module "occt-import-js" {
  export interface OcctMesh {
    name: string
    attributes: {
      position: { array: number[] }
      normal?: { array: number[] }
    }
    index?: { array: number[] }
    color?: [number, number, number]
  }

  export interface OcctReadResult {
    success: boolean
    meshes: OcctMesh[]
  }

  export interface OcctInstance {
    ReadStepFile(fileBuffer: Uint8Array, params: null): OcctReadResult
    ReadIgesFile(fileBuffer: Uint8Array, params: null): OcctReadResult
  }

  export interface OcctInitOptions {
    locateFile?: (path: string) => string
  }

  export default function occtimportjs(options?: OcctInitOptions): Promise<OcctInstance>
}
