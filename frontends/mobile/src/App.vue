<script setup lang="ts">
import { Icon } from "@iconify/vue";
import { computed, ref } from "vue";

const connected = ref(false);
const updating = ref(false);
const updateProgress = ref(0);
const activePage = ref(0);
const theme = ref<"light" | "dark">("light");

const desktops = [
  { name: "Studio PC", address: "192.168.1.24:48320", scheme: "Studio Flow", status: "Trusted" },
  { name: "Laptop", address: "192.168.1.41:48320", scheme: "Travel Deck", status: "Cached" },
];

const pages = [
  {
    name: "Capture",
    tiles: [
      { label: "Record", icon: "solar:record-circle-bold-duotone", accent: "bg-rose-500" },
      { label: "Scene", icon: "solar:layers-bold-duotone", accent: "bg-sky-500" },
      { label: "Mic", icon: "solar:microphone-3-bold-duotone", accent: "bg-emerald-500" },
      { label: "Mark", icon: "solar:bookmark-bold-duotone", accent: "bg-amber-500" },
    ],
  },
  {
    name: "Live",
    tiles: [
      { label: "Chat", icon: "solar:chat-round-bold-duotone", accent: "bg-sky-500" },
      { label: "Clip", icon: "solar:video-frame-cut-bold-duotone", accent: "bg-fuchsia-500" },
      { label: "Music", icon: "solar:music-note-2-bold-duotone", accent: "bg-cyan-500" },
      { label: "Break", icon: "solar:pause-circle-bold-duotone", accent: "bg-violet-500" },
    ],
  },
];

const currentPage = computed(() => pages[activePage.value]);

function setTheme(next: "light" | "dark") {
  theme.value = next;
  document.documentElement.classList.toggle("dark", next === "dark");
}

function connect() {
  updating.value = true;
  updateProgress.value = 12;
  const timer = window.setInterval(() => {
    updateProgress.value += 22;
    if (updateProgress.value >= 100) {
      window.clearInterval(timer);
      updateProgress.value = 100;
      window.setTimeout(() => {
        updating.value = false;
        connected.value = true;
      }, 350);
    }
  }, 240);
}

function nextPage() {
  activePage.value = (activePage.value + 1) % pages.length;
}
</script>

<template>
  <main class="min-h-screen px-4 py-5 text-slate-950 dark:text-slate-100">
    <section class="mx-auto flex min-h-[calc(100vh-40px)] max-w-md flex-col overflow-hidden rounded-[32px] border border-white/50 bg-white/46 shadow-2xl shadow-sky-950/12 backdrop-blur-2xl dark:border-white/10 dark:bg-slate-950/46">
      <header class="flex items-center justify-between bg-white px-5 py-4 dark:bg-slate-950">
        <div class="flex items-center gap-3">
          <div class="grid size-10 place-items-center rounded-2xl bg-sky-500 text-white">
            <Icon icon="solar:command-bold-duotone" class="size-6" />
          </div>
          <div>
            <h1 class="font-semibold">OneDesk</h1>
            <p class="text-xs text-slate-500 dark:text-slate-400">{{ connected ? currentPage.name : "Connect to desktop" }}</p>
          </div>
        </div>
        <button class="grid size-10 place-items-center rounded-2xl bg-slate-100 dark:bg-slate-900" @click="setTheme(theme === 'light' ? 'dark' : 'light')">
          <Icon :icon="theme === 'light' ? 'solar:moon-bold-duotone' : 'solar:sun-2-bold-duotone'" class="size-5" />
        </button>
      </header>

      <section v-if="!connected" class="flex flex-1 flex-col gap-4 overflow-auto p-5">
        <div class="rounded-3xl bg-sky-500 p-5 text-white shadow-xl shadow-sky-500/25">
          <Icon icon="solar:smartphone-update-bold-duotone" class="size-11" />
          <h2 class="mt-4 text-2xl font-semibold">Pair and cache before display</h2>
          <p class="mt-2 text-sm text-sky-50">A mobile device connects to one desktop at a time and loads the verified scheme cache before showing controls.</p>
        </div>

        <div class="rounded-3xl bg-white p-4 shadow-lg shadow-sky-950/8 dark:bg-slate-950">
          <div class="grid gap-3">
            <label class="grid gap-1 text-sm font-medium">
              Desktop IP and port
              <input value="192.168.1.24:48320" class="rounded-2xl bg-slate-100 px-4 py-3 outline-none ring-sky-500 focus:ring-2 dark:bg-slate-900" />
            </label>
            <label class="grid gap-1 text-sm font-medium">
              6-digit code
              <input value="482913" inputmode="numeric" class="rounded-2xl bg-slate-100 px-4 py-3 tracking-[0.35em] outline-none ring-sky-500 focus:ring-2 dark:bg-slate-900" />
            </label>
          </div>
          <div class="mt-4 grid grid-cols-[1fr_auto] gap-3">
            <button class="rounded-2xl bg-sky-500 py-3 font-semibold text-white" @click="connect">Connect</button>
            <button class="grid size-12 place-items-center rounded-2xl bg-slate-100 dark:bg-slate-900">
              <Icon icon="solar:qr-code-bold-duotone" class="size-6" />
            </button>
          </div>
          <div v-if="updating" class="mt-4">
            <div class="h-2 overflow-hidden rounded-full bg-slate-100 dark:bg-slate-900">
              <div class="h-full rounded-full bg-sky-500 transition-all" :style="{ width: `${updateProgress}%` }"></div>
            </div>
            <p class="mt-2 text-xs text-slate-500">Updating scheme cache {{ updateProgress }}%</p>
          </div>
        </div>

        <div class="rounded-3xl bg-white p-4 shadow-lg shadow-sky-950/8 dark:bg-slate-950">
          <h3 class="font-semibold">Known desktops</h3>
          <div class="mt-3 grid gap-2">
            <button v-for="desktop in desktops" :key="desktop.address" class="rounded-2xl bg-slate-100 p-3 text-left dark:bg-slate-900" @click="connect">
              <div class="flex items-center justify-between">
                <span class="font-medium">{{ desktop.name }}</span>
                <span class="rounded-full bg-emerald-100 px-2 py-1 text-xs font-semibold text-emerald-600 dark:bg-emerald-950 dark:text-emerald-300">{{ desktop.status }}</span>
              </div>
              <p class="mt-1 text-xs text-slate-500">{{ desktop.address }} · {{ desktop.scheme }}</p>
            </button>
          </div>
        </div>
      </section>

      <section v-else class="flex flex-1 flex-col overflow-hidden bg-white p-5 dark:bg-slate-950">
        <div class="mb-4 flex items-center justify-between">
          <div>
            <p class="text-sm text-slate-500">Studio Flow</p>
            <h2 class="text-2xl font-semibold">{{ currentPage.name }}</h2>
          </div>
          <button class="rounded-2xl bg-slate-100 px-4 py-3 text-sm font-semibold dark:bg-slate-900" @click="nextPage">Next</button>
        </div>

        <div class="grid flex-1 grid-cols-2 grid-rows-2 gap-3 overflow-hidden">
          <button v-for="tile in currentPage.tiles" :key="tile.label" class="flex flex-col items-center justify-center overflow-hidden rounded-[28px] bg-slate-100 p-4 active:scale-[0.98] dark:bg-slate-900">
            <span :class="['grid size-14 place-items-center rounded-2xl text-white shadow-lg', tile.accent]">
              <Icon :icon="tile.icon" class="size-8" />
            </span>
            <span class="mt-4 text-lg font-semibold">{{ tile.label }}</span>
          </button>
        </div>

        <div class="mt-4 flex items-center justify-center gap-2">
          <span v-for="(_, index) in pages" :key="index" class="h-2 rounded-full transition-all" :class="index === activePage ? 'w-6 bg-sky-500' : 'w-2 bg-slate-300 dark:bg-slate-700'"></span>
        </div>
      </section>
    </section>
  </main>
</template>
