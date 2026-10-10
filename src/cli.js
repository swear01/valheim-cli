import { readFile, writeFile, realpath, stat, mkdir, copyFile, lstat } from 'node:fs/promises';
import { constants } from 'node:fs';
import { resolve, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { request } from './client.js';

export const help = `valheim install --profile <Gale profile folder>
valheim status --profile <folder> [--port 28761]
valheim players --profile <folder>
valheim observe --profile <folder> [--image <new.png>]
valheim input --profile <folder> --confirm [--move-x -1..1] [--move-z -1..1]
  [--actions attack,secondary,block,jump,crouch,run,dodge] [--ms 200]
valheim look --profile <folder> --confirm [--yaw <degrees>] [--pitch <degrees>]
valheim action --profile <folder> --confirm --action interact|slot|inventory|build-menu|hide|guardian|place|rotate
  [--slot 1..8] [--scroll -10..10]
valheim ui --profile <folder> --confirm --ui-action click|scroll
  --pointer-x 0..1 --pointer-y 0..1 [--button left|right|middle] [--scroll -10..10]
valheim stop --profile <folder>
valheim teleport <x> <y> <z> --profile <folder> --confirm [--request-id <UUID>]
Game-internal controls only. No OS keyboard/mouse injection or console execution.
Input requires AllowControl=true + focused game. F12 revokes control.
Movement is relative to player look. Input lasts 50..5000 ms; poll status and observe.
Look takes degree deltas: positive yaw turns right, positive pitch looks down.
UI coordinates start at top left. UI events are dispatched inside the game.
Teleport coordinates are world X, Y (height), Z; requires host + AllowTeleport=true.
All results are JSON. No automatic write retries.`;

const inputOptions = { '--actions': 'actions', '--move-x': 'moveX', '--move-z': 'moveZ', '--ms': 'durationMs', '--yaw': 'yaw', '--pitch': 'pitch', '--action': 'action', '--slot': 'slot', '--ui-action': 'uiAction', '--button': 'button', '--scroll': 'scroll', '--pointer-x': 'pointerX', '--pointer-y': 'pointerY' };
const controlCommands = ['input', 'look', 'action', 'ui'];
const stringFields = new Set(['actions', 'action', 'uiAction', 'button']);
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));

export function validateInput(input, operation = 'input') {
  const fields = { input: ['actions', 'moveX', 'moveZ', 'durationMs'], look: ['yaw', 'pitch'], action: ['action', 'slot', 'scroll'], ui: ['uiAction', 'pointerX', 'pointerY', 'button', 'scroll'] }[operation];
  if (!fields || Object.keys(input).some(k => !fields.includes(k))) throw new Error('Unexpected action fields');
  const bounded = (n, max) => Number.isFinite(n) && Math.abs(n) <= max;
  if (operation === 'input') {
    const actions = input.actions?.split(',') ?? [];
    if (actions.some(a => !['attack', 'secondary', 'block', 'jump', 'crouch', 'run', 'dodge'].includes(a)) || new Set(actions).size !== actions.length) throw new Error('Unsupported or repeated game action');
    if (!Number.isInteger(input.durationMs) || input.durationMs < 50 || input.durationMs > 5000) throw new Error('Input requires duration 50..5000 ms');
    if (!bounded(input.moveX ?? 0, 1) || !bounded(input.moveZ ?? 0, 1)) throw new Error('Movement must be in -1..1');
    if (!input.moveX && !input.moveZ && !actions.length) throw new Error('Empty input');
  } else if (operation === 'look') {
    if (input.yaw === undefined && input.pitch === undefined || !bounded(input.yaw ?? 0, 180) || !bounded(input.pitch ?? 0, 180)) throw new Error('Look requires degree deltas in -180..180');
  } else if (operation === 'action') {
    if (!['interact', 'slot', 'inventory', 'build-menu', 'hide', 'guardian', 'place', 'rotate'].includes(input.action)) throw new Error('Unknown game action');
    if (input.action === 'slot' ? !Number.isInteger(input.slot) || input.slot < 1 || input.slot > 8 : input.slot !== undefined) throw new Error('Slot requires 1..8 and only applies to slot');
    if (input.action === 'rotate' ? !Number.isInteger(input.scroll) || !input.scroll || Math.abs(input.scroll) > 10 : input.scroll !== undefined) throw new Error('Rotate requires nonzero scroll in -10..10');
  } else {
    if (!['click', 'scroll'].includes(input.uiAction)) throw new Error('Unknown UI action');
    if (['pointerX', 'pointerY'].some(k => !bounded(input[k], 1) || input[k] < 0)) throw new Error('UI requires normalized pointer x/y in 0..1');
    if (input.button !== undefined && !['left', 'right', 'middle'].includes(input.button)) throw new Error('Unknown UI button');
    if (input.uiAction === 'scroll' ? !Number.isInteger(input.scroll) || !input.scroll || Math.abs(input.scroll) > 10 : input.scroll !== undefined) throw new Error('Scroll requires nonzero -10..10');
  }
}

export async function observe(connection, imagePath) {
  for (let attempt = 0; attempt < 20; attempt++) {
    const { image, ...result } = await request({ ...connection, operation: 'observe' });
    if (image) {
      if (imagePath) {
        const png = Buffer.from(image, 'base64');
        if (!png.subarray(0, 8).equals(Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]))) throw new Error('Invalid screenshot PNG');
        await writeFile(imagePath, png, { flag: 'wx' });
        result.imagePath = imagePath;
      }
      return result;
    }
    if (result.captureError) throw new Error(result.captureError);
    if (result.state !== 'capture_pending') throw new Error('Bridge returned no screenshot');
    await delay(50);
  }
  throw new Error('Game did not render a screenshot; bring Valheim to the foreground and try observe again');
}

async function safeDirectory(path) {
  await mkdir(path, { recursive: true });
  if ((await lstat(path)).isSymbolicLink()) {
    throw new Error('Profile/plugin path must not contain symlinks');
  }
}

export async function install(profile, artifactRoot = fileURLToPath(new URL('../bridge-dist/', import.meta.url))) {
  try {
    profile = await realpath(profile);
    if (!(await stat(join(profile, 'BepInEx', 'core', 'BepInEx.dll'))).isFile()) throw new Error('Choose an existing BepInEx profile');
  } catch (error) {
    if (['ENOENT', 'ENOTDIR'].includes(error.code)) throw new Error('Choose an existing BepInEx profile');
    throw error;
  }
  const plugins = join(profile, 'BepInEx', 'plugins');
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
  if (!['install', 'status', 'players', 'teleport', 'observe', 'stop', ...controlCommands].includes(command)) throw new Error('Unknown command. Run valheim --help');
  const values = [];
  let profile;
  let port = 28761;
  let confirm = false;
  let id;
  let imagePath;
  const input = {};
  const seen = new Set();
  while (args.length) {
    const value = args.shift();
    if (['--profile', '--port', '--confirm', '--request-id', '--image', ...Object.keys(inputOptions)].includes(value)) {
      if (seen.has(value)) throw new Error(`Duplicate option: ${value}`);
      seen.add(value);
      if (value === '--confirm') { confirm = true; continue; }
      const next = args.shift();
      if (!next || next.startsWith('--')) throw new Error(`Missing value for ${value}`);
      if (value === '--profile') profile = resolve(next);
      else if (value === '--image') imagePath = resolve(next);
      else if (value in inputOptions) {
        const field = inputOptions[value];
        if (stringFields.has(field)) input[field] = next;
        else {
          if (!/^-?\d+(\.\d+)?$/.test(next)) throw new Error(`Invalid number for ${value}`);
          input[field] = Number(next);
        }
      }
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
  if (!['teleport', ...controlCommands].includes(command) && (values.length || confirm || id)) throw new Error('Unexpected arguments');
  if (!controlCommands.includes(command) && Object.keys(input).length || command !== 'observe' && imagePath) throw new Error('Unexpected arguments');
  if (controlCommands.includes(command)) {
    if (values.length || !confirm) throw new Error('Control commands require --confirm and named options');
    if (command === 'input') input.durationMs ??= 200;
    validateInput(input, command);
  }
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
  let token;
  try {
    token = (await readFile(join(profile, 'BepInEx', 'config', 'swear01.ValheimCliBridge.token'), 'utf8')).trim();
  } catch (error) {
    if (error.code === 'ENOENT') throw new Error('Bridge token not found. Start Valheim with the plugin installed, then try again.');
    throw error;
  }
  if (command === 'observe') return observe({ port, token }, imagePath);
  return request({ port, token, operation: command, id, ...coordinates, ...input });
}
