import test from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { createInterface } from 'node:readline';
import { once } from 'node:events';
import { request } from '../src/client.js';
import { randomUUID } from 'node:crypto';

test('Node CLI client talks to production C# transport and dispatcher', async () => {
  const runtime = process.env.DOTNET_ROOT && existsSync(join(process.env.DOTNET_ROOT, 'dotnet')) ? join(process.env.DOTNET_ROOT, 'dotnet') : 'dotnet';
  const child = spawn(runtime, ['bridge-tests/bin/Release/net9.0/BridgeTests.dll', '--serve'], { stdio: ['ignore', 'pipe', 'pipe'] });
  const lines = createInterface({ input: child.stdout });
  let stderr = '';
  child.stderr.on('data', chunk => { stderr += chunk; });
  const ready = new Promise((resolve, reject) => {
    lines.once('line', line => resolve(Number(line)));
    child.once('exit', () => reject(new Error(`Bridge fixture exited: ${stderr}`)));
    child.once('error', reject);
  });
  try {
    const port = await Promise.race([ready, new Promise((_, reject) => setTimeout(() => reject(new Error('Bridge startup timeout')), 10000).unref())]);
    const status = await request({ port, token: 'a'.repeat(64), operation: 'status' });
    assert.equal(status.state, 'observed');
    assert.equal(status.version, '0.2.0');
    const players = await request({ port, token: 'a'.repeat(64), operation: 'players' });
    assert.deepEqual(players.players, ['fixture-player']);
    await assert.rejects(request({ port, token: 'b'.repeat(64), operation: 'status' }), /Unauthorized/);
    await assert.rejects(request({ port, token: 'a'.repeat(64), operation: 'pos ; spawn Troll' }), /Invalid bridge response/);
    const id = randomUUID();
    const write = { port, token: 'a'.repeat(64), operation: 'teleport', x: 1, y: 2, z: 3, id };
    assert.equal((await request(write)).state, 'started');
    await assert.rejects(request(write), /Write ID already used/);
    const cancelled = { ...write, id: randomUUID(), x: 0 };
    await assert.rejects(request(cancelled), /Fixture cancelled/);
    assert.equal((await request({ ...cancelled, x: 1 })).state, 'started');
    const uncertain = { ...write, id: randomUUID(), x: -1 };
    await assert.rejects(request(uncertain), /Game operation failed/);
    await assert.rejects(request({ ...uncertain, x: 1 }), /Write ID already used/);
    const input = { port, token: 'a'.repeat(64), operation: 'input', moveZ: 1, durationMs: 200, id: randomUUID() };
    assert.equal((await request(input)).state, 'started');
    await assert.rejects(request(input), /Write ID already used/);
    const refusedSlot = { port, token: 'a'.repeat(64), operation: 'action', action: 'slot', slot: 8, id: randomUUID() };
    await assert.rejects(request(refusedSlot), /Hotbar slot is empty/);
    assert.equal((await request({ ...refusedSlot, slot: 1 })).state, 'started');
    const uncertainSlot = { ...refusedSlot, slot: 7, id: randomUUID() };
    await assert.rejects(request(uncertainSlot), /Game operation failed/);
    await assert.rejects(request({ ...uncertainSlot, slot: 1 }), /Write ID already used/);
    for (const action of [
      { operation: 'look', yaw: 30, pitch: -10 },
      { operation: 'action', action: 'slot', slot: 1 },
      { operation: 'ui', uiAction: 'click', pointerX: 0.2, pointerY: 0.4 }
    ]) {
      const write = { port, token: 'a'.repeat(64), ...action, id: randomUUID() };
      assert.equal((await request(write)).state, 'started');
      await assert.rejects(request(write), /Write ID already used/);
    }
    assert.equal((await request({ port, token: 'a'.repeat(64), operation: 'stop' })).ok, true);
  } finally {
    if (child.exitCode === null && child.signalCode === null) {
      const exited = once(child, 'exit');
      child.kill();
      await exited;
    }
    lines.close();
  }
});
