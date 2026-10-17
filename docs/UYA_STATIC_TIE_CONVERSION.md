# UYA static TIE conversion

`RatchetPs2.Sdk.StaticModelConverter` converts a validated
`StaticModelCanonicalAsset` into native UYA TIE model bytes, a 0x20-byte model
definition, and indexed8 PIF texture assets. The public entry point dispatches by
`GameId`; UYA is the only supported target today.

```csharp
var result = StaticModelConverter.Convert(
    GameId.UYA,
    imported.Asset,
    targetClassId,
    cancellationToken);
```

The returned material bindings use canonical content-addressed texture IDs. Two
materials with identical effective pixels share one encoded texture, while keeping
distinct native material slots and clamp state. `MaterialTexturePifBytes` is ordered
by native material index for direct use by the later archive-attachment stage.

## Supported semantics

- Indexed triangle primitives, positions, authored normals, generated face-normal
  fallback, and `TEXCOORD_0`.
- Base-color textures and factors, `OPAQUE`, `MASK`, and `BLEND` alpha.
- Repeat and clamp-to-edge sampler state. Mirrored repeat is mapped to repeat and
  reported as a diagnostic.
- glTF `doubleSided` material state. Single-sided materials use the retail
  always-cull packet distance; double-sided materials disable backface culling.
- Deterministic shared-palette optimization and indexed8 PIF output with the two
  downsampled material mip levels expected by UYA.
- Rectangular source images are resampled to a square native allocation before
  mip generation, matching the UYA static TIE texture layout.
- Connected meshes are converted into native triangle strips and split across
  packet groups rather than being restricted to one small-model batch.
- Exact position/normal/UV duplicates are welded before strip construction, so
  unindexed source meshes can share edges without crossing authored seams.
- Repeat-wrapped UV axes are shifted by whole texture periods when necessary to
  fit UYA's unsigned fixed-point coordinates without changing sampling.
- Stable source mesh/primitive/material mappings in the conversion result.
- Quantized model bounds and UYA vertex-normal remaps.

Metallic and roughness values are not represented by this UYA TIE path and produce
an approximation diagnostic. Non-white vertex colors are rejected because silently
discarding them would change supported appearance semantics.

## Current LOD policy and limits

The authored geometry is emitted only in native LOD0. LOD1 and LOD2 remain empty,
matching retail TIEs that do not provide reduced meshes. The result identifies this
policy as `lod0-only`; automatic simplification is intentionally deferred to the
later LOD task. Generated packets use the retail UYA VU unpack mode and stride
envelope. Connected triangles are emitted as longer strips, while an isolated
triangle uses a four-token strip with a degenerate final slot. Up to 15 strips are
batched into each packet while every generated vertex write remains inside the
packet's byte-addressed retail-observed `0xcc` VU boundary.
Generated strips normally use the retail-dominant four-token shape. Models that would overflow the
runtime packet or remap tables are rebuilt with a compact six-token shape. Single triangles are padded
with a degenerate tail, matching the paired retail VU loop.
Multi-strip vertex rows use retail reverse-strip storage order while each strip
retains forward vertex order.

Normal/RGBA direct-copy targets follow that physical upload-row order. Stored normal
vectors use the runtime's inward lighting-facing convention rather than glTF's
outward surface-normal convention.

The fixed 0x80-byte bounds block, RGBA cache size, quantized bounding sphere, and
packet-aligned normal/remap cache targets are emitted as in retail data. Remap groups
end only between render packets, explicitly zero packet-alignment slots, and remain
within the retail 64-qword work-record limit.
The header also reserves the retail two-word prefix plus one ambient-light word per
normal, aligned to four bytes, so placed instances receive model-sized lighting data.
Quantized positions are limited to the retail-observed signed range of ±32,640.

The converter preflights the complete input before returning output. Current native
limits are 16 referenced materials, 7,620 source triangles, a retail-style 512-entry
shared normal palette, 127 packets, and 31 normal/remap work groups. The final byte in
the 32-byte remap-group header remains zero for the runtime terminator.
4,096 pixels on either texture axis, 16-bit
fixed-point texture coordinates,
and a 64 MiB archive-safe model size. Triangle capacity depends on how efficiently
the source topology packs into strips. Failures identify the source mesh, primitive,
material, or texture where applicable; conversion is in memory, so failure and
cancellation cannot leave partial files.

Every generated TIE is reread by the SDK before it is returned. Header fields, the
LOD0 topology and empty higher LODs, quantized positions and UVs, normal remaps,
ambient-light size, material mappings,
and raw byte stability are checked. Generated PIFs are also reread.

Run the focused qualification with:

```bash
dotnet run --project tests/RatchetPs2.LevelTests/RatchetPs2.LevelTests.csproj -- --uya-static-tie
```
