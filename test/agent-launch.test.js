import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { launchAgent } from '../src/agent-launch.js';

test('Agent launcher rejects unsupported platforms and invalid Windows paths', async () => {
  if (process.platform !== 'win32') {
    await assert.rejects(launchAgent('.', 'valheim.exe'), /requires Windows/);
  } else {
    await assert.rejects(launchAgent('.', 'bad"path.exe'), /existing game/);
    await assert.rejects(launchAgent('.', 'missing-valheim.exe'), { code: 'ENOENT' });
  }
});

test('Windows launcher refuses disabled permissions before creating any desktop or process', { skip: process.platform !== 'win32' }, async () => {
  const root = await mkdtemp(join(tmpdir(), 'valheim agent ü '));
  try {
    await mkdir(join(root, 'BepInEx/core'), { recursive: true });
    await mkdir(join(root, 'BepInEx/config'), { recursive: true });
    await writeFile(join(root, 'valheim.exe'), 'never executable');
    await writeFile(join(root, 'BepInEx/core/BepInEx.Preloader.dll'), 'fixture');
    const script = fileURLToPath(new URL('../src/windows-agent-launch.ps1', import.meta.url));
    for (const config of ['[Permissions]\nAllowControl = false\n', '[Bridge]\nEnabled = false\n[Permissions]\nAllowControl = true\n']) {
      await writeFile(join(root, 'BepInEx/config/swear01.ValheimCliBridge.cfg'), config);
      const child = spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', script, '-Game', join(root, 'valheim.exe'), '-Profile', root], { windowsHide: true, encoding: 'utf8', timeout: 10000 });
      assert.equal(child.status, 1, child.error?.message);
      assert.match(child.stderr, /Enable the bridge and AllowControl/);
    }
  } finally { await rm(root, { force: true, recursive: true }); }
});
