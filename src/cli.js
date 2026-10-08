import { readFile, realpath, stat, mkdir, copyFile, lstat } from 'node:fs/promises';
import { constants } from 'node:fs';
import { resolve, join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { request } from './client.js';

export const help = `valheim install --profile <Gale profile folder>
valheim status --profile <folder> [--port 28761]
valheim players --profile <folder> [--port 28761]
valheim teleport <x> <y> <z> --profile <folder> --confirm [--port 28761] [--request-id <UUID>]
Coordinates are world X, Y (height), Z. Teleport requires host + AllowTeleport=true.
All results are JSON. No arbitrary console commands. No automatic retries.`;

async function safeDirectory(path) {
  await mkdir(path, { recursive: true });
  if ((await lstat(path)).isSymbolicLink() || await realpath(path) !== resolve(path)) {
    throw new Error('Profile/plugin path must not contain symlinks');
  }
}

export async function install(profile, artifactRoot = fileURLToPath(new URL('../bridge-dist/', import.meta.url))) {
  profile = await realpath(profile);
  const plugins = join(profile, 'BepInEx', 'plugins');
  if (!(await stat(join(profile, 'BepInEx', 'core', 'BepInEx.dll'))).isFile()) throw new Error('Choose an existing BepInEx profile');
  const manifest = JSON.parse(await readFile(join(artifactRoot, 'manifest.json'), 'utf8'));
  const source = join(artifactRoot, 'ValheimCliBridge.dll');
  const expected = manifest.sha256;
  if (!/^[a-f0-9]{64}$/.test(expected)) throw new Error('Invalid bridge manifest');
  if (createHash('sha256').update(await readFile(source)).digest('hex') !== expected) throw new Error('Bridge checksum mismatch');
  const targetDir = join(plugins, 'swear01-ValheimCliBridge');
  await safeDirectory(join(profile, 'BepInEx'));
  await safeDirectory(plugins);
  await safeDirectory(targetDir);
  const target = join(targetDir, 'ValheimCliBridge.dll');
  try {
    await copyFile(source, target, constants.COPYFILE_EXCL);
  } catch (error) {
    if (error.code !== 'EEXIST') throw error;
    if ((await lstat(target)).isSymbolicLink()) throw new Error('Existing bridge must not be a symlink');
    if (createHash('sha256').update(await readFile(target)).digest('hex') !== expected) {
      throw new Error('Different bridge already installed. Exit Valheim and back up/remove only this plugin before upgrading.');
    }
  }
  return { ok: true, state: 'installed', path: target, version: manifest.version,
    next: 'Restart Valheim. The plugin creates its local token; teleport is disabled by default.' };
}

export async function main(args) {
  if (args.length === 0 || args.length === 1 && ['--help', 'help'].includes(args[0])) return { ok: true, help };
  const command = args.shift();
  if (!['install', 'status', 'players', 'teleport'].includes(command)) throw new Error('Unknown command. Run valheim --help');
  const values = [];
  let profile;
  let port = 28761;
  let confirm = false;
  let id;
  const seen = new Set();
  while (args.length) {
    const value = args.shift();
    if (['--profile', '--port', '--confirm', '--request-id'].includes(value)) {
      if (seen.has(value)) throw new Error(`Duplicate option: ${value}`);
      seen.add(value);
      if (value === '--confirm') { confirm = true; continue; }
      const next = args.shift();
      if (!next || next.startsWith('--')) throw new Error(`Missing value for ${value}`);
      if (value === '--profile') profile = resolve(next);
      else if (value === '--request-id') {
        if (!/^[a-f0-9]{8}(-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i.test(next)) throw new Error('Invalid request UUID');
        id = next;
      }
      else {
        if (!/^\d+$/.test(next)) throw new Error('Invalid port');
        port = Number(next);
      }
    } else if (value.startsWith('--')) throw new Error(`Unknown option: ${value}`);
    else values.push(value);
  }
  if (!profile) throw new Error('--profile is required');
  if (command !== 'teleport' && (values.length || confirm || id)) throw new Error('Unexpected arguments');
  if (command === 'install') {
    if (seen.has('--port')) throw new Error('Configure the port in the plugin config after first launch');
    return install(profile);
  }
  let coordinates = {};
  if (command === 'teleport') {
    if (values.length !== 3 || !confirm) throw new Error('teleport requires x y z and --confirm');
    const numbers = values.map(value => /^-?\d+(\.\d+)?$/.test(value) ? Number(value) : NaN);
    if (!numbers.every(Number.isFinite) || Math.abs(numbers[0]) > 10500 || Math.abs(numbers[2]) > 10500 || numbers[1] < -1000 || numbers[1] > 5000) {
      throw new Error('Coordinates outside allowed world bounds');
    }
    coordinates = Object.fromEntries(['x', 'y', 'z'].map((axis, index) => [axis, numbers[index]]));
  }
  const token = (await readFile(join(profile, 'BepInEx', 'config', 'swear01.ValheimCliBridge.token'), 'utf8')).trim();
  return request({ port, token, operation: command, id, ...coordinates });
}
