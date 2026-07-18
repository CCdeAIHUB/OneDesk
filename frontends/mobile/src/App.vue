<script setup lang="ts">
import { Icon } from "@iconify/vue";
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from "vue";
import CodeComponentFrame from "./components/CodeComponentFrame.vue";
import type { ActionDefinition, CachedScheme, CodeJsApiRequest, ComponentBundle, GridCellDefinition, KnownDesktop, NativeResponse, PageDefinition } from "./domain";
import { GestureRecognizer, triggerMatches } from "./gestureRecognizer";
import { buildGridStyle, cellStyle, componentStyle, imageStyle, pageBackgroundStyle, textStyle } from "./schemeRenderer";

declare global {
  interface Window {
    OneDeskNative?: {
      listKnownDesktops?: () => NativeBridgeValue;
      connect?: (host: string, port: number, code: string) => NativeBridgeValue;
      connectByQr?: (qrPayload: string) => NativeBridgeValue;
      startQrScan?: () => NativeBridgeValue;
      cancelQrScan?: () => NativeBridgeValue;
      getCachedScheme?: (desktopId: string) => NativeBridgeValue;
      refreshScheme?: (desktopId: string) => NativeBridgeValue;
      setDisplayRatio?: (width: number, height: number) => NativeBridgeValue;
      callJsApi?: (
        targetDeviceId: string,
        capability: string,
        payloadJson: string,
        schemeId: string,
        pageId: string,
        componentId: string,
      ) => NativeBridgeValue;
    };
    __oneDeskHandleQrScan?: (payload: string | null, error: string | null) => void;
    __oneDeskHandleSchemeUpdated?: (desktopId: string, version: string, hash: string) => void;
    __oneDeskHandlePageSwitch?: (payload: Record<string, unknown>) => void;
    __oneDeskHandleInAppNotification?: (payload: Record<string, unknown>) => void;
    __oneDeskHandleDeviceTrigger?: (payload: { triggerId?: string }) => void;
  }
}

type NativeBridgeValue = string | Promise<string>;

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
const notificationText = ref("");
let schemeRefreshTimer = 0;
let notificationTimer = 0;

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
const gestureRecognizer = new GestureRecognizer(dispatchGesture);

// 页面宽高比是桌面设计结果的一部分；移动端据此请求原生壳子切换横竖屏。
watch(currentPage, (page) => {
  if (!page) return;
  window.OneDeskNative?.setDisplayRatio?.(page.previewRatioWidth ?? 21, page.previewRatioHeight ?? 9);
});
const currentGridStyle = computed<Record<string, string>>(() => {
  const page = currentPage.value;
  if (!page) return {} as Record<string, string>;
  return buildGridStyle(page, viewportWidth.value, viewportHeight.value);
});

onMounted(() => {
  void loadKnownDesktops();
  window.addEventListener("resize", updateViewport);
  window.__oneDeskHandleQrScan = handleQrScan;
  window.__oneDeskHandleSchemeUpdated = handleSchemeUpdated;
  window.__oneDeskHandlePageSwitch = handleNativePageSwitch;
  window.__oneDeskHandleInAppNotification = handleInAppNotification;
  window.__oneDeskHandleDeviceTrigger = handleDeviceTrigger;
});

onUnmounted(() => {
  if (schemeRefreshTimer) window.clearInterval(schemeRefreshTimer);
  if (notificationTimer) window.clearTimeout(notificationTimer);
  gestureRecognizer.dispose();
  window.OneDeskNative?.cancelQrScan?.();
  delete window.__oneDeskHandleQrScan;
  delete window.__oneDeskHandleSchemeUpdated;
  delete window.__oneDeskHandlePageSwitch;
  delete window.__oneDeskHandleInAppNotification;
  delete window.__oneDeskHandleDeviceTrigger;
  window.removeEventListener("resize", updateViewport);
});

function updateViewport() {
  viewportWidth.value = window.innerWidth;
  viewportHeight.value = window.innerHeight;
}

async function loadKnownDesktops() {
  try {
    const raw = await Promise.resolve(window.OneDeskNative?.listKnownDesktops?.() ?? "[]");
    desktops.value = JSON.parse(raw) as KnownDesktop[];
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

async function startQrScan() {
  if (!window.OneDeskNative?.startQrScan) {
    message.value = "二维码扫描必须在移动客户端中使用。";
    return;
  }
  const response = parseResponse<{ started: boolean }>(await Promise.resolve(window.OneDeskNative.startQrScan()));
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

async function performConnection(request: () => NativeBridgeValue | undefined) {
  if (busy.value) return;
  if (scanning.value) {
    window.OneDeskNative?.cancelQrScan?.();
    scanning.value = false;
  }
  busy.value = true;
  message.value = "正在连接并校验方案缓存...";
  await new Promise<void>((resolve) => window.setTimeout(resolve, 0));
  try {
      const raw = await Promise.resolve(request());
      const response = parseResponse<{ desktop: KnownDesktop; hasScheme: boolean }>(raw);
      if (!response.ok || !response.payload) {
        message.value = response.message ?? "连接失败，请检查 IP、端口和验证码。";
        return;
      }
      await loadKnownDesktops();
      await loadScheme(response.payload.desktop.desktopId);
      connected.value = true;
      activePageIndex.value = 0;
      startSchemeRefresh(response.payload.desktop.desktopId);
      void nextTick(updateViewport);
  } catch (error) {
    message.value = error instanceof Error ? error.message : "连接失败，请检查桌面端状态。";
  } finally {
    busy.value = false;
  }
}

async function loadScheme(desktopId: string) {
  const raw = await Promise.resolve(window.OneDeskNative?.getCachedScheme?.(desktopId));
  const response = parseResponse<CachedScheme>(raw);
  cachedScheme.value = response.ok && response.payload ? response.payload : emptyScheme(desktopId);
  if (activePageIndex.value >= pages.value.length) activePageIndex.value = 0;
}

function handleSchemeUpdated(desktopId: string) {
  void loadScheme(desktopId);
  activePageIndex.value = 0;
}

function handleNativePageSwitch(payload: Record<string, unknown>) {
  const requestedId = typeof payload.pageId === "string" ? payload.pageId : "";
  const requestedIndex = Number(payload.index);
  let index = requestedId ? pages.value.findIndex((page) => page.id === requestedId) : Number.isInteger(requestedIndex) ? requestedIndex : -1;
  if (index < 0 && payload.direction === "next") index = (activePageIndex.value + 1) % Math.max(1, pages.value.length);
  if (index < 0 && payload.direction === "previous") index = (activePageIndex.value - 1 + pages.value.length) % Math.max(1, pages.value.length);
  if (index >= 0 && index < pages.value.length) switchPage(index, String(payload.animation || "fade"), null);
}

function handleInAppNotification(payload: Record<string, unknown>) {
  const title = typeof payload.title === "string" ? payload.title.trim() : "";
  const body = typeof payload.message === "string" ? payload.message.trim() : "";
  showNotification([title, body].filter(Boolean).join("：") || "操作已完成");
}

function handleDeviceTrigger(payload: { triggerId?: string }) {
  const triggerId = payload.triggerId?.trim();
  if (!triggerId) return;
  let consumed = false;
  for (const cell of currentPage.value?.cells ?? []) {
    consumed = runComponentActions(cell, triggerId, 1) || consumed;
  }
  if (!consumed) runPageSwitch(triggerId, 1, null);
}

function showNotification(text: string) {
  notificationText.value = text;
  if (notificationTimer) window.clearTimeout(notificationTimer);
  notificationTimer = window.setTimeout(() => {
    notificationText.value = "";
  }, 3200);
}

function startSchemeRefresh(desktopId: string) {
  if (schemeRefreshTimer) window.clearInterval(schemeRefreshTimer);
  schemeRefreshTimer = window.setInterval(async () => {
    const raw = await Promise.resolve(window.OneDeskNative?.refreshScheme?.(desktopId));
    const response = parseResponse<{ cacheUpdated: boolean }>(raw);
    if (response.ok && response.payload?.cacheUpdated) await loadScheme(desktopId);
  }, 10_000);
}

function componentFor(cell: GridCellDefinition) {
  return cell.componentId ? componentMap.value.get(cell.componentId) : undefined;
}

function isVisualComponent(bundle: ComponentBundle | undefined) {
  return Boolean(bundle?.visualConfig);
}

function isCodeComponent(bundle: ComponentBundle | undefined) {
  return Boolean(bundle?.codeRuntime?.code);
}

function runCodeComponentJsApi(componentId: string, request: CodeJsApiRequest) {
  if (!currentPage.value || !scheme.value) {
    request.respond(JSON.stringify({ ok: false, errorCode: "SchemeUnavailable", message: "当前方案不可用" }));
    return;
  }
  const response = window.OneDeskNative?.callJsApi?.(
    request.targetDeviceId,
    request.capability,
    JSON.stringify(request.payload),
    scheme.value.id,
    currentPage.value.id,
    componentId,
  ) ?? JSON.stringify({ ok: false, errorCode: "NativeBridgeUnavailable", message: "原生壳子不可用" });
  void Promise.resolve(response)
    .then(request.respond)
    .catch((error) => request.respond(JSON.stringify({
      ok: false,
      errorCode: "NativeBridgeFailed",
      message: error instanceof Error ? error.message : "原生壳子调用失败",
    })));
}

function handleTouchStart(key: string, event: TouchEvent) {
  gestureRecognizer.start(key, event);
}

function handleTouchMove(key: string, event: TouchEvent) {
  gestureRecognizer.move(key, event);
}

function handleTouchEnd(key: string, event: TouchEvent) {
  gestureRecognizer.end(key, event);
}

function handleTouchCancel(key: string) {
  gestureRecognizer.cancel(key);
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
  if (matched.length > 0) void invokeComponentActions(matched, component.id, scheme.value.id, currentPage.value.id);
  return matched.length > 0;
}

async function invokeComponentActions(actions: ActionDefinition[], componentId: string, schemeId: string, pageId: string) {
  for (const action of actions) {
    for (const invocation of action.invocations) {
      const raw = await Promise.resolve(window.OneDeskNative?.callJsApi?.(
        invocation.targetDeviceId,
        invocation.capability,
        JSON.stringify(invocation.parameters ?? {}),
        schemeId,
        pageId,
        componentId,
      ));
      const result = parseResponse(raw);
      if (!result.ok) showNotification(result.message ?? `动作执行失败：${invocation.capability}`);
    }
  }
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

</script>

<template>
  <main v-if="!connected" class="min-h-screen overflow-auto bg-gradient-to-br from-sky-50 via-white to-cyan-50 px-4 py-5 text-slate-950">
    <section class="flex min-h-[calc(100vh-40px)] w-full flex-col gap-4 p-1">
      <header class="px-1 pb-1 pt-2">
        <span class="grid size-11 place-items-center rounded-2xl bg-sky-500 text-white shadow-lg shadow-sky-500/25">
          <Icon icon="solar:widget-5-bold" class="size-6" />
        </span>
        <h1 class="mt-4 text-[20px] font-semibold">连接桌面端</h1>
        <p class="mt-1 text-[13px] leading-5 text-slate-500">首次连接需要验证码，之后可以直接选择已信任的桌面端。</p>
      </header>

      <section class="px-1 py-2">
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

      <section class="px-1 py-2">
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
          <div
            v-for="cell in currentPage.cells"
            :key="cell.id"
            role="button"
            tabindex="0"
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
            <CodeComponentFrame
              v-else-if="isCodeComponent(componentFor(cell))"
              :runtime="componentFor(cell)!.codeRuntime!"
              @jsapi="runCodeComponentJsApi(componentFor(cell)!.definition.id, $event)"
              @trigger="runComponentActions(cell, $event.triggerId, $event.fingers)"
            />
            <div v-else-if="cell.componentId" class="grid size-full place-items-center bg-slate-100 p-3 text-center text-[11px] text-slate-500">
              代码组件产物无效，请在桌面端重新保存构建
            </div>
          </div>
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
    <Transition name="native-toast">
      <div v-if="notificationText" class="pointer-events-none absolute left-1/2 top-5 z-50 max-w-[calc(100vw-32px)] -translate-x-1/2 rounded-xl bg-slate-950/90 px-4 py-2.5 text-center text-[12px] leading-5 text-white shadow-lg backdrop-blur-sm">
        {{ notificationText }}
      </div>
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

.native-toast-enter-active,
.native-toast-leave-active {
  transition: opacity 160ms ease, transform 160ms ease;
}

.native-toast-enter-from,
.native-toast-leave-to {
  opacity: 0;
  transform: translate(-50%, -6px);
}
</style>
