<script setup lang="ts">
import { Icon } from "@iconify/vue";
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from "vue";

interface KnownDesktop {
  desktopId: string;
  name: string;
  host: string;
  port: number;
  trusted: boolean;
  schemeVersion: string;
  schemeHash: string;
}

interface TriggerDefinition {
  id: string;
  category: string;
  displayName: string;
  fingerCount: number;
}

interface InvocationDefinition {
  targetDeviceId: string;
  capability: string;
  parameters: Record<string, unknown>;
}

interface ActionDefinition {
  id: string;
  name: string;
  trigger: TriggerDefinition;
  invocations: InvocationDefinition[];
}

interface VisualTextLayer {
  id: string;
  content: string;
  fontSize: number;
  color: string;
  x: number;
  y: number;
  width: number;
  height: number;
}

interface VisualConfig {
  base: { borderRadius: number; margin: number; layout: string };
  background: { kind: string; value: string; secondaryValue: string; mediaSource: string };
  texts: VisualTextLayer[];
  image: { source: string; size: string; position: string; margin: number };
  states: { pressed: string; locked: string };
}

interface ComponentDefinition {
  id: string;
  name: string;
  editMode: string | number;
  actionIds: string[];
}

interface ComponentBundle {
  definition: ComponentDefinition;
  visualConfig: VisualConfig | null;
}

interface GridCellDefinition {
  id: string;
  row: number;
  column: number;
  rowSpan: number;
  columnSpan: number;
  componentId?: string | null;
  style: {
    borderRadius: number;
    outlineColor: string;
    outlineWidth: number;
    outlineStyle: string;
  };
}

interface PageDefinition {
  id: string;
  name: string;
  rows: number;
  columns: number;
  previewRatioWidth?: number;
  previewRatioHeight?: number;
  gridHorizontalAlign?: "left" | "center" | "right";
  gridVerticalAlign?: "top" | "center" | "bottom";
  spacing: { padding: number; rowGap: number; columnGap: number };
  backgroundKind: string;
  backgroundValue: string;
  backgroundSecondaryValue?: string | null;
  backgroundMediaSource?: string | null;
  cells: GridCellDefinition[];
}

interface SchemeDefinition {
  id: string;
  name: string;
  pageIds: string[];
  globalPrevious: { trigger: TriggerDefinition; animation: string };
  globalNext: { trigger: TriggerDefinition; animation: string };
  edges: Array<{
    fromPageId: string;
    toPageId: string;
    trigger: TriggerDefinition;
    animation: string;
  }>;
}

interface CachedScheme {
  desktopId: string;
  version: string;
  hash: string;
  activeSchemeId: string | null;
  scheme: SchemeDefinition | null;
  pages: PageDefinition[];
  components: ComponentBundle[];
  actions: ActionDefinition[];
}

interface NativeResponse<T> {
  ok: boolean;
  payload?: T;
  message?: string;
  errorCode?: string;
}

interface GestureState {
  startX: number;
  startY: number;
  startDistance: number;
  startAngle: number;
  startedAt: number;
  maxFingers: number;
  moved: boolean;
  held: boolean;
  holdTimer: number;
}

declare global {
  interface Window {
    OneDeskNative?: {
      listKnownDesktops?: () => string;
      connect?: (host: string, port: number, code: string) => string;
      connectByQr?: (qrPayload: string) => string;
      startQrScan?: () => string;
      cancelQrScan?: () => string;
      getCachedScheme?: (desktopId: string) => string;
      refreshScheme?: (desktopId: string) => string;
      setDisplayRatio?: (width: number, height: number) => string;
      callJsApi?: (
        targetDeviceId: string,
        capability: string,
        payloadJson: string,
        schemeId: string,
        pageId: string,
        componentId: string,
      ) => string;
    };
    __oneDeskHandleQrScan?: (payload: string | null, error: string | null) => void;
    __oneDeskHandleSchemeUpdated?: (desktopId: string, version: string, hash: string) => void;
  }
}

const connected = ref(false);
const busy = ref(false);
const scanning = ref(false);
const activePageIndex = ref(0);
const transitionName = ref("page-fade");
const host = ref("");
const port = ref(48320);
const code = ref("");
const message = ref("请输入桌面端显示的 IP、端口和 6 位验证码。");
const desktops = ref<KnownDesktop[]>([]);
const cachedScheme = ref<CachedScheme | null>(null);
const viewportWidth = ref(window.innerWidth);
const viewportHeight = ref(window.innerHeight);
const gestureStates = new Map<string, GestureState>();
const lastTapAt = new Map<string, number>();
let schemeRefreshTimer = 0;

const scheme = computed(() => cachedScheme.value?.scheme ?? null);
const pages = computed(() => {
  const payload = cachedScheme.value;
  if (!payload?.scheme) return [];
  const byId = new Map(payload.pages.map((page) => [page.id, page]));
  return payload.scheme.pageIds.map((id) => byId.get(id)).filter((page): page is PageDefinition => Boolean(page));
});
const currentPage = computed(() => pages.value[activePageIndex.value] ?? pages.value[0] ?? null);
const componentMap = computed(() => new Map((cachedScheme.value?.components ?? []).map((item) => [item.definition.id, item])));
const actionMap = computed(() => new Map((cachedScheme.value?.actions ?? []).map((item) => [item.id, item])));

// 页面宽高比是桌面设计结果的一部分；移动端据此请求原生壳子切换横竖屏。
watch(currentPage, (page) => {
  if (!page) return;
  window.OneDeskNative?.setDisplayRatio?.(page.previewRatioWidth ?? 21, page.previewRatioHeight ?? 9);
});
const currentGridStyle = computed<Record<string, string>>(() => {
  const page = currentPage.value;
  if (!page) return {} as Record<string, string>;
  const rows = clamp(page.rows, 1, 12);
  const columns = clamp(page.columns, 1, 12);
  const padding = Math.max(0, Number(page.spacing?.padding ?? 0));
  const rowGap = Math.max(0, Number(page.spacing?.rowGap ?? 0));
  const columnGap = Math.max(0, Number(page.spacing?.columnGap ?? 0));
  const availableWidth = viewportWidth.value - padding * 2 - columnGap * Math.max(0, columns - 1);
  const availableHeight = viewportHeight.value - padding * 2 - rowGap * Math.max(0, rows - 1);
  const cellSize = Math.max(1, Math.floor(Math.min(availableWidth / columns, availableHeight / rows)));
  return {
    gridTemplateColumns: `repeat(${columns}, ${cellSize}px)`,
    gridTemplateRows: `repeat(${rows}, ${cellSize}px)`,
    columnGap: `${columnGap}px`,
    rowGap: `${rowGap}px`,
    padding: `${padding}px`,
    justifyContent: page.gridHorizontalAlign === "left" ? "start" : page.gridHorizontalAlign === "right" ? "end" : "center",
    alignContent: page.gridVerticalAlign === "top" ? "start" : page.gridVerticalAlign === "bottom" ? "end" : "center",
  };
});

onMounted(() => {
  loadKnownDesktops();
  window.addEventListener("resize", updateViewport);
  window.__oneDeskHandleQrScan = handleQrScan;
  window.__oneDeskHandleSchemeUpdated = handleSchemeUpdated;
});

onUnmounted(() => {
  if (schemeRefreshTimer) window.clearInterval(schemeRefreshTimer);
  for (const state of gestureStates.values()) window.clearTimeout(state.holdTimer);
  window.OneDeskNative?.cancelQrScan?.();
  delete window.__oneDeskHandleQrScan;
  delete window.__oneDeskHandleSchemeUpdated;
  window.removeEventListener("resize", updateViewport);
});

function updateViewport() {
  viewportWidth.value = window.innerWidth;
  viewportHeight.value = window.innerHeight;
}

function loadKnownDesktops() {
  try {
    desktops.value = JSON.parse(window.OneDeskNative?.listKnownDesktops?.() ?? "[]") as KnownDesktop[];
  } catch {
    desktops.value = [];
  }
}

function connect() {
  if (!host.value.trim()) {
    message.value = "请输入桌面端 IP。";
    return;
  }
  if (!/^\d{6}$/.test(code.value) && !findTrustedDesktop(host.value, port.value)) {
    message.value = "首次连接请输入桌面端显示的 6 位验证码。";
    return;
  }
  performConnection(() => window.OneDeskNative?.connect?.(host.value.trim(), Number(port.value), code.value));
}

function startQrScan() {
  if (!window.OneDeskNative?.startQrScan) {
    message.value = "二维码扫描必须在 Android 客户端中使用。";
    return;
  }
  const response = parseResponse<{ started: boolean }>(window.OneDeskNative.startQrScan());
  if (!response.ok) {
    message.value = response.message ?? "无法启动二维码扫描。";
    return;
  }
  scanning.value = true;
  message.value = "请将桌面端显示的二维码放入取景框。";
}

function handleQrScan(payload: string | null, error: string | null) {
  scanning.value = false;
  if (!payload) {
    message.value = error || "未识别到二维码。";
    return;
  }
  performConnection(() => window.OneDeskNative?.connectByQr?.(payload));
}

function connectKnown(desktop: KnownDesktop) {
  host.value = desktop.host;
  port.value = desktop.port;
  code.value = "";
  if (!desktop.trusted) {
    message.value = "该桌面端需要重新输入验证码。";
    return;
  }
  performConnection(() => window.OneDeskNative?.connect?.(desktop.host, desktop.port, ""));
}

function performConnection(request: () => string | undefined) {
  if (busy.value) return;
  if (scanning.value) {
    window.OneDeskNative?.cancelQrScan?.();
    scanning.value = false;
  }
  busy.value = true;
  message.value = "正在连接并校验方案缓存...";
  window.setTimeout(() => {
    try {
      const raw = request();
      const response = parseResponse<{ desktop: KnownDesktop; hasScheme: boolean }>(raw);
      if (!response.ok || !response.payload) {
        message.value = response.message ?? "连接失败，请检查 IP、端口和验证码。";
        return;
      }
      loadKnownDesktops();
      loadScheme(response.payload.desktop.desktopId);
      connected.value = true;
      activePageIndex.value = 0;
      startSchemeRefresh(response.payload.desktop.desktopId);
      void nextTick(updateViewport);
    } finally {
      busy.value = false;
    }
  }, 0);
}

function loadScheme(desktopId: string) {
  const response = parseResponse<CachedScheme>(window.OneDeskNative?.getCachedScheme?.(desktopId));
  cachedScheme.value = response.ok && response.payload ? response.payload : emptyScheme(desktopId);
  if (activePageIndex.value >= pages.value.length) activePageIndex.value = 0;
}

function handleSchemeUpdated(desktopId: string) {
  loadScheme(desktopId);
  activePageIndex.value = 0;
}

function startSchemeRefresh(desktopId: string) {
  if (schemeRefreshTimer) window.clearInterval(schemeRefreshTimer);
  schemeRefreshTimer = window.setInterval(() => {
    const response = parseResponse<{ cacheUpdated: boolean }>(window.OneDeskNative?.refreshScheme?.(desktopId));
    if (response.ok && response.payload?.cacheUpdated) loadScheme(desktopId);
  }, 10_000);
}

function pageBackgroundStyle(page: PageDefinition): Record<string, string> {
  if (page.backgroundKind === "solid") return { background: page.backgroundValue || "#ffffff" };
  if (page.backgroundKind === "gradient") {
    return { background: `linear-gradient(135deg, ${page.backgroundValue || "#0ea5e9"}, ${page.backgroundSecondaryValue || "#22d3ee"})` };
  }
  if (page.backgroundKind === "image" && page.backgroundMediaSource) {
    return { background: `url('${cssUrl(page.backgroundMediaSource)}') center / cover no-repeat` };
  }
  return { background: page.backgroundKind === "video" ? "#020617" : "#ffffff" };
}

function cellStyle(cell: GridCellDefinition): Record<string, string> {
  return {
    gridColumn: `${cell.column} / span ${Math.max(1, cell.columnSpan)}`,
    gridRow: `${cell.row} / span ${Math.max(1, cell.rowSpan)}`,
    borderRadius: `${Math.max(0, cell.style?.borderRadius ?? 0)}px`,
    border: `${Math.max(0, cell.style?.outlineWidth ?? 0)}px ${cell.style?.outlineStyle || "solid"} ${cell.style?.outlineColor || "transparent"}`,
  };
}

function componentStyle(bundle: ComponentBundle | undefined): Record<string, string> {
  const config = bundle?.visualConfig;
  if (!config) return { background: "#e2e8f0" };
  const background = config.background;
  const style: Record<string, string> = {
    borderRadius: `${Math.max(0, config.base?.borderRadius ?? 0)}px`,
    margin: `${Math.max(0, config.base?.margin ?? 0)}px`,
  };
  if (background.kind === "solid") style.background = background.value || "#0ea5e9";
  else if (background.kind === "gradient") style.background = `linear-gradient(135deg, ${background.value || "#0ea5e9"}, ${background.secondaryValue || "#22d3ee"})`;
  else if (background.kind === "image" && background.mediaSource) {
    style.background = `url('${cssUrl(background.mediaSource)}') center / ${config.image?.size || "cover"} no-repeat`;
  } else style.background = "#0f172a";
  return style;
}

function textStyle(text: VisualTextLayer): Record<string, string> {
  return {
    left: `${clamp(Number(text.x), 0, 100)}%`,
    top: `${clamp(Number(text.y), 0, 100)}%`,
    width: `${clamp(Number(text.width), 4, 100)}%`,
    minHeight: `${clamp(Number(text.height), 4, 100)}%`,
    color: text.color || "#ffffff",
    fontSize: `${Math.max(6, Number(text.fontSize) || 14)}px`,
  };
}

function imageStyle(config: VisualConfig): Record<string, string> {
  const positionMap: Record<string, string> = { left: "left", right: "right", top: "top", bottom: "bottom", center: "center" };
  return {
    objectFit: config.image.size === "contain" ? "contain" : "cover",
    objectPosition: positionMap[config.image.position] || "center",
    padding: `${Math.max(0, Number(config.image.margin) || 0)}px`,
  };
}

function componentFor(cell: GridCellDefinition) {
  return cell.componentId ? componentMap.value.get(cell.componentId) : undefined;
}

function isVisualComponent(bundle: ComponentBundle | undefined) {
  return Boolean(bundle?.visualConfig);
}

function handleTouchStart(key: string, event: TouchEvent) {
  const points = Array.from(event.touches);
  if (!points.length) return;
  const center = touchCenter(points);
  const previous = gestureStates.get(key);
  if (previous) window.clearTimeout(previous.holdTimer);
  const state: GestureState = {
    startX: center.x,
    startY: center.y,
    startDistance: touchDistance(points),
    startAngle: touchAngle(points),
    startedAt: performance.now(),
    maxFingers: points.length,
    moved: false,
    held: false,
    holdTimer: 0,
  };
  state.holdTimer = window.setTimeout(() => {
    state.held = true;
    dispatchGesture(key, event.currentTarget, "long-press", state.maxFingers);
  }, 620);
  gestureStates.set(key, state);
}

function handleTouchMove(key: string, event: TouchEvent) {
  const state = gestureStates.get(key);
  if (!state) return;
  const points = Array.from(event.touches);
  if (!points.length) return;
  state.maxFingers = Math.max(state.maxFingers, points.length);
  const center = touchCenter(points);
  if (Math.hypot(center.x - state.startX, center.y - state.startY) > 12) {
    state.moved = true;
    window.clearTimeout(state.holdTimer);
  }
}

function handleTouchEnd(key: string, event: TouchEvent) {
  const state = gestureStates.get(key);
  if (!state) return;
  gestureStates.delete(key);
  window.clearTimeout(state.holdTimer);
  if (state.held) return;
  const changed = Array.from(event.changedTouches);
  const end = touchCenter(changed);
  const dx = end.x - state.startX;
  const dy = end.y - state.startY;
  const distance = Math.hypot(dx, dy);
  let trigger = "tap";
  if (state.maxFingers >= 2 && changed.length >= 2) {
    const endDistance = touchDistance(changed);
    const endAngle = touchAngle(changed);
    if (state.startDistance > 0 && Math.abs(endDistance - state.startDistance) > 42) {
      trigger = endDistance > state.startDistance ? "pinch-out" : "pinch-in";
    } else if (Math.abs(normalizeAngle(endAngle - state.startAngle)) > 18) {
      trigger = "rotate";
    } else if (distance >= 36) {
      trigger = swipeTrigger(dx, dy, state.maxFingers);
    }
  } else if (distance >= 36) {
    trigger = swipeTrigger(dx, dy, state.maxFingers);
  } else {
    const previousTap = lastTapAt.get(key) ?? 0;
    const now = performance.now();
    trigger = now - previousTap < 320 ? "double-tap" : "tap";
    lastTapAt.set(key, now);
  }
  dispatchGesture(key, event.currentTarget, trigger, state.maxFingers);
}

function handleTouchCancel(key: string) {
  const state = gestureStates.get(key);
  if (state) window.clearTimeout(state.holdTimer);
  gestureStates.delete(key);
}

function dispatchGesture(key: string, target: EventTarget | null, triggerId: string, fingers: number) {
  if (key.startsWith("cell:")) {
    const cellId = key.slice(5);
    const cell = currentPage.value?.cells.find((item) => item.id === cellId);
    if (cell && runComponentActions(cell, triggerId, fingers)) return;
  }
  runPageSwitch(triggerId, fingers, target as HTMLElement | null);
}

function runComponentActions(cell: GridCellDefinition, triggerId: string, fingers: number) {
  const component = componentFor(cell)?.definition;
  if (!component || !currentPage.value || !scheme.value) return false;
  const actions = component.actionIds.map((id) => actionMap.value.get(id)).filter((item): item is ActionDefinition => Boolean(item));
  const matched = actions.filter((action) => triggerMatches(action.trigger, triggerId, fingers));
  for (const action of matched) {
    for (const invocation of action.invocations) {
      window.OneDeskNative?.callJsApi?.(
        invocation.targetDeviceId,
        invocation.capability,
        JSON.stringify(invocation.parameters ?? {}),
        scheme.value.id,
        currentPage.value.id,
        component.id,
      );
    }
  }
  return matched.length > 0;
}

function runPageSwitch(triggerId: string, fingers: number, target: HTMLElement | null) {
  const activeScheme = scheme.value;
  const page = currentPage.value;
  if (!activeScheme || !page || pages.value.length < 2) return;
  const edge = activeScheme.edges.find((item) => item.fromPageId === page.id && triggerMatches(item.trigger, triggerId, fingers));
  if (edge) {
    const index = pages.value.findIndex((item) => item.id === edge.toPageId);
    if (index >= 0) switchPage(index, edge.animation, target);
    return;
  }
  if (triggerMatches(activeScheme.globalPrevious.trigger, triggerId, fingers)) {
    switchPage((activePageIndex.value - 1 + pages.value.length) % pages.value.length, activeScheme.globalPrevious.animation, target);
  } else if (triggerMatches(activeScheme.globalNext.trigger, triggerId, fingers)) {
    switchPage((activePageIndex.value + 1) % pages.value.length, activeScheme.globalNext.animation, target);
  }
}

function switchPage(index: number, animation: string, target: HTMLElement | null) {
  transitionName.value = animation === "slide" ? "page-slide" : animation === "none" ? "page-none" : "page-fade";
  activePageIndex.value = index;
  target?.blur?.();
}

function triggerMatches(trigger: TriggerDefinition | undefined, actual: string, fingers: number) {
  if (!trigger) return false;
  const expectedFingers = Number(trigger.fingerCount || 1);
  if (expectedFingers > 0 && expectedFingers !== fingers) return false;
  if (trigger.id === actual) return true;
  if (trigger.id === "press-and-hold" && actual === "long-press") return true;
  if (trigger.id === "horizontal-swipe" && (actual.endsWith("swipe-left") || actual.endsWith("swipe-right"))) return true;
  if (trigger.id === "vertical-swipe" && (actual.endsWith("swipe-up") || actual.endsWith("swipe-down"))) return true;
  return false;
}

function swipeTrigger(dx: number, dy: number, fingers: number) {
  const direction = Math.abs(dx) > Math.abs(dy) ? (dx > 0 ? "right" : "left") : dy > 0 ? "down" : "up";
  return fingers > 1 ? `${fingers}-finger-swipe-${direction}` : `swipe-${direction}`;
}

function touchCenter(points: Touch[]) {
  if (!points.length) return { x: 0, y: 0 };
  return points.reduce((sum, point) => ({ x: sum.x + point.clientX / points.length, y: sum.y + point.clientY / points.length }), { x: 0, y: 0 });
}

function touchDistance(points: Touch[]) {
  return points.length < 2 ? 0 : Math.hypot(points[1].clientX - points[0].clientX, points[1].clientY - points[0].clientY);
}

function touchAngle(points: Touch[]) {
  return points.length < 2 ? 0 : Math.atan2(points[1].clientY - points[0].clientY, points[1].clientX - points[0].clientX) * 180 / Math.PI;
}

function normalizeAngle(value: number) {
  let result = value;
  while (result > 180) result -= 360;
  while (result < -180) result += 360;
  return result;
}

function findTrustedDesktop(targetHost: string, targetPort: number) {
  return desktops.value.find((item) => item.trusted && item.host === targetHost.trim() && item.port === Number(targetPort));
}

function parseResponse<T>(raw: string | undefined): NativeResponse<T> {
  if (!raw) return { ok: false, message: "当前功能必须通过 Android 原生壳子调用。" };
  try {
    return JSON.parse(raw) as NativeResponse<T>;
  } catch {
    return { ok: false, message: "原生壳子返回了无法识别的数据。" };
  }
}

function emptyScheme(desktopId: string): CachedScheme {
  return { desktopId, version: "0", hash: "", activeSchemeId: null, scheme: null, pages: [], components: [], actions: [] };
}

function cssUrl(value: string) {
  return value.replace(/[\\'\n\r]/g, (character) => `\\${character}`);
}

function clamp(value: number, minimum: number, maximum: number) {
  return Math.min(maximum, Math.max(minimum, Number.isFinite(value) ? value : minimum));
}
</script>

<template>
  <main v-if="!connected" class="min-h-screen overflow-auto bg-gradient-to-br from-sky-50 via-white to-cyan-50 px-4 py-5 text-slate-950">
    <section class="mx-auto flex min-h-[calc(100vh-40px)] max-w-md flex-col gap-4 p-1">
      <header class="px-1 pb-1 pt-2">
        <span class="grid size-11 place-items-center rounded-2xl bg-sky-500 text-white shadow-lg shadow-sky-500/25">
          <Icon icon="solar:widget-5-bold" class="size-6" />
        </span>
        <h1 class="mt-4 text-[20px] font-semibold">连接桌面端</h1>
        <p class="mt-1 text-[13px] leading-5 text-slate-500">首次连接需要验证码，之后可以直接选择已信任的桌面端。</p>
      </header>

      <section class="rounded-2xl bg-white p-4 shadow-sm ring-1 ring-slate-200/80">
        <div class="grid gap-3">
          <label class="grid gap-1.5 text-[13px] font-medium">
            桌面端 IP
            <input v-model="host" autocomplete="off" class="h-11 rounded-xl border border-slate-200 bg-slate-50 px-3 outline-none transition focus:border-sky-500 focus:ring-2 focus:ring-sky-500/15" placeholder="例如 192.168.1.10" />
          </label>
          <label class="grid gap-1.5 text-[13px] font-medium">
            端口
            <input v-model.number="port" inputmode="numeric" class="h-11 rounded-xl border border-slate-200 bg-slate-50 px-3 outline-none transition focus:border-sky-500 focus:ring-2 focus:ring-sky-500/15" />
          </label>
          <label class="grid gap-1.5 text-[13px] font-medium">
            6 位验证码
            <input v-model="code" inputmode="numeric" maxlength="6" autocomplete="one-time-code" class="h-11 rounded-xl border border-slate-200 bg-slate-50 px-3 tracking-[0.3em] outline-none transition focus:border-sky-500 focus:ring-2 focus:ring-sky-500/15" placeholder="000000" />
          </label>
        </div>
        <div class="mt-4 grid grid-cols-[1fr_44px] gap-3">
          <button :disabled="busy" class="h-11 rounded-xl bg-sky-500 text-[14px] font-semibold text-white shadow-sm transition active:scale-[0.98] disabled:opacity-60" @click="connect">
            {{ busy ? "正在连接" : "连接" }}
          </button>
          <button :disabled="busy || scanning" class="grid size-11 place-items-center rounded-xl border border-slate-200 bg-slate-50 text-slate-700 transition active:scale-95 disabled:opacity-60" aria-label="扫描桌面端二维码" @click="startQrScan">
            <Icon icon="solar:qr-code-bold" class="size-6" />
          </button>
        </div>
        <p class="mt-3 min-h-5 text-[12px] leading-5 text-slate-500">{{ message }}</p>
      </section>

      <section class="rounded-2xl bg-white p-4 shadow-sm ring-1 ring-slate-200/80">
        <h2 class="text-[14px] font-semibold">已信任的桌面端</h2>
        <div class="mt-3 grid gap-2">
          <button v-for="desktop in desktops" :key="desktop.desktopId" class="rounded-xl border border-slate-200 bg-slate-50 p-3 text-left transition active:scale-[0.99]" @click="connectKnown(desktop)">
            <div class="flex items-center justify-between gap-3">
              <span class="truncate text-[13px] font-medium">{{ desktop.name }}</span>
              <span class="shrink-0 text-[11px] font-medium text-emerald-600">{{ desktop.trusted ? "已信任" : "需验证" }}</span>
            </div>
            <p class="mt-1 text-[11px] text-slate-500">{{ desktop.host }}:{{ desktop.port }}</p>
          </button>
          <p v-if="desktops.length === 0" class="rounded-xl bg-slate-50 p-3 text-[12px] text-slate-500">暂无信任记录</p>
        </div>
      </section>
    </section>
  </main>

  <main v-else class="relative h-screen w-screen overflow-hidden bg-white text-slate-950">
    <Transition :name="transitionName" mode="out-in">
      <section
        v-if="currentPage"
        :key="currentPage.id"
        class="relative h-full w-full overflow-hidden touch-none"
        :style="pageBackgroundStyle(currentPage)"
        @touchstart="handleTouchStart('page', $event)"
        @touchmove.prevent="handleTouchMove('page', $event)"
        @touchend="handleTouchEnd('page', $event)"
        @touchcancel="handleTouchCancel('page')"
      >
        <video v-if="currentPage.backgroundKind === 'video' && currentPage.backgroundMediaSource" class="pointer-events-none absolute inset-0 size-full object-cover" :src="currentPage.backgroundMediaSource" autoplay muted loop playsinline />
        <div class="relative z-10 grid h-full w-full overflow-hidden" :style="currentGridStyle">
          <button
            v-for="cell in currentPage.cells"
            :key="cell.id"
            type="button"
            class="relative min-h-0 min-w-0 overflow-hidden bg-transparent p-0 text-left outline-none"
            :style="cellStyle(cell)"
            @touchstart.stop="handleTouchStart(`cell:${cell.id}`, $event)"
            @touchmove.stop.prevent="handleTouchMove(`cell:${cell.id}`, $event)"
            @touchend.stop="handleTouchEnd(`cell:${cell.id}`, $event)"
            @touchcancel.stop="handleTouchCancel(`cell:${cell.id}`)"
          >
            <article v-if="isVisualComponent(componentFor(cell))" class="relative size-full overflow-hidden" :style="componentStyle(componentFor(cell))">
              <video
                v-if="componentFor(cell)?.visualConfig?.background.kind === 'video' && componentFor(cell)?.visualConfig?.background.mediaSource"
                class="pointer-events-none absolute inset-0 size-full object-cover"
                :src="componentFor(cell)?.visualConfig?.background.mediaSource"
                autoplay
                muted
                loop
                playsinline
              />
              <img
                v-if="componentFor(cell)?.visualConfig?.image.source"
                class="pointer-events-none absolute inset-0 size-full"
                :src="componentFor(cell)?.visualConfig?.image.source"
                :style="imageStyle(componentFor(cell)!.visualConfig!)"
                alt=""
              />
              <span
                v-for="text in componentFor(cell)?.visualConfig?.texts ?? []"
                :key="text.id"
                class="pointer-events-none absolute z-10 grid -translate-x-1/2 -translate-y-1/2 place-items-center overflow-hidden text-center leading-tight break-words"
                :style="textStyle(text)"
              >{{ text.content }}</span>
            </article>
            <div v-else-if="cell.componentId" class="grid size-full place-items-center bg-slate-100 p-3 text-center text-[11px] text-slate-500">
              该代码组件暂不包含可直接渲染的移动端产物
            </div>
          </button>
        </div>
      </section>

      <section v-else key="empty" class="grid h-full w-full place-items-center bg-white p-6 text-center">
        <div class="max-w-xs">
          <span class="mx-auto grid size-12 place-items-center rounded-2xl bg-slate-100 text-slate-400">
            <Icon icon="solar:widget-6-linear" class="size-6" />
          </span>
          <p class="mt-4 text-[14px] font-semibold">还没有应用方案</p>
          <p class="mt-2 text-[12px] leading-5 text-slate-500">请在桌面端为这台设备选择并应用方案，应用后会自动显示。</p>
        </div>
      </section>
    </Transition>
  </main>
</template>

<style scoped>
.page-fade-enter-active,
.page-fade-leave-active {
  transition: opacity 180ms ease;
}

.page-fade-enter-from,
.page-fade-leave-to {
  opacity: 0;
}

.page-slide-enter-active,
.page-slide-leave-active {
  transition: transform 210ms ease, opacity 210ms ease;
}

.page-slide-enter-from {
  transform: translateX(4%);
  opacity: 0;
}

.page-slide-leave-to {
  transform: translateX(-4%);
  opacity: 0;
}

.page-none-enter-active,
.page-none-leave-active {
  transition: none;
}
</style>
