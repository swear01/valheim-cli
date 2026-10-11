import { lstat, readdir, readFile, realpath, stat } from 'node:fs/promises';
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
  const found = new Map();
  async function scan(folder, parents = new Set()) {
    // An incomplete scan must fail: skipping unreadable folders can install a duplicate.
    const resolved = await realpath(folder);
    if (parents.has(resolved)) return;
    const ancestors = new Set([...parents, resolved]);
    for (const entry of await readdir(folder, { withFileTypes: true })) {
      const path = join(folder, entry.name);
      if (entry.isSymbolicLink()) {
        if (entry.name.toLowerCase() === 'valheimclibridge.dll') throw new Error('Bridge DLL must not be a symlink');
        let target;
        try { target = await stat(path); }
        catch (error) { if (error.code !== 'ENOENT') throw error; }
        if (target?.isDirectory()) await scan(path, ancestors);
        continue;
      }
      if (entry.isDirectory()) await scan(path, ancestors);
      else if (entry.name.toLowerCase() === 'valheimclibridge.dll') {
        if (!entry.isFile()) throw new Error('Bridge DLL must be a regular file');
        found.set(await realpath(path), path);
      }
    }
  }
  await scan(plugins);
  if (found.size > 1) throw new Error('Multiple bridge DLLs found. Exit Valheim and remove duplicate bridge copies before continuing.');
  const [current] = found.values();
  if (!current) return null;
  if (createHash('sha256').update(await readFile(current)).digest('hex') !== expected) {
    throw new Error('Different bridge already installed. Exit Valheim and back up/remove only this plugin before upgrading.');
  }
  return current;
}
