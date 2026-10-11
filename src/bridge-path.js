import { lstat, readdir, readFile } from 'node:fs/promises';
import { join } from 'node:path';
import { createHash } from 'node:crypto';

export async function installedBridge(profile, expected) {
  const plugins = join(profile, 'BepInEx', 'plugins');
  try {
    if ((await lstat(plugins)).isSymbolicLink()) throw new Error('Plugin paths must not contain symlinks');
  } catch (error) {
    if (error.code === 'ENOENT') return null;
    throw error;
  }
  const found = [];
  async function scan(folder) {
    for (const entry of await readdir(folder, { withFileTypes: true })) {
      const path = join(folder, entry.name);
      if (entry.isSymbolicLink()) throw new Error('Plugin paths must not contain symlinks');
      if (entry.isDirectory()) await scan(path);
      else if (entry.name.toLowerCase() === 'valheimclibridge.dll') {
        if (!entry.isFile()) throw new Error('Bridge DLL must be a regular file');
        found.push(path);
      }
    }
  }
  await scan(plugins);
  if (found.length > 1) throw new Error('Multiple bridge DLLs found. Exit Valheim and remove duplicate bridge copies before continuing.');
  if (!found.length) return null;
  if (createHash('sha256').update(await readFile(found[0])).digest('hex') !== expected) {
    throw new Error('Different bridge already installed. Exit Valheim and back up/remove only this plugin before upgrading.');
  }
  return found[0];
}
