import { sendShell } from "./nativeBridge";

interface FrontendPluginDescriptor {
  pluginId: string;
  name: string;
  sessionId: string;
  source: string;
}

interface PluginRequest {
  marker: "onedesk.frontend-plugin";
  requestId: string;
  operation: "callJsApi" | "invokeBackend" | "runtimeError";
  targetDeviceId?: string;
  capability?: string;
  method?: string;
  payload?: unknown;
  parameters?: unknown;
  message?: string;
}

const sessions = new Map<Window, FrontendPluginDescriptor>();
const frames: HTMLIFrameElement[] = [];
let errorReporter: ((message: string) => void) | undefined;
let listening = false;

function pluginDocument(descriptor: FrontendPluginDescriptor) {
  const source = JSON.stringify(descriptor.source).replace(/<\//g, "<\\/");
  return `<!doctype html><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; connect-src 'none'; img-src 'none'; media-src 'none'; style-src 'none'; script-src 'unsafe-inline' blob:"><script>
  const pending = new Map();
  let sequence = 0;
  const request = (operation, values) => new Promise((resolve, reject) => {
    const requestId = 'plugin-' + (++sequence) + '-' + Date.now();
    const timer = setTimeout(() => { pending.delete(requestId); reject(new Error('PluginHostTimeout')); }, 30000);
    pending.set(requestId, { resolve, reject, timer });
    parent.postMessage({ marker: 'onedesk.frontend-plugin', requestId, operation, ...values }, '*');
  });
  addEventListener('message', (event) => {
    const data = event.data;
    if (!data || data.marker !== 'onedesk.frontend-plugin-result') return;
    const item = pending.get(data.requestId);
    if (!item) return;
    clearTimeout(item.timer);
    pending.delete(data.requestId);
    if (data.ok) item.resolve(data.payload); else item.reject(new Error(data.message || data.errorCode || 'PluginHostError'));
  });
  const api = Object.freeze({
    callJsApi: (targetDeviceId, capability, payload = {}) => request('callJsApi', { targetDeviceId, capability, payload }),
    invokeBackend: (method, parameters = {}) => request('invokeBackend', { method, parameters }),
  });
  Object.defineProperty(window, 'OneDeskPlugin', { value: api, configurable: false, writable: false });
  (async () => {
    const source = ${source};
    const url = URL.createObjectURL(new Blob([source], { type: 'text/javascript' }));
    try {
      const module = await import(url);
      const activate = typeof module.default === 'function' ? module.default : module.activate;
      if (typeof activate === 'function') await activate(api);
    } catch (error) {
      parent.postMessage({ marker: 'onedesk.frontend-plugin', operation: 'runtimeError', requestId: '', message: String(error?.message || error) }, '*');
    } finally {
      URL.revokeObjectURL(url);
    }
  })();
  <\/script>`;
}

async function handlePluginMessage(event: MessageEvent<PluginRequest>) {
  const descriptor = event.source instanceof Window ? sessions.get(event.source) : undefined;
  const request = event.data;
  if (!descriptor || request?.marker !== "onedesk.frontend-plugin") return;
  if (request.operation === "runtimeError") {
    errorReporter?.(`${descriptor.name} 运行失败：${request.message ?? "未知错误"}`);
    return;
  }

  const response = request.operation === "callJsApi"
    ? await sendShell("plugin.frontend.callJsApi", {
        sessionId: descriptor.sessionId,
        targetDeviceId: request.targetDeviceId ?? "",
        capability: request.capability ?? "",
        payload: request.payload ?? {},
      })
    : await sendShell("plugin.frontend.invokeBackend", {
        sessionId: descriptor.sessionId,
        method: request.method ?? "",
        parameters: request.parameters ?? {},
      });
  event.source?.postMessage({
    marker: "onedesk.frontend-plugin-result",
    requestId: request.requestId,
    ok: response.ok,
    payload: response.payload,
    errorCode: response.errorCode,
    message: response.message,
  }, { targetOrigin: "*" });
}

export async function reloadFrontendPlugins(reportError: (message: string) => void) {
  stopFrontendPlugins();
  errorReporter = reportError;
  if (!listening) {
    window.addEventListener("message", handlePluginMessage);
    listening = true;
  }
  const response = await sendShell<FrontendPluginDescriptor[]>("plugin.frontend.list");
  if (!response.ok || !response.payload) {
    reportError(response.message ?? "前端插件加载失败");
    return;
  }
  for (const descriptor of response.payload) {
    const frame = document.createElement("iframe");
    frame.hidden = true;
    frame.setAttribute("sandbox", "allow-scripts");
    frame.setAttribute("aria-hidden", "true");
    frame.srcdoc = pluginDocument(descriptor);
    document.body.append(frame);
    if (frame.contentWindow) sessions.set(frame.contentWindow, descriptor);
    frames.push(frame);
  }
}

export function stopFrontendPlugins() {
  sessions.clear();
  for (const frame of frames.splice(0)) frame.remove();
}
