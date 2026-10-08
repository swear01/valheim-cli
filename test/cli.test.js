import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, readFile, rm, symlink } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createHash } from 'node:crypto';
import { main, install } from '../src/cli.js';

test('CLI rejects unsafe or ambiguous inputs before connecting', async () => {
  for (const args of [ ['spawn'], ['teleport', '0', '2', '3', '--profile', '.'],
    ['teleport', '0;spawn', '2', '3', '--profile', '.', '--confirm'],
    ['teleport', 'NaN', '2', '3', '--profile', '.', '--confirm'],
    ['teleport', '0', '999999', '3', '--profile', '.', '--confirm'],
    ['status', '--profile', '.', '--profile', '.'], ['status', '--profile', '.', '--confirm'],
    ['install', '--profile', '.', '--port', '28761'], ['status', '--profile'] ]) {
    await assert.rejects(main(args));
  }
  assert.match((await main(['--help'])).help, /--confirm/);
  await assert.rejects(main(['status', '--profile', 'does-not-exist']), /Start Valheim/);
});

test('Installer verifies checksum, preserves other files/configs and refuses replacement', async () => {
  const root = await mkdtemp(join(tmpdir(), 'valheim-cli-'));
  try {
    const profile = join(root, 'profile');
    const artifact = join(root, 'artifact');
    await assert.rejects(install(profile, artifact), /existing BepInEx profile/);
    await mkdir(join(profile, 'BepInEx', 'core'), { recursive: true });
    await mkdir(join(profile, 'BepInEx', 'config'), { recursive: true });
    await mkdir(artifact);
    await writeFile(join(profile, 'BepInEx', 'core', 'BepInEx.dll'), 'fixture');
    await writeFile(join(profile, 'BepInEx', 'config', 'other.cfg'), 'preserve');
    const dll = Buffer.from('MZ-fixture');
    await writeFile(join(artifact, 'ValheimCliBridge.dll'), dll);
    await writeFile(join(artifact, 'manifest.json'), JSON.stringify({ version: '0.1.0', sha256: createHash('sha256').update(dll).digest('hex') }));
    const first = await install(profile, artifact);
    assert.equal(first.state, 'installed');
    assert.equal((await install(profile, artifact)).state, 'installed');
    assert.equal(await readFile(join(profile, 'BepInEx', 'config', 'other.cfg'), 'utf8'), 'preserve');
    await writeFile(first.path, 'different');
    await assert.rejects(install(profile, artifact), /Different bridge/);
    await rm(first.path);
    await symlink(join(profile, 'BepInEx', 'config', 'other.cfg'), first.path);
    await assert.rejects(install(profile, artifact), /symlink/);
    await writeFile(join(artifact, 'ValheimCliBridge.dll'), 'tampered');
    await assert.rejects(install(profile, artifact), /checksum/);
  } finally { await rm(root, { recursive: true, force: true }); }
});
