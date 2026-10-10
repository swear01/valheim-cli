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

test('Input validation rejects OS shortcuts, invalid motion and ambiguous CLI options', async () => {
  const base = { keys: 'W', durationMs: 200 };
  for (const change of [{ keys: 'Alt,Tab' }, { keys: 'Win' }, { keys: 'F5' }, { keys: 'F12' }, { keys: 'Control,Escape' }, { keys: 'w,W' }, { keys: 'W,' }, { durationMs: 49 }, { durationMs: 5001 }, { durationMs: 50.5 }, { mouseX: 2001 }, { scroll: 11 }, { pointerX: 0.5 }, { pointerX: 0.5, pointerY: NaN }, { pointerX: 0.5, pointerY: 0.5, mouseY: 0 }]) {
    assert.throws(() => validateInput({ ...base, ...change }));
  }
  assert.throws(() => validateInput({ durationMs: 200 }), /Empty/);
  validateInput({ keys: 'Space', buttons: 'right', durationMs: 200 });
  validateInput({ pointerX: 0, pointerY: 1, buttons: 'left', durationMs: 200 });
  validateInput({ mouseX: -120 }, 'mouse');
  assert.throws(() => validateInput({ mouseX: 120, durationMs: 200 }, 'mouse'));
  assert.throws(() => validateInput({ mouseX: 120, keys: 'W' }, 'mouse'));
  for (const args of [['input', '--keys', 'W'], ['input', '--ms', '5001', '--keys', 'W', '--confirm'], ['input', '--keys', 'W', '--mouse-x', 'NaN', '--confirm'], ['input', 'W', '--confirm'], ['input', '--confirm'], ['status', '--keys', 'W'], ['stop', '--confirm'], ['status', '--image', 'frame.png']]) {
    await assert.rejects(main([...args, '--profile', '.']));
  }
});

test('CLI sends bounded controls, status and permission-free stop without leaking tokens', async () => {
  const root = await mkdtemp(join(tmpdir(), 'valheim-control-'));
  try {
    await mkdir(join(root, 'BepInEx', 'config'), { recursive: true });
    await writeFile(join(root, 'BepInEx', 'config', 'swear01.ValheimCliBridge.token'), 'a'.repeat(64));
    const commands = [];
    await fixture(value => { commands.push(value); return { state: value.operation === 'input' ? 'started' : 'stopped' }; }, async port => {
      const common = ['--profile', root, '--port', String(port)];
      const result = await main(['input', '--keys', 'W,Shift', '--ms', '500', '--mouse-x', '120', '--confirm', ...common]);
      assert.equal(result.state, 'started');
      await main(['stop', ...common]);
      await main(['mouse', '--pointer-x', '0.3', '--pointer-y', '0.8', '--confirm', ...common]);
      assert.deepEqual(commands.map(c => c.operation), ['input', 'stop', 'mouse']);
      assert.equal(commands[0].durationMs, 500);
      assert.equal(commands[0].mouseX, 120);
      assert.equal(commands[0].keys, 'W,Shift');
      assert.equal(commands[1].durationMs, undefined);
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
    await assert.rejects(request({ port: server.address().port, token: 'a'.repeat(64), operation: 'input', keys: 'W', durationMs: 200 }), /outcome unknown.*stop\/status/);
  } finally { await new Promise(resolve => server.close(resolve)); }
});
