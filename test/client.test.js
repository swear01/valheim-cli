import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:net';
import { once } from 'node:events';
import { request } from '../src/client.js';

test('Client validates identity, errors, framing and accepts fragmented responses', async () => {
  for (const scenario of ['good', 'badId', 'refused', 'oversize', 'truncated']) {
    const server = createServer(socket => {
      let bytes = Buffer.alloc(0);
      socket.on('data', data => {
        bytes = Buffer.concat([bytes, data]);
        if (bytes.length < 4 || bytes.length < 4 + bytes.readUInt32BE()) return;
        const value = JSON.parse(bytes.subarray(4).toString());
        assert.equal(value.operation, 'status');
        const response = Buffer.from(JSON.stringify({ id: scenario === 'badId' ? 'wrong' : value.id, ok: scenario !== 'refused', error: 'denied', state: 'observed' }));
        const header = Buffer.alloc(4); header.writeUInt32BE(scenario === 'oversize' ? 999999 : response.length);
        socket.write(header.subarray(0, 2));
        setTimeout(() => {
          socket.write(header.subarray(2));
          if (scenario !== 'truncated' && scenario !== 'oversize') socket.end(response);
          else socket.end();
        }, 5);
      });
    });
    server.listen(0, '127.0.0.1');
    await once(server, 'listening');
    try {
      const result = request({ port: server.address().port, token: 'a'.repeat(64), operation: 'status' });
      if (scenario === 'good') assert.equal((await result).state, 'observed');
      else await assert.rejects(result);
    } finally { await new Promise(resolve => server.close(resolve)); }
  }
});

test('Connection loss after submitting a write reports unknown outcome', async () => {
  const server = createServer(socket => socket.once('data', () => socket.resetAndDestroy()));
  server.listen(0, '127.0.0.1');
  await once(server, 'listening');
  try {
    await assert.rejects(request({ port: server.address().port, token: 'a'.repeat(64), operation: 'teleport', x: 1, y: 2, z: 3 }), /outcome.*unknown.*retry/i);
  } finally { await new Promise(resolve => server.close(resolve)); }
});
