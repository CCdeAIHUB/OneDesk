<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from "vue";
import vueRuntime from "vue/dist/vue.global.prod.js?raw";

interface RuntimeArtifact {
  code: string;
  style: string;
  sha256: string;
}

interface CodeJsApiRequest {
  targetDeviceId: string;
  capability: string;
  payload: Record<string, unknown>;
  respond: (response: string) => void;
}

const props = defineProps<{ runtime: RuntimeArtifact }>();
const emit = defineEmits<{
  jsapi: [request: CodeJsApiRequest];
  trigger: [value: { triggerId: string; fingers: number }];
}>();
const frame = ref<HTMLIFrameElement | null>(null);
const frameToken = crypto.randomUUID();

const source = computed(() => buildSourceDocument(props.runtime, frameToken));

onMounted(() => window.addEventListener("message", handleMessage));
onUnmounted(() => window.removeEventListener("message", handleMessage));

function handleMessage(event: MessageEvent) {
  if (event.source !== frame.value?.contentWindow || !event.data || event.data.frameToken !== frameToken) return;
  if (event.data.type === "onedesk-code-jsapi") {
    const requestId = String(event.data.requestId || "");
    emit("jsapi", {
      targetDeviceId: String(event.data.targetDeviceId || ""),
      capability: String(event.data.capability || ""),
      payload: isRecord(event.data.payload) ? event.data.payload : {},
      respond: (response) => frame.value?.contentWindow?.postMessage({
        type: "onedesk-code-jsapi-response",
        frameToken,
        requestId,
        response,
      }, "*"),
    });
  } else if (event.data.type === "onedesk-code-trigger") {
    emit("trigger", {
      triggerId: String(event.data.triggerId || ""),
      fingers: Math.max(1, Math.min(5, Number(event.data.fingers) || 1)),
    });
  }
}

function buildSourceDocument(runtime: RuntimeArtifact, token: string) {
  const bridge = `
(() => {
  const frameToken = ${JSON.stringify(token)};
  const pending = new Map();
  window.OneDesk = Object.freeze({
    callJsApi(targetDeviceId, capability, payload = {}) {
      return new Promise((resolve, reject) => {
        const requestId = crypto.randomUUID();
        pending.set(requestId, { resolve, reject });
        parent.postMessage({ type: "onedesk-code-jsapi", frameToken, requestId, targetDeviceId, capability, payload }, "*");
      });
    },
  });
  addEventListener("message", (event) => {
    const data = event.data;
    if (!data || data.type !== "onedesk-code-jsapi-response" || data.frameToken !== frameToken) return;
    const task = pending.get(data.requestId);
    if (!task) return;
    pending.delete(data.requestId);
    try {
      const result = JSON.parse(data.response);
      if (result.ok) task.resolve(result.payload); else task.reject(Object.assign(new Error(result.message || "JSAPI 调用失败"), result));
    } catch (error) { task.reject(error); }
  });

  let gesture = null;
  let holdTimer = 0;
  const center = (points) => Array.from(points).reduce((sum, point) => ({ x: sum.x + point.clientX / points.length, y: sum.y + point.clientY / points.length }), { x: 0, y: 0 });
  const distance = (points) => points.length < 2 ? 0 : Math.hypot(points[1].clientX - points[0].clientX, points[1].clientY - points[0].clientY);
  const angle = (points) => points.length < 2 ? 0 : Math.atan2(points[1].clientY - points[0].clientY, points[1].clientX - points[0].clientX) * 180 / Math.PI;
  const send = (triggerId, fingers) => parent.postMessage({ type: "onedesk-code-trigger", frameToken, triggerId, fingers }, "*");
  addEventListener("touchstart", (event) => {
    const start = center(event.touches);
    gesture = { x: start.x, y: start.y, distance: distance(event.touches), angle: angle(event.touches), fingers: event.touches.length, moved: false, held: false };
    clearTimeout(holdTimer);
    holdTimer = setTimeout(() => { if (gesture && !gesture.moved) { gesture.held = true; send("long-press", gesture.fingers); } }, 620);
  }, { passive: true });
  addEventListener("touchmove", (event) => {
    if (!gesture) return;
    gesture.fingers = Math.max(gesture.fingers, event.touches.length);
    const point = center(event.touches);
    if (Math.hypot(point.x - gesture.x, point.y - gesture.y) > 12) { gesture.moved = true; clearTimeout(holdTimer); }
  }, { passive: true });
  addEventListener("touchend", (event) => {
    if (!gesture) return;
    const current = gesture; gesture = null; clearTimeout(holdTimer); if (current.held) return;
    const point = center(event.changedTouches); const dx = point.x - current.x; const dy = point.y - current.y; const travel = Math.hypot(dx, dy);
    let triggerId = "tap";
    if (current.fingers >= 2 && event.changedTouches.length >= 2) {
      const endDistance = distance(event.changedTouches); const endAngle = angle(event.changedTouches);
      if (current.distance > 0 && Math.abs(endDistance - current.distance) > 42) triggerId = endDistance > current.distance ? "pinch-out" : "pinch-in";
      else if (Math.abs(endAngle - current.angle) > 18) triggerId = "rotate";
      else if (travel >= 36) triggerId = swipe(dx, dy, current.fingers);
    } else if (travel >= 36) triggerId = swipe(dx, dy, current.fingers);
    send(triggerId, current.fingers);
  }, { passive: true });
  function swipe(dx, dy, fingers) {
    const direction = Math.abs(dx) > Math.abs(dy) ? (dx > 0 ? "right" : "left") : (dy > 0 ? "down" : "up");
    return fingers > 1 ? fingers + "-finger-swipe-" + direction : "swipe-" + direction;
  }
})();`;
  const scriptTag = "script";
  return `<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1,user-scalable=no"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data: file:; media-src data: file:; connect-src 'none'; font-src data: file:"><style>html,body,#app{width:100%;height:100%;margin:0;overflow:hidden}*{box-sizing:border-box}${escapeStyle(runtime.style)}</style></head><body><div id="app"></div><${scriptTag}>${escapeScript(vueRuntime)}</${scriptTag}><${scriptTag}>${escapeScript(bridge)}</${scriptTag}><${scriptTag}>${escapeScript(runtime.code)}</${scriptTag}></body></html>`;
}

function escapeScript(value: string) {
  return value.replaceAll("</scr" + "ipt", "<\\/scr" + "ipt");
}

function escapeStyle(value: string) {
  return value.replaceAll("</style", "<\\/style");
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
</script>

<template>
  <iframe
    ref="frame"
    :srcdoc="source"
    class="block size-full border-0 bg-transparent"
    sandbox="allow-scripts"
    title="代码组件"
  />
</template>
