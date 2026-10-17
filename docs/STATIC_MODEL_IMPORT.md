# Static model GLB/glTF import

`StaticModelGltfImporter` converts a bounded glTF 2.0 static scene into canonical
`HFSM` version 1 bytes. The importer is byte-oriented and performs no filesystem or
network access, so the same API can run in .NET and in the generated TypeScript SDK.

## Host flow

1. Read the `.gltf` or `.glb` source within the host's import boundary.
2. Call `DiscoverDependencies(sourceBytes, sourceName)`.
3. Resolve each returned URI relative to the source directory, enforce the host's path
   policy, and supply its bytes as a `StaticModelImportFile`.
4. Call `Import(sourceBytes, sourceName, files, cancellationRequested)`.

The dependency list is normalized and sorted. Extra, duplicate, missing, absolute,
remote, query/fragment, backslash, empty-segment, and `.`/`..` paths are rejected.
URI percent escapes are supported for printable ASCII only. Data URIs are accepted
only in the documented glTF buffer and PNG image positions.

`Import` returns the structured canonical asset, its deterministic binary form and
SHA-256 content ID, plus source/dependency hashes for provenance. The optional callback
is polled at safe checkpoints and throws `OperationCanceledException` when it returns
`true`.

## Supported profile

- glTF 2.0 JSON and GLB 2.0 with an optional embedded BIN chunk.
- The selected scene's acyclic node hierarchy, with one parent per node and a maximum
  depth of 64. Matrix or TRS transforms are baked into positions and normals. Mirrored
  transforms reverse triangle winding.
- Triangle-list primitives with required `POSITION`, optional `NORMAL`, `TEXCOORD_0`,
  and `COLOR_0`, and optional indices. Other vertex attributes are ignored with an
  import diagnostic. Sparse accessors are not supported.
- Positions and normals use finite `FLOAT`. Texture coordinates and colors use finite
  `FLOAT` or normalized unsigned bytes/shorts. Indices use unsigned bytes, shorts, or
  integers.
- PBR base-color factor/PNG texture, metallic and roughness factors, alpha mode/cutoff,
  double-sided state, glTF sampler filtering, and wrap modes.
- Blender's optional `KHR_materials_specular` and `KHR_materials_ior` declarations are
  accepted and approximated through the base-color material path. Required extensions
  remain unsupported.
- External or embedded PNG images decoded to canonical RGBA8.

Canonical geometry remains in glTF's right-handed, metre-based coordinate space with
`+Y` up. Node transforms are flattened, but target-game coordinate and unit conversion
belongs to the native model composer.

The importer rejects non-triangle topology, extensions, morph targets, skins,
animations, cameras, sparse accessors, additional UV/color sets, tangent/joint/weight
attributes, non-PNG images, emissive/normal/occlusion/metallic-roughness textures, and
other material features outside this profile.

## Bounds and allocation

All public limits are declared in `StaticModelGltfLimits`: source/dependency/canonical
bytes, total sidecar bytes, buffers, buffer views, accessors, meshes, primitives, nodes,
materials, textures, images, vertices, indices, image dimensions/pixels, URI length,
and hierarchy depth. Buffer ranges, accessor element size/stride/alignment, component
conversion, references, decoded image size, and final canonical size are validated
before the corresponding allocation or output is accepted.

`HFSM` is an SDK interchange format, not a native game model. Native TIE conversion
must consume the canonical asset and perform its own target-limit preflight.
