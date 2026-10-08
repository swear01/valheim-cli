import { readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
const pkg = JSON.parse(await readFile('package.json', 'utf8'));
const manifest = JSON.parse(await readFile('bridge-dist/manifest.json', 'utf8'));
const dll = await readFile('bridge-dist/ValheimCliBridge.dll');
const plugin = await readFile('bridge/Plugin.cs', 'utf8');
const project = await readFile('bridge/ValheimCliBridge.csproj', 'utf8');
if (pkg.version !== manifest.version || !plugin.includes(`"${pkg.version}")]`) || !project.includes(`<Version>${pkg.version}</Version>`)) throw new Error('Version mismatch');
if (createHash('sha256').update(dll).digest('hex') !== manifest.sha256) throw new Error('Bridge checksum mismatch');
