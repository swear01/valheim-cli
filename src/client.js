import { connect } from 'node:net';
import { randomUUID } from 'node:crypto';

const maxFrame = 65536;

export function request({ port, token, operation, x, y, z, actions, moveX, moveZ, durationMs, yaw, pitch, action, slot, uiAction, button, scroll, pointerX, pointerY, id = randomUUID() }) {
  if (!Number.isInteger(port) || port < 1024 || port > 65535) throw new Error('Invalid bridge port');
  if (!/^[a-f0-9]{64}$/.test(token)) throw new Error('Invalid local token');
  const body = Buffer.from(JSON.stringify({ id, token, operation, x, y, z, actions, moveX, moveZ, durationMs, yaw, pitch, action, slot, uiAction, button, scroll, pointerX, pointerY }));
  const maxResponse = operation === 'observe' ? 8 * 1024 * 1024 : maxFrame;
  if (body.length > maxFrame) throw new Error('Request too large');
  const header = Buffer.alloc(4);
  header.writeUInt32BE(body.length);
  return new Promise((resolve, reject) => {
    const socket = connect({ host: '127.0.0.1', port });
    const timer = setTimeout(() => fail(new Error('Bridge timeout')), 5000);
    let buffer = Buffer.alloc(0);
    let expected;
    let settled = false;
    let submitted = false;
    let acknowledged = false;
    function fail(error) {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      socket.destroy();
      if (submitted && ['teleport', 'input', 'look', 'action', 'ui'].includes(operation) && !acknowledged) error.message += ' Write outcome unknown; do not automatically retry. Use stop/status.';
      reject(error);
    }
    socket.once('connect', () => { submitted = true; socket.write(Buffer.concat([header, body])); });
    socket.on('data', chunk => {
      if (settled) return;
      if (buffer.length + chunk.length > maxResponse + 4) return fail(new Error('Bridge response too large'));
      buffer = Buffer.concat([buffer, chunk]);
      if (expected === undefined && buffer.length >= 4) {
        expected = buffer.readUInt32BE();
        if (expected < 1 || expected > maxResponse) return fail(new Error('Invalid bridge frame'));
      }
      if (expected !== undefined && buffer.length >= expected + 4) {
        try {
          if (buffer.length !== expected + 4) throw new Error('Trailing bridge data');
          const response = JSON.parse(buffer.subarray(4).toString('utf8'));
          if (response.id !== id || typeof response.ok !== 'boolean') throw new Error('Invalid bridge response');
          acknowledged = true;
          if (!response.ok) throw new Error(response.error || 'Bridge rejected operation');
          settled = true;
          clearTimeout(timer);
          socket.destroy();
          resolve(response);
        } catch (error) { fail(error); }
      }
    });
    socket.on('error', () => fail(new Error(submitted ? 'Bridge connection failed.' : 'Cannot connect to bridge. Start Valheim with the plugin enabled.')));
    socket.on('end', () => fail(new Error('Bridge closed before a complete response')));
  });
}
