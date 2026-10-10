import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:net';
import { once } from 'node:events';
import { mkdtemp, mkdir, writeFile, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { main, validateInput, observe } from '../src/cli.js';
import { request } from '../src/client.js';

const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jv1cAAAAASUVORK5CYII=', 'base64');

async function fixture(handler, run) {
  const server = createServer(socket => {
    socket.on('error', () => {});
    let data = Buffer.alloc(0);
    socket.on('data', chunk => {
      data = Buffer.concat([data, chunk]);
      if (data.length < 4 || data.length < data.readUInt32BE() + 4) return;
      const value = JSON.parse(data.subarray(4).toString());
      const response = Buffer.from(JSON.stringify({ id: value.id, ok: true, ...handler(value) }));
      const header = Buffer.alloc(4); header.writeUInt32BE(response.length);
      socket.end(Buffer.concat([header, response]));
    });
  });
  server.listen(0, '127.0.0.1');
  await once(server, 'listening');
  try { await run(server.address().port); }
  finally { await new Promise(resolve => server.close(resolve)); }
}

test('Semantic control validation rejects raw keys, invalid actions and mixed commands', async () => {
  const base = { moveZ: 1, durationMs: 200 };
  for (const change of [{ actions: '' }, { keys: 'W' }, { buttons: 'left' }, { actions: 'attack,attack' }, { actions: 'spawn' }, { actions: 'jump,' }, { moveX: 1.1 }, { moveZ: NaN }, { durationMs: 49 }, { durationMs: 5001 }, { durationMs: 50.5 }, { yaw: 10 }]) {
    assert.throws(() => validateInput({ ...base, ...change }));
  }
  assert.throws(() => validateInput({ durationMs: 200 }), /Empty/);
  validateInput({ actions: 'jump,dodge', durationMs: 200 });
  validateInput({ yaw: -120, pitch: 10 }, 'look');
  validateInput({ action: 'slot', slot: 8 }, 'action');
  validateInput({ uiAction: 'click', pointerX: 0, pointerY: 1, button: 'left' }, 'ui');
  for (const [input, op] of [[{}, 'look'], [{ yaw: 181 }, 'look'], [{ action: 'slot', slot: 0 }, 'action'], [{ action: 'inventory', slot: 1 }, 'action'], [{ action: 'rotate', scroll: 0 }, 'action'], [{ uiAction: 'click', pointerX: 0.5 }, 'ui'], [{ uiAction: 'scroll', pointerX: 0, pointerY: 1 }, 'ui'], [{ uiAction: 'click', pointerX: 0, pointerY: Infinity }, 'ui']]) assert.throws(() => validateInput(input, op));
  for (const args of [['input', '--move-z', '1'], ['input', '--ms', '5001', '--move-z', '1', '--confirm'], ['input', '--keys', 'W', '--confirm'], ['mouse', '--confirm'], ['input', '--confirm'], ['status', '--move-z', '1'], ['stop', '--confirm'], ['status', '--image', 'frame.png']]) {
    await assert.rejects(main([...args, '--profile', '.']));
  }
});

test('CLI sends game actions, degree look and UI events without OS key fields', async () => {
  const root = await mkdtemp(join(tmpdir(), 'valheim-control-'));
  try {
    await mkdir(join(root, 'BepInEx', 'config'), { recursive: true });
    await writeFile(join(root, 'BepInEx', 'config', 'swear01.ValheimCliBridge.token'), 'a'.repeat(64));
    const commands = [];
    await fixture(value => { commands.push(value); return { state: value.operation === 'input' ? 'started' : 'applied' }; }, async port => {
      const common = ['--profile', root, '--port', String(port)];
      const result = await main(['input', '--move-z', '1', '--actions', 'run', '--ms', '500', '--confirm', ...common]);
      assert.equal(result.state, 'started');
      await main(['stop', ...common]);
      await main(['look', '--yaw', '30', '--pitch', '-10', '--confirm', ...common]);
      await main(['action', '--action', 'slot', '--slot', '1', '--confirm', ...common]);
      await main(['ui', '--ui-action', 'click', '--pointer-x', '0.3', '--pointer-y', '0.8', '--confirm', ...common]);
      assert.deepEqual(commands.map(c => c.operation), ['input', 'stop', 'look', 'action', 'ui']);
      assert.equal(commands[0].durationMs, 500);
      assert.equal(commands[0].moveZ, 1);
      assert.equal(commands[0].actions, 'run');
      assert.equal(commands[0].keys, undefined);
      assert.equal(commands[1].durationMs, undefined);
      assert.equal(commands[2].yaw, 30);
      assert.equal(commands[2].pitch, -10);
      assert.equal(commands[3].slot, 1);
      assert.ok(!JSON.stringify(result).includes('a'.repeat(64)));
    });
  } finally { await rm(root, { recursive: true, force: true }); }
});

test('Observe polls only reads, writes PNG exclusively and omits image data from JSON', async () => {
  const root = await mkdtemp(join(tmpdir(), 'valheim-observe-'));
  try {
    let reads = 0;
    await fixture(value => {
      assert.equal(value.operation, 'observe');
      return ++reads === 1 ? { state: 'capture_pending' } : { state: 'observed', image: png.toString('base64'), imageWidth: 1, imageHeight: 1, capturedAt: 'fixture' };
    }, async port => {
      const connection = { port, token: 'a'.repeat(64) };
      const path = join(root, 'frame.png');
      const result = await observe(connection, path);
      assert.equal(result.imagePath, path);
      assert.equal(result.image, undefined);
      assert.deepEqual(await readFile(path), png);
      await assert.rejects(observe(connection, path), { code: 'EEXIST' });
      assert.deepEqual(await readFile(path), png);
      assert.equal((await observe(connection)).image, undefined);
    });
    await fixture(() => ({ captureError: 'Screenshot failed', state: 'capture_pending' }), async port => {
      await assert.rejects(observe({ port, token: 'a'.repeat(64) }), /Screenshot failed/);
    });
  } finally { await rm(root, { recursive: true, force: true }); }
});

test('Large screenshots are accepted only for observe, and lost input acknowledgements remain uncertain', async () => {
  await fixture(() => ({ image: Buffer.alloc(100000).toString('base64') }), async port => {
    const connection = { port, token: 'a'.repeat(64) };
    assert.ok((await request({ ...connection, operation: 'observe' })).image.length > 65536);
    await assert.rejects(request({ ...connection, operation: 'status' }), /frame|large/);
  });
  const server = createServer(socket => socket.once('data', () => socket.resetAndDestroy()));
  server.listen(0, '127.0.0.1'); await once(server, 'listening');
  try {
    await assert.rejects(request({ port: server.address().port, token: 'a'.repeat(64), operation: 'input', moveZ: 1, durationMs: 200 }), /outcome unknown.*stop\/status/);
  } finally { await new Promise(resolve => server.close(resolve)); }
});
