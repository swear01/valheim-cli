import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, readFile, rm, symlink, rename, realpath, chmod } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createHash } from 'node:crypto';
import { main, install } from '../src/cli.js';
import { installedBridge } from '../src/bridge-path.js';

test('CLI rejects unsafe or ambiguous inputs before connecting', async () => {
  for (const args of [ ['spawn'], ['launch-agent', '--profile', '.', '--confirm'], ['launch-agent', '--profile', '.', '--game', 'valheim.exe'], ['status', '--profile', '.', '--game', 'valheim.exe'], ['launch-agent', '--profile', '.', '--game', 'valheim.exe', '--port', '28761', '--confirm'], ['teleport', '0', '2', '3', '--profile', '.'],
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

test('Standalone and manager plugin layouts reuse one verified bridge and refuse duplicate copies', async () => {
  const root = await realpath(await mkdtemp(join(tmpdir(), 'valheim standalone ü ')));
  try {
    const profile = join(root, 'Valheim');
    const artifact = join(root, 'artifact');
    const plugins = join(profile, 'BepInEx', 'plugins');
    await mkdir(join(profile, 'BepInEx', 'core'), { recursive: true });
    await writeFile(join(profile, 'BepInEx', 'core', 'BepInEx.dll'), 'fixture');
    await mkdir(artifact);
    const dll = Buffer.from('MZ-fixture');
    const hash = createHash('sha256').update(dll).digest('hex');
    await writeFile(join(artifact, 'ValheimCliBridge.dll'), dll);
    await writeFile(join(artifact, 'manifest.json'), JSON.stringify({ version: '0.4.0', sha256: hash }));
    let current = (await install(profile, artifact)).path;
    for (const folder of ['', 'AnyManager/Author-Package/BepInEx/plugins']) {
      const directory = join(plugins, folder);
      await mkdir(directory, { recursive: true });
      const target = join(directory, 'ValheimCliBridge.dll');
      await rename(current, target);
      current = target;
      assert.equal(await installedBridge(profile, hash), current);
      assert.equal((await install(profile, artifact)).path, current);
    }
    const external = join(root, 'linked-mods');
    await mkdir(external);
    const link = join(plugins, 'linked-mods');
    const linkType = process.platform === 'win32' ? 'junction' : 'dir';
    await symlink(external, link, linkType);
    await symlink(plugins, join(external, 'cycle'), linkType);
    assert.equal((await install(profile, artifact)).path, current);
    const linkedDll = join(external, 'ValheimCliBridge.dll');
    await rename(current, linkedDll);
    if (process.platform !== 'win32' && process.getuid() !== 0) {
      await chmod(external, 0);
      try {
        await assert.rejects(install(profile, artifact), { code: 'EACCES' });
        await assert.rejects(installedBridge(profile, hash), { code: 'EACCES' });
      } finally { await chmod(external, 0o700); }
      await assert.rejects(readFile(join(plugins, 'swear01-ValheimCliBridge', 'ValheimCliBridge.dll')), { code: 'ENOENT' });
    }
    assert.equal((await install(profile, artifact)).path, join(link, 'ValheimCliBridge.dll'));
    const alias = join(plugins, 'linked-mods-alias');
    await symlink(external, alias, linkType);
    assert.equal(await realpath((await install(profile, artifact)).path), await realpath(linkedDll));
    assert.equal(await realpath(await installedBridge(profile, hash)), await realpath(linkedDll));
    await rm(alias);
    await rename(linkedDll, current);
    await writeFile(linkedDll, dll);
    await assert.rejects(install(profile, artifact), /Multiple bridge DLLs/);
    await rm(linkedDll);
    const duplicate = join(plugins, 'ValheimCliBridge.dll');
    await writeFile(duplicate, dll);
    await assert.rejects(install(profile, artifact), /Multiple bridge DLLs/);
    await assert.rejects(installedBridge(profile, hash), /Multiple bridge DLLs/);
    await rm(duplicate);
    await writeFile(current, 'different');
    await assert.rejects(install(profile, artifact), /Different bridge/);
    await assert.rejects(installedBridge(profile, hash), /Different bridge/);
  } finally { await rm(root, { recursive: true, force: true }); }
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
    if (process.platform === 'win32') assert.equal((await install(profile.toUpperCase(), artifact)).state, 'installed');
    assert.equal((await install(profile, artifact)).state, 'installed');
    assert.equal(await readFile(join(profile, 'BepInEx', 'config', 'other.cfg'), 'utf8'), 'preserve');
    await writeFile(first.path, 'different');
    await assert.rejects(install(profile, artifact), /Different bridge/);
    await rm(first.path);
    await symlink(join(profile, 'BepInEx', 'config', 'other.cfg'), first.path);
    await assert.rejects(install(profile, artifact), /symlink/);
    await rm(join(profile, 'BepInEx', 'plugins'), { recursive: true });
    const outside = join(root, 'outside');
    await mkdir(outside);
    await symlink(outside, join(profile, 'BepInEx', 'plugins'), process.platform === 'win32' ? 'junction' : 'dir');
    await assert.rejects(install(profile, artifact), /symlink/);
    await assert.rejects(readFile(join(outside, 'swear01-ValheimCliBridge', 'ValheimCliBridge.dll')), { code: 'ENOENT' });
    await writeFile(join(artifact, 'ValheimCliBridge.dll'), 'tampered');
    await assert.rejects(install(profile, artifact), /checksum/);
  } finally { await rm(root, { recursive: true, force: true }); }
});
