import {
  RatchetPs2_Games_UYA_Gameplay_UyaLevelSettingsReader as uya,
  RatchetPs2_Games_UYA_Gameplay_UyaLevelSettings as UyaLevelSettings,
  RatchetPs2_Core_IO_BinarySpanReader as binary,
  RatchetPs2_Core_Games_GameId as gameId,
  RatchetPs2_Sdk_FrontendMapPackageBuilder as maps
} from '../bin/probe/index.js';
const parseUyaLevelSettings = uya.read;

const settings: UyaLevelSettings = parseUyaLevelSettings(new Uint8Array(0x84));
const coordinate: number = settings.chunkPlanes[0].point.x;
const trailing: Uint8Array = settings.trailingBytes;
parseUyaLevelSettings(new ArrayBuffer(0x84));
// @ts-expect-error SDK accepts binary buffers, not untyped number arrays.
parseUyaLevelSettings([1, 2, 3]);
// @ts-expect-error SDK declarations retain the C# field's numeric type.
const invalid: string = settings.deathHeight;

const optional: UyaLevelSettings | null = uya.tryRead(new Uint8Array(0));
const wide: bigint = binary.readUInt64LittleEndian(new Uint8Array(8), 0);
const view = binary.slice(new Uint8Array(8), 1, 4, 'test');
const nestedValue: number = binary.readInt32LittleEndian(view, 0);
const ownedCopy: Uint8Array = Uint8Array.from(view);
// @ts-expect-error ReadOnlySpan results expose a read-only indexed view.
view[0] = 1;
// @ts-expect-error Unregistered array-like objects are not SDK span views.
binary.readInt32LittleEndian({ length: 4, 0: 1 }, 0);

const packageResult = maps.buildLevelWad(new Uint8Array(), gameId.uya);
const packageBytes: Uint8Array = packageResult.packedBytes;
const firstPackagePath: string = packageResult.entries[0].path;
maps.buildUyaCustomMapZip(new ArrayBuffer(0));
// @ts-expect-error Map entry points accept binary buffers, not untyped number arrays.
maps.buildUyaCustomMapZip([1, 2, 3]);
