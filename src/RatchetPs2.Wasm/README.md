# RatchetPs2.Wasm

Browser-facing WASM bridge for `RatchetPs2.Core`.

## What this project produces

After publishing:

```bash
dotnet publish src/RatchetPs2.Wasm/RatchetPs2.Wasm.csproj -c Release -o src/RatchetPs2.Wasm/dist
```

the important browser assets are under:

- `src/RatchetPs2.Wasm/dist/wwwroot/_framework/`

The source-controlled JS/TS wrapper contract is:

- `src/RatchetPs2.Wasm/ratchetps2-wasm.js`
- `src/RatchetPs2.Wasm/ratchetps2-wasm.d.ts`

## Vite / React consumption

Copy these into your consuming app:

1. copy `src/RatchetPs2.Wasm/dist/wwwroot/_framework` into your Vite app's public assets, for example:
   - `your-app/public/ratchetps2/_framework`
2. copy these wrapper files into your app source or package them:
   - `ratchetps2-wasm.js`
   - `ratchetps2-wasm.d.ts`

If you place the wrapper next to the `_framework` folder like this:

```text
your-app/
  public/
    ratchetps2/
      _framework/
  src/
    lib/
      ratchetps2-wasm.js
      ratchetps2-wasm.d.ts
```

then adjust the wrapper asset base path or place the wrapper in a location where `./_framework/blazor.webassembly.js` resolves correctly.

The current wrapper resolves runtime assets relative to `import.meta.url` by default.

For a Vite app, it is usually easier to explicitly configure the public asset location:

```ts
import { initializeRatchetPs2Wasm, convertPifToPngBlob } from './ratchetps2-wasm';

await initializeRatchetPs2Wasm({
  assetBaseUrl: '/ratchetps2/',
});

const response = await fetch('/textures/minimap.pif');
const buffer = await response.arrayBuffer();
const blob = await convertPifToPngBlob(buffer, { pngFormat: 'indexed4' });
```

If your published runtime files are served from `public/ratchetps2/_framework`, then use:

- `assetBaseUrl: '/ratchetps2/'`

because the wrapper will then request:

- `/ratchetps2/_framework/blazor.webassembly.js`

and the rest of the Blazor boot resources such as:

- `/ratchetps2/_framework/dotnet.js`
- `/ratchetps2/_framework/blazor.boot.json`
- `/ratchetps2/_framework/*.wasm`

### Important Vite note

Do **not** pass `public/...` to `fetch()` or to `assetBaseUrl` in the browser.

In Vite, files inside `public/` are served from the web root.

So if the files are on disk here:

```text
your-app/public/ratchetps2/_framework/...
your-app/public/pif/texture.pif
```

then browser URLs should be:

```ts
await initializeRatchetPs2Wasm({ assetBaseUrl: '/ratchetps2/' });
const response = await fetch('/pif/texture.pif');
```

not:

```ts
assetBaseUrl: 'public/ratchetps2/'
fetch('pif/texture.pif')
```

## Example usage

```ts
import { convertPifToPngBlob } from './ratchetps2-wasm';

export async function convert(file: File) {
  const input = await file.arrayBuffer();

  const blob = await convertPifToPngBlob(input, {
    pngFormat: 'indexed8',
    doubleAlpha: false,
  });

  return blob;
}
```

If you need raw bytes instead, `convertPifToPng(...)` still returns a `Uint8Array`.

For bulk conversion, use `convertPifListToPng(...)` to send an array of PIF buffers in a single WASM call and receive an array of PNG byte arrays back. This avoids repeated JS↔WASM invocation overhead when converting many textures.

```ts
import { convertPifListToPng } from './ratchetps2-wasm';

const pngImages = await convertPifListToPng([pifA, pifB, pifC], {
  pngFormat: 'rgba32',
  doubleAlpha: false,
});
```

If you want blobs directly, `convertPifListToPngBlobs(...)` returns `Blob[]`.

## Current exported API

- `getApiVersion(): Promise<string>`
- `convertPifToPng(input, options?): Promise<Uint8Array>`
- `convertPifListToPng(inputs, options?): Promise<Uint8Array[]>`
- `convertPifListToPngBlobs(inputs, options?): Promise<Blob[]>`

Where `options.pngFormat` can be:

- `rgba32`
- `indexed8`
- `indexed4`