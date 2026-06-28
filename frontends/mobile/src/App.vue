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
  lastConnectedAt: string;
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
  updatedAt: string;
  pages: CachedPage[];
}

interface NativeResponse<T> {
  ok: boolean;
  payload?: T;
  errorCode?: string;
  message?: string;
}

declare global {
  interface Window {
    OneDeskNative?: {
      listKnownDesktops?: () => string;
      connect?: (host: string, port: number, code: string) => string;
      connectByQr?: (qrPayload: string) => string;
      getCachedScheme?: (desktopId: string) => string;
      getDeviceId?: () => string;
    };
  }
}

const connected = ref(false);
const updating = ref(false);
const updateProgress = ref(0);
const activePage = ref(0);
const theme = ref<"light" | "dark">("light");
const host = ref("192.168.1.24");
const port = ref(48320);
const code = ref("482913");
const qrPayload = ref("");
const toast = ref("每次打开都会先进入连接页");
const deviceId = ref("android-preview");
const desktops = ref<KnownDesktop[]>([]);
const cachedScheme = ref<CachedScheme | null>(null);

const fallbackPages: CachedPage[] = [
  {
    name: "预览",
    tiles: [
      { label: "录制", icon: "solar:record-circle-bold-duotone", accent: "rose" },
      { label: "场景", icon: "solar:layers-bold-duotone", accent: "sky" },
      { label: "麦克风", icon: "solar:microphone-3-bold-duotone", accent: "emerald" },
      { label: "标记", icon: "solar:bookmark-bold-duotone", accent: "amber" },
    ],
  },
];
const pages = computed<CachedPage[]>(() => cachedScheme.value?.pages.length ? cachedScheme.value.pages : fallbackPages);
const currentPage = computed(() => pages.value[activePage.value] ?? pages.value[0]);

onMounted(() => {
  document.documentElement.classList.toggle("dark", theme.value === "dark");
  deviceId.value = window.OneDeskNative?.getDeviceId?.() ?? "android-preview";
  loadKnownDesktops();
});

function loadKnownDesktops() {
  try {
    desktops.value = JSON.parse(window.OneDeskNative?.listKnownDesktops?.() ?? "[]") as KnownDesktop[];
  } catch {
    desktops.value = [];
  }
}

function setTheme(next: "light" | "dark") {
  theme.value = next;
  document.documentElement.classList.toggle("dark", next === "dark");
}

function connect() {
  updating.value = true;
  updateProgress.value = 12;
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
    toast.value = "请先粘贴或扫描二维码内容";
    return;
  }

  updating.value = true;
  updateProgress.value = 40;
  window.setTimeout(() => finishConnect(qrPayload.value), 260);
}

function finishConnect(qr?: string) {
  const raw = qr
    ? window.OneDeskNative?.connectByQr?.(qr)
    : window.OneDeskNative?.connect?.(host.value, port.value, code.value);
  const response = raw ? (JSON.parse(raw) as NativeResponse<{ desktop: KnownDesktop; cacheUpdated: boolean }>) : {
    ok: false,
    message: "移动端连接必须通过原生壳子转发，网页预览不能模拟连接成功。",
  };

  if (!response.ok || !response.payload) {
    toast.value = response.message ?? "连接失败";
    updating.value = false;
    return;
  }

  loadKnownDesktops();
  loadScheme(response.payload.desktop.desktopId);
  connected.value = true;
  updating.value = false;
  toast.value = response.payload.cacheUpdated ? "方案缓存已更新" : "已使用本地方案缓存";
}

function loadScheme(desktopId: string) {
  const raw = window.OneDeskNative?.getCachedScheme?.(desktopId);
  if (!raw) return;
  const response = JSON.parse(raw) as NativeResponse<CachedScheme>;
  if (response.ok && response.payload) {
    cachedScheme.value = response.payload;
  }
}

function connectKnown(desktop: KnownDesktop) {
  host.value = desktop.host;
  port.value = desktop.port;
  code.value = "000000";
  if (!desktop.trusted) {
    toast.value = "需要重新输入验证码";
    return;
  }
  updating.value = true;
  updateProgress.value = 45;
  window.setTimeout(() => finishConnect(), 120);
}

function nextPage() {
  activePage.value = (activePage.value + 1) % pages.value.length;
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
  <main class="min-h-screen px-4 py-5 text-slate-950 dark:text-slate-100">
    <section class="mx-auto flex min-h-[calc(100vh-40px)] max-w-md flex-col overflow-hidden rounded-[32px] border border-white/50 bg-white/60 shadow-2xl shadow-sky-950/12 backdrop-blur-2xl dark:border-white/10 dark:bg-black/70">
      <header class="flex items-center justify-between bg-white px-5 py-4 dark:bg-slate-950">
        <div class="flex items-center gap-3">
          <div class="grid size-10 place-items-center rounded-2xl bg-sky-500 text-white"><Icon icon="solar:command-bold-duotone" class="size-6" /></div>
          <div>
            <h1 class="font-semibold">OneDesk</h1>
            <p class="text-xs text-slate-500 dark:text-slate-400">{{ connected ? currentPage.name : "连接桌面端" }}</p>
          </div>
        </div>
        <button class="grid size-10 place-items-center rounded-2xl bg-slate-100 dark:bg-slate-900" @click="setTheme(theme === 'light' ? 'dark' : 'light')">
          <Icon :icon="theme === 'light' ? 'solar:moon-bold-duotone' : 'solar:sun-2-bold-duotone'" class="size-5" />
        </button>
      </header>

      <section v-if="!connected" class="flex flex-1 flex-col gap-4 overflow-auto p-5">
        <div class="rounded-3xl bg-sky-500 p-5 text-white shadow-xl shadow-sky-500/25">
          <Icon icon="solar:smartphone-update-bold-duotone" class="size-11" />
          <h2 class="mt-4 text-[22px] font-semibold">先连接，再显示方案</h2>
          <p class="mt-2 text-sm text-sky-50">移动端每次打开进入连接页，校验方案缓存后再显示控制界面。</p>
        </div>

        <div class="rounded-3xl bg-white p-4 shadow-lg shadow-sky-950/8 dark:bg-slate-950">
          <div class="grid gap-3">
            <label class="grid gap-1 text-sm font-medium">桌面 IP<input v-model="host" class="rounded-2xl bg-slate-100 px-4 py-3 outline-none ring-sky-500 focus:ring-2 dark:bg-slate-900" /></label>
            <label class="grid gap-1 text-sm font-medium">端口<input v-model.number="port" inputmode="numeric" class="rounded-2xl bg-slate-100 px-4 py-3 outline-none ring-sky-500 focus:ring-2 dark:bg-slate-900" /></label>
            <label class="grid gap-1 text-sm font-medium">6 位验证码<input v-model="code" inputmode="numeric" maxlength="6" class="rounded-2xl bg-slate-100 px-4 py-3 tracking-[0.35em] outline-none ring-sky-500 focus:ring-2 dark:bg-slate-900" /></label>
          </div>
          <div class="mt-4 grid grid-cols-[1fr_auto] gap-3">
            <button class="rounded-2xl bg-sky-500 py-3 font-semibold text-white" @click="connect">连接</button>
            <button class="grid size-12 place-items-center rounded-2xl bg-slate-100 dark:bg-slate-900" @click="connectWithQr"><Icon icon="solar:qr-code-bold-duotone" class="size-6" /></button>
          </div>
          <input v-model="qrPayload" placeholder="二维码内容 onedesk://pair?..." class="mt-3 w-full rounded-2xl bg-slate-100 px-4 py-3 text-sm outline-none dark:bg-slate-900" />
          <div v-if="updating" class="mt-4">
            <div class="h-2 overflow-hidden rounded-full bg-slate-100 dark:bg-slate-900"><div class="h-full rounded-full bg-sky-500 transition-all" :style="{ width: `${updateProgress}%` }"></div></div>
            <p class="mt-2 text-xs text-slate-500">正在校验并更新方案缓存 {{ updateProgress }}%</p>
          </div>
        </div>

        <div class="rounded-3xl bg-white p-4 shadow-lg shadow-sky-950/8 dark:bg-slate-950">
          <h3 class="font-semibold">已连接过的桌面端</h3>
          <div class="mt-3 grid gap-2">
            <button v-for="desktop in desktops" :key="desktop.desktopId" class="rounded-2xl bg-slate-100 p-3 text-left dark:bg-slate-900" @click="connectKnown(desktop)">
              <div class="flex items-center justify-between"><span class="font-medium">{{ desktop.name }}</span><span class="rounded-full bg-emerald-100 px-2 py-1 text-xs font-semibold text-emerald-600 dark:bg-emerald-950 dark:text-emerald-300">{{ desktop.trusted ? "已信任" : "需验证" }}</span></div>
              <p class="mt-1 text-xs text-slate-500">{{ desktop.host }}:{{ desktop.port }} · 方案 {{ desktop.schemeVersion }}</p>
            </button>
            <p v-if="desktops.length === 0" class="rounded-2xl bg-slate-100 p-3 text-sm text-slate-500 dark:bg-slate-900">暂无连接记录</p>
          </div>
        </div>
      </section>

      <section v-else class="flex flex-1 flex-col overflow-hidden bg-white p-5 dark:bg-slate-950">
        <div class="mb-4 flex items-center justify-between">
          <div><p class="text-sm text-slate-500">设备 {{ deviceId }}</p><h2 class="text-2xl font-semibold">{{ currentPage.name }}</h2></div>
          <button class="rounded-2xl bg-slate-100 px-4 py-3 text-sm font-semibold dark:bg-slate-900" @click="nextPage">下一页</button>
        </div>
        <div class="grid flex-1 grid-cols-2 grid-rows-2 gap-3 overflow-hidden">
          <button v-for="tile in currentPage.tiles" :key="tile.label" class="flex flex-col items-center justify-center overflow-hidden rounded-[28px] bg-slate-100 p-4 active:scale-[0.98] dark:bg-slate-900">
            <span :class="['grid size-14 place-items-center rounded-2xl text-white shadow-lg', accentClass(tile.accent)]"><Icon :icon="tile.icon" class="size-8" /></span>
            <span class="mt-4 text-lg font-semibold">{{ tile.label }}</span>
          </button>
        </div>
        <div class="mt-4 flex items-center justify-between text-xs text-slate-500">
          <span>{{ toast }}</span>
          <div class="flex gap-2"><span v-for="(_, index) in pages" :key="index" class="h-2 rounded-full transition-all" :class="index === activePage ? 'w-6 bg-sky-500' : 'w-2 bg-slate-300 dark:bg-slate-700'"></span></div>
        </div>
      </section>
    </section>
  </main>
</template>
