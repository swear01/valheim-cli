import { readFile, mkdir, writeFile, copyFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
const pkg = JSON.parse(await readFile('package.json', 'utf8'));
const source = 'bridge/bin/Release/net48/ValheimCliBridge.dll';
const dll = await readFile(source);
if (dll.subarray(0, 2).toString() !== 'MZ') throw new Error('Invalid bridge DLL');
await mkdir('bridge-dist', { recursive: true });
await copyFile(source, 'bridge-dist/ValheimCliBridge.dll');
await writeFile('bridge-dist/manifest.json', JSON.stringify({ version: pkg.version, guid: 'swear01.ValheimCliBridge', sha256: createHash('sha256').update(dll).digest('hex'), runtimeVerified: false }, null, 2) + '\n');
