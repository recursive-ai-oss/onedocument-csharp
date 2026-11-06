// Starts the .NET runtime, forwards every console line to the local runner (POST /log) and sends the exit code (POST /exit).
import { dotnet } from './_framework/dotnet.js';

const logElement = document.getElementById('log');
const pending = [];

function send(path, body) {
  const p = fetch(path, { method: 'POST', body, keepalive: true }).catch(() => {});
  pending.push(p);
  return p;
}

function line(text) {
  logElement.textContent += '\n' + text;
  send('/log', text);
}

window.addEventListener('error', (e) => line('PAGE-ERROR ' + e.message));
window.addEventListener('unhandledrejection', (e) => line('PAGE-ERROR unhandled rejection ' + e.reason));

try {
  const { runMain } = await dotnet
    .withModuleConfig({ out: line, err: (t) => line('STDERR ' + t) })
    .withExitOnUnhandledError()
    .create();
  const code = await runMain();
  await Promise.all(pending);
  await send('/exit', String(code));
} catch (e) {
  line('PAGE-ERROR ' + (e && e.stack ? e.stack : e));
  await Promise.all(pending);
  await send('/exit', '99');
}
