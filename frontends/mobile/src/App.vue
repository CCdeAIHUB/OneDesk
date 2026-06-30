<script setup lang="ts">
import { Icon } from "@iconify/vue";
import { computed, onMounted, ref } from "vue";

interface KnownDesktop {
  desktopId: string;
  name: string;
  host: string;
  port: number;
  trusted: boolean;
  schemeVersion: string;
  schemeHash: string;
}

interface CachedTile {
  label: string;
  icon: string;
  accent: string;
}

interface CachedPage {
  name: string;
  tiles: CachedTile[];
}

interface CachedScheme {
  desktopId: string;
  version: string;
  hash: string;
  pages: CachedPage[];
}

interface NativeResponse<T> {
  ok: boolean;
  payload?: T;
  message?: string;
}

declare global {
  interface Window {
    OneDeskNative?: {
      listKnownDesktops?: () => string;
      connect?: (host: string, port: number, code: string) => string;
      connectByQr?: (qrPayload: string) => string;
      getCachedScheme?: (desktopId: string) => string;
    };
  }
}

const connected = ref(false);
const updating = ref(false);
const updateProgress = ref(0);
const activePage = ref(0);
const host = ref("");
const port = ref(48320);
const code = ref("");
const qrPayload = ref("");
const message = ref("请输入桌面端显示的 IP、端口和 6 位验证码。");
const desktops = ref<KnownDesktop[]>([]);
const cachedScheme = ref<CachedScheme | null>(null);

const pages = computed<CachedPage[]>(() => cachedScheme.value?.pages ?? []);
const currentPage = computed(() => pages.value[activePage.value] ?? pages.value[0] ?? null);

onMounted(loadKnownDesktops);

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

  updating.value = true;
  updateProgress.value = 12;
  message.value = "正在连接桌面端...";
  const timer = window.setInterval(() => {
    updateProgress.value += 18;
    if (updateProgress.value >= 100) {
      window.clearInterval(timer);
      updateProgress.value = 100;
      finishConnect();
    }
  }, 160);
}

function connectWithQr() {
  if (!qrPayload.value.trim()) {
    message.value = "请先粘贴或扫描二维码内容。";
    return;
  }

  updating.value = true;
  updateProgress.value = 40;
  message.value = "正在读取二维码连接信息...";
  window.setTimeout(() => finishConnect(qrPayload.value), 260);
}

function finishConnect(qr?: string) {
  const raw = qr
    ? window.OneDeskNative?.connectByQr?.(qr)
    : window.OneDeskNative?.connect?.(host.value, port.value, code.value);
  const response = raw ? (JSON.parse(raw) as NativeResponse<{ desktop: KnownDesktop }>) : {
    ok: false,
    message: "移动端连接必须通过 Android 原生壳子转发。",
  };

  if (!response.ok || !response.payload) {
    message.value = response.message ?? "连接失败，请检查 IP、端口和验证码。";
    updating.value = false;
    return;
  }

  loadKnownDesktops();
  loadScheme(response.payload.desktop.desktopId);
  connected.value = true;
  updating.value = false;
}

function loadScheme(desktopId: string) {
  const raw = window.OneDeskNative?.getCachedScheme?.(desktopId);
  if (!raw) {
    cachedScheme.value = { desktopId, version: "0", hash: "", pages: [] };
    return;
  }

  const response = JSON.parse(raw) as NativeResponse<CachedScheme>;
  cachedScheme.value = response.ok && response.payload
    ? response.payload
    : { desktopId, version: "0", hash: "", pages: [] };
}

function connectKnown(desktop: KnownDesktop) {
  host.value = desktop.host;
  port.value = desktop.port;
  code.value = "000000";
  if (!desktop.trusted) {
    message.value = "该桌面端需要重新输入验证码。";
    return;
  }
  updating.value = true;
  updateProgress.value = 45;
  message.value = "正在使用信任凭据连接...";
  window.setTimeout(() => finishConnect(), 120);
}

function accentClass(accent: string) {
  return {
    rose: "bg-rose-500",
    sky: "bg-sky-500",
    emerald: "bg-emerald-500",
    amber: "bg-amber-500",
    fuchsia: "bg-fuchsia-500",
    cyan: "bg-cyan-500",
    violet: "bg-violet-500",
  }[accent] ?? "bg-sky-500";
}
</script>

<template>
  <main v-if="!connected" class="min-h-screen bg-gradient-to-br from-sky-50 via-white to-cyan-50 px-4 py-5 text-slate-950">
    <section class="mx-auto flex min-h-[calc(100vh-40px)] max-w-md flex-col gap-4 overflow-auto rounded-[32px] border border-white/60 bg-white/70 p-5 shadow-2xl shadow-sky-950/12 backdrop-blur-2xl">
      <div class="rounded-3xl bg-sky-500 p-5 text-white shadow-xl shadow-sky-500/25">
        <Icon icon="solar:smartphone-update-bold-duotone" class="size-11" />
        <h1 class="mt-4 text-[22px] font-semibold">连接桌面端</h1>
        <p class="mt-2 text-sm text-sky-50">首次连接需要输入桌面端显示的验证码；信任后可直接重连。</p>
      </div>

      <div class="rounded-3xl bg-white p-4 shadow-lg shadow-sky-950/8">
        <div class="grid gap-3">
          <label class="grid gap-1 text-sm font-medium">
            桌面端 IP
            <input v-model="host" class="rounded-2xl bg-slate-100 px-4 py-3 outline-none ring-sky-500 focus:ring-2" />
          </label>
          <label class="grid gap-1 text-sm font-medium">
            端口
            <input v-model.number="port" inputmode="numeric" class="rounded-2xl bg-slate-100 px-4 py-3 outline-none ring-sky-500 focus:ring-2" />
          </label>
          <label class="grid gap-1 text-sm font-medium">
            6 位验证码
            <input v-model="code" inputmode="numeric" maxlength="6" class="rounded-2xl bg-slate-100 px-4 py-3 tracking-[0.35em] outline-none ring-sky-500 focus:ring-2" />
          </label>
        </div>
        <div class="mt-4 grid grid-cols-[1fr_auto] gap-3">
          <button class="rounded-2xl bg-sky-500 py-3 font-semibold text-white" @click="connect">连接</button>
          <button class="grid size-12 place-items-center rounded-2xl bg-slate-100" @click="connectWithQr">
            <Icon icon="solar:qr-code-bold-duotone" class="size-6" />
          </button>
        </div>
        <input v-model="qrPayload" placeholder="二维码内容 onedesk://pair?..." class="mt-3 w-full rounded-2xl bg-slate-100 px-4 py-3 text-sm outline-none" />
        <div v-if="updating" class="mt-4">
          <div class="h-2 overflow-hidden rounded-full bg-slate-100">
            <div class="h-full rounded-full bg-sky-500 transition-all" :style="{ width: `${updateProgress}%` }"></div>
          </div>
          <p class="mt-2 text-xs text-slate-500">正在建立连接 {{ updateProgress }}%</p>
        </div>
        <p class="mt-3 text-xs leading-5 text-slate-500">{{ message }}</p>
      </div>

      <div class="rounded-3xl bg-white p-4 shadow-lg shadow-sky-950/8">
        <h2 class="font-semibold">已信任的桌面端</h2>
        <div class="mt-3 grid gap-2">
          <button v-for="desktop in desktops" :key="desktop.desktopId" class="rounded-2xl bg-slate-100 p-3 text-left" @click="connectKnown(desktop)">
            <div class="flex items-center justify-between">
              <span class="font-medium">{{ desktop.name }}</span>
              <span class="rounded-full bg-emerald-100 px-2 py-1 text-xs font-semibold text-emerald-600">
                {{ desktop.trusted ? "已信任" : "需验证" }}
              </span>
            </div>
            <p class="mt-1 text-xs text-slate-500">{{ desktop.host }}:{{ desktop.port }} · 方案 {{ desktop.schemeVersion }}</p>
          </button>
          <p v-if="desktops.length === 0" class="rounded-2xl bg-slate-100 p-3 text-sm text-slate-500">暂无信任记录</p>
        </div>
      </div>
    </section>
  </main>

  <main v-else class="min-h-screen bg-white text-slate-950">
    <section v-if="currentPage && currentPage.tiles.length > 0" class="grid h-screen w-screen grid-cols-2 grid-rows-2 gap-3 overflow-hidden p-3">
      <button v-for="tile in currentPage.tiles" :key="tile.label" class="flex min-h-0 flex-col items-center justify-center overflow-hidden rounded-[28px] bg-slate-100 p-4 active:scale-[0.98]">
        <span :class="['grid size-14 place-items-center rounded-2xl text-white shadow-lg', accentClass(tile.accent)]">
          <Icon :icon="tile.icon" class="size-8" />
        </span>
        <span class="mt-4 text-lg font-semibold">{{ tile.label }}</span>
      </button>
    </section>

    <section v-else class="grid h-screen w-screen place-items-center bg-white p-6 text-center">
      <div>
        <Icon icon="solar:folder-error-bold-duotone" class="mx-auto size-12 text-slate-400" />
        <p class="mt-3 text-sm font-semibold">当前没有可显示的方案</p>
        <p class="mt-2 text-xs leading-5 text-slate-500">请在桌面端应用方案到该移动设备后重新连接。</p>
      </div>
    </section>
  </main>
</template>
