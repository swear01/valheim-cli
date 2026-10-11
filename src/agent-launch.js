import { spawn } from 'node:child_process';
import { readFile, stat } from 'node:fs/promises';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { installedBridge } from './bridge-path.js';

export async function launchAgent(profile, game) {
  if (process.platform !== 'win32') throw new Error('launch-agent currently requires Windows');
  for (const path of [game, profile]) {
    if (typeof path !== 'string' || /["\u0000-\u001f]/u.test(path)) throw new Error('Choose an existing game executable and BepInEx profile');
  }
  if (!(await stat(game)).isFile() || !(await stat(join(profile, 'BepInEx/core/BepInEx.Preloader.dll'))).isFile()) throw new Error('Choose an existing game executable and BepInEx profile');
  const manifest = JSON.parse(await readFile(new URL('../bridge-dist/manifest.json', import.meta.url), 'utf8'));
  if (!await installedBridge(profile, manifest.sha256)) throw new Error('Install this CLI version of the bridge before launch-agent');
  const script = fileURLToPath(new URL('./windows-agent-launch.ps1', import.meta.url));
  return new Promise((resolve, reject) => {
    const child = spawn('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', script, '-Game', game, '-Profile', profile], { windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
    let output = '', error = '';
    child.stdout.setEncoding('utf8').on('data', data => { output += data; });
    child.stderr.setEncoding('utf8').on('data', data => { error += data; });
    child.on('error', reject);
    child.on('close', code => {
      if (code !== 0) return reject(new Error(error.trim() || `Agent launcher exited ${code}`));
      try { resolve(JSON.parse(output.trim())); } catch { reject(new Error('Agent launcher returned invalid JSON')); }
    });
  });
}
