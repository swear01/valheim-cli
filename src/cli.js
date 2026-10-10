import { readFile, writeFile, realpath, stat, mkdir, copyFile, lstat } from 'node:fs/promises';
import { constants } from 'node:fs';
import { resolve, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { request } from './client.js';

export const help = `valheim install --profile <Gale profile folder>
valheim status --profile <folder> [--port 28761]
valheim players --profile <folder> [--port 28761]
valheim observe --profile <folder> [--image <new.png>]
valheim input --profile <folder> --confirm [--keys W,Shift] [--buttons left,right,middle]
  [--ms 200] [--mouse-x 120 --mouse-y -30] [--scroll 1] [--pointer-x 0.5 --pointer-y 0.5]
valheim mouse --profile <folder> --confirm [--mouse-x 120 --mouse-y -30 | --pointer-x 0.5 --pointer-y 0.5] [--scroll 1]
valheim stop --profile <folder>
valheim teleport <x> <y> <z> --profile <folder> --confirm [--port 28761] [--request-id <UUID>]
Coordinates are world X, Y (height), Z. Teleport requires host + AllowTeleport=true.
Input requires Windows + AllowControl=true + foreground game. F12 revokes control.
Input lasts 50..5000 ms and returns started; poll status until inputActive=false.
Pointer coordinates are normalized from top left. Observe saves PNG only with --image.
All results are JSON. No arbitrary console commands. No automatic retries.`;

const inputOptions = { '--keys': 'keys', '--buttons': 'buttons', '--ms': 'durationMs', '--mouse-x': 'mouseX', '--mouse-y': 'mouseY', '--scroll': 'scroll', '--pointer-x': 'pointerX', '--pointer-y': 'pointerY' };
const allowedKeys = new Set([...Array.from('ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789'), 'SPACE', 'SHIFT', 'CONTROL', 'TAB', 'ESCAPE', 'ENTER', 'BACKSPACE', 'LEFT', 'UP', 'RIGHT', 'DOWN']);
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));

export function validateInput(input, operation = 'input') {
  const keys = input.keys?.split(',') ?? [];
  const buttons = input.buttons?.split(',') ?? [];
  if (keys.length > 8 || keys.some(k => !allowedKeys.has(k.toUpperCase())) || new Set(keys.map(k => k.toUpperCase())).size !== keys.length || buttons.some(b => !['left', 'right', 'middle'].includes(b)) || new Set(buttons).size !== buttons.length) throw new Error('Unsupported or repeated input');
  if (keys.some(k => k.toUpperCase() === 'CONTROL') && keys.some(k => !['CONTROL', 'SPACE'].includes(k.toUpperCase()))) throw new Error('Control may only be combined with Space (dodge)');
  if (operation === 'mouse' ? input.keys !== undefined || input.buttons !== undefined || input.durationMs !== undefined : !Number.isInteger(input.durationMs) || input.durationMs < 50 || input.durationMs > 5000) throw new Error('Mouse takes motion only; input requires duration 50..5000 ms');
  if (['mouseX', 'mouseY', 'scroll'].some(k => input[k] !== undefined && (!Number.isInteger(input[k]) || Math.abs(input[k]) > (k === 'scroll' ? 10 : 2000)))) throw new Error('Input motion outside bounds');
  if ((input.pointerX === undefined) !== (input.pointerY === undefined) || ['pointerX', 'pointerY'].some(k => input[k] !== undefined && (!Number.isFinite(input[k]) || input[k] < 0 || input[k] > 1))) throw new Error('Pointer requires normalized x/y in 0..1');
  if (input.pointerX !== undefined && (input.mouseX !== undefined || input.mouseY !== undefined)) throw new Error('Choose pointer or relative mouse motion');
  if (!keys.length && !buttons.length && !input.mouseX && !input.mouseY && !input.scroll && input.pointerX === undefined) throw new Error('Empty input');
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
  if (!['install', 'status', 'players', 'teleport', 'observe', 'input', 'mouse', 'stop'].includes(command)) throw new Error('Unknown command. Run valheim --help');
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
        if (['keys', 'buttons'].includes(field)) input[field] = next;
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
  if (!['teleport', 'input', 'mouse'].includes(command) && (values.length || confirm || id)) throw new Error('Unexpected arguments');
  if (!['input', 'mouse'].includes(command) && Object.keys(input).length || command !== 'observe' && imagePath) throw new Error('Unexpected arguments');
  if (command === 'input' || command === 'mouse') {
    if (values.length || !confirm) throw new Error('input requires --confirm and named input options');
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
