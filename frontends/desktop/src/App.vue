<script setup lang="ts">
import { Icon } from "@iconify/vue";
import { computed, reactive, ref } from "vue";

type ViewKey = "dashboard" | "components" | "pages" | "schemes" | "plugins" | "permissions" | "logs";
type ThemeMode = "light" | "dark";

const theme = ref<ThemeMode>("light");
const activeView = ref<ViewKey>("dashboard");
const editorMode = ref<"visual" | "code">("visual");
const previewRatio = ref("1 / 1");
const showPermissionDialog = ref(false);
const showCodeSwitchDialog = ref(false);
const exportProgress = ref(0);
const exporting = ref(false);

const state = reactive({
  connectedDevices: 3,
  activeScheme: "Studio Flow",
  selectedDevice: "Pixel Fold",
  toast: "Scheme cache is up to date",
});

const views: Array<{ key: ViewKey; label: string; icon: string }> = [
  { key: "dashboard", label: "Home", icon: "solar:home-2-bold-duotone" },
  { key: "components", label: "Components", icon: "solar:widget-5-bold-duotone" },
  { key: "pages", label: "Pages", icon: "solar:layers-bold-duotone" },
  { key: "schemes", label: "Schemes", icon: "solar:share-circle-bold-duotone" },
  { key: "plugins", label: "Plugins", icon: "solar:plug-circle-bold-duotone" },
  { key: "permissions", label: "Permissions", icon: "solar:shield-keyhole-bold-duotone" },
  { key: "logs", label: "Logs", icon: "solar:document-text-bold-duotone" },
];

const permissions = [
  { id: "file.writeExternal", category: "Files", label: "Modify files outside private storage", risk: "High" },
  { id: "plugin.invoke", category: "Plugins", label: "Invoke desktop plugin methods", risk: "Normal" },
  { id: "notification.native", category: "Notifications", label: "Send native system notifications", risk: "Normal" },
  { id: "input.keyboardMouseSimulation", category: "Input", label: "Simulate keyboard and mouse input", risk: "High" },
];

const components = [
  { name: "Scene Launcher", mode: "visual", actions: 3, ratio: "1:1", color: "from-sky-400 to-cyan-300" },
  { name: "Mixer Strip", mode: "code", actions: 5, ratio: "2:3", color: "from-fuchsia-400 to-sky-400" },
  { name: "Focus Timer", mode: "visual", actions: 2, ratio: "4:6", color: "from-emerald-300 to-sky-400" },
];

const pages = [
  { name: "Capture", grid: "4 x 3", components: 9 },
  { name: "Live", grid: "5 x 3", components: 12 },
  { name: "Edit", grid: "4 x 4", components: 10 },
];

const schemeEdges = [
  { from: "Capture", to: "Live", trigger: "Three-finger swipe up", animation: "Fade" },
  { from: "Live", to: "Edit", trigger: "Three-finger swipe right", animation: "Slide" },
  { from: "Edit", to: "Capture", trigger: "Five-finger tap", animation: "Scale" },
];

const windowTitle = computed(() => `${state.activeScheme} - OneDesk`);

function setTheme(next: ThemeMode) {
  theme.value = next;
  document.documentElement.classList.toggle("dark", next === "dark");
}

function startExport() {
  exporting.value = true;
  exportProgress.value = 8;
  const timer = window.setInterval(() => {
    exportProgress.value += 14;
    if (exportProgress.value >= 100) {
      exportProgress.value = 100;
      window.clearInterval(timer);
      window.setTimeout(() => {
        exporting.value = false;
        state.toast = "Export package is ready";
      }, 500);
    }
  }, 220);
}
</script>

<template>
  <main class="h-screen w-screen overflow-hidden p-3 text-slate-950 dark:text-slate-100">
    <section class="flex h-full overflow-hidden rounded-[22px] border border-white/40 bg-white/42 shadow-2xl shadow-sky-950/10 backdrop-blur-2xl dark:border-white/10 dark:bg-slate-950/42">
      <aside class="m-3 flex w-64 shrink-0 flex-col rounded-2xl bg-white px-3 py-4 shadow-lg shadow-sky-950/8 dark:bg-slate-950">
        <div class="mb-5 flex items-center gap-3 px-2">
          <div class="grid size-11 place-items-center rounded-2xl bg-sky-500 text-white shadow-lg shadow-sky-500/30">
            <Icon icon="solar:command-bold-duotone" class="size-6" />
          </div>
          <div>
            <h1 class="text-lg font-semibold leading-tight">OneDesk</h1>
            <p class="text-xs text-slate-500 dark:text-slate-400">Control studio</p>
          </div>
        </div>

        <nav class="grid gap-1">
          <button
            v-for="view in views"
            :key="view.key"
            type="button"
            class="flex h-11 items-center gap-3 rounded-xl px-3 text-left text-sm font-medium transition"
            :class="activeView === view.key ? 'bg-sky-500 text-white shadow-lg shadow-sky-500/25' : 'text-slate-600 hover:bg-sky-50 dark:text-slate-300 dark:hover:bg-slate-900'"
            @click="activeView = view.key"
          >
            <Icon :icon="view.icon" class="size-5" />
            <span>{{ view.label }}</span>
          </button>
        </nav>

        <div class="mt-auto rounded-2xl bg-slate-100 p-3 dark:bg-slate-900">
          <p class="text-xs font-medium uppercase tracking-wide text-slate-500 dark:text-slate-400">Connected</p>
          <div class="mt-2 flex items-center justify-between">
            <span class="text-sm font-semibold">{{ state.connectedDevices }} devices</span>
            <span class="rounded-full bg-emerald-500 px-2 py-0.5 text-xs font-semibold text-white">Live</span>
          </div>
        </div>
      </aside>

      <section class="flex min-w-0 flex-1 flex-col">
        <header class="m-3 mb-0 flex h-14 shrink-0 items-center justify-between rounded-2xl bg-white px-4 shadow-lg shadow-sky-950/8 dark:bg-slate-950">
          <div class="min-w-0">
            <p class="truncate text-sm text-slate-500 dark:text-slate-400">{{ windowTitle }}</p>
            <h2 class="truncate text-xl font-semibold">Design, route, and run mobile control surfaces</h2>
          </div>
          <div class="flex items-center gap-2">
            <button class="grid size-10 place-items-center rounded-xl bg-slate-100 text-slate-700 dark:bg-slate-900 dark:text-slate-200" title="Light mode" @click="setTheme('light')">
              <Icon icon="solar:sun-2-bold-duotone" class="size-5" />
            </button>
            <button class="grid size-10 place-items-center rounded-xl bg-slate-100 text-slate-700 dark:bg-slate-900 dark:text-slate-200" title="Dark mode" @click="setTheme('dark')">
              <Icon icon="solar:moon-bold-duotone" class="size-5" />
            </button>
            <button class="grid size-10 place-items-center rounded-xl bg-slate-100 text-slate-700 dark:bg-slate-900 dark:text-slate-200" title="Minimize">
              <Icon icon="mdi:window-minimize" class="size-5" />
            </button>
            <button class="grid size-10 place-items-center rounded-xl bg-slate-100 text-slate-700 dark:bg-slate-900 dark:text-slate-200" title="Maximize">
              <Icon icon="mdi:window-maximize" class="size-5" />
            </button>
            <button class="grid size-10 place-items-center rounded-xl bg-rose-500 text-white" title="Close">
              <Icon icon="mdi:window-close" class="size-5" />
            </button>
          </div>
        </header>

        <div class="grid min-h-0 flex-1 grid-cols-[1fr_360px] gap-3 p-3">
          <section class="min-h-0 overflow-hidden rounded-2xl bg-white shadow-lg shadow-sky-950/8 dark:bg-slate-950">
            <div v-if="activeView === 'dashboard'" class="grid h-full grid-cols-2 gap-4 overflow-auto p-5">
              <div class="col-span-2 rounded-2xl bg-sky-500 p-6 text-white shadow-xl shadow-sky-500/25">
                <p class="text-sm font-medium text-sky-100">Active scheme</p>
                <div class="mt-3 flex items-end justify-between gap-6">
                  <div>
                    <h3 class="text-4xl font-semibold">{{ state.activeScheme }}</h3>
                    <p class="mt-2 max-w-2xl text-sky-50">Cached on {{ state.selectedDevice }} with verified permissions and plugin dependencies.</p>
                  </div>
                  <button class="rounded-2xl bg-white px-5 py-3 text-sm font-semibold text-sky-600" @click="startExport">Export scheme</button>
                </div>
              </div>
              <div v-for="item in components" :key="item.name" class="rounded-2xl border border-slate-200 bg-white p-4 dark:border-slate-800 dark:bg-slate-900">
                <div :class="['mb-4 grid aspect-square place-items-center rounded-2xl bg-gradient-to-br text-white shadow-lg', item.color]">
                  <Icon icon="solar:play-circle-bold-duotone" class="size-12" />
                </div>
                <h4 class="font-semibold">{{ item.name }}</h4>
                <p class="text-sm text-slate-500 dark:text-slate-400">{{ item.actions }} actions · {{ item.mode }} · {{ item.ratio }}</p>
              </div>
            </div>

            <div v-else-if="activeView === 'components'" class="grid h-full grid-cols-[320px_1fr]">
              <div class="border-r border-slate-200 p-4 dark:border-slate-800">
                <div class="mb-4 flex items-center justify-between">
                  <h3 class="text-lg font-semibold">Components</h3>
                  <button class="grid size-9 place-items-center rounded-xl bg-sky-500 text-white">
                    <Icon icon="solar:add-circle-bold-duotone" class="size-5" />
                  </button>
                </div>
                <div class="grid gap-2">
                  <button v-for="item in components" :key="item.name" class="rounded-2xl border border-slate-200 p-3 text-left dark:border-slate-800">
                    <p class="font-medium">{{ item.name }}</p>
                    <p class="text-xs text-slate-500">{{ item.actions }} actions · {{ item.mode }}</p>
                  </button>
                </div>
              </div>
              <div class="grid min-w-0 grid-cols-[1fr_310px]">
                <div class="min-w-0 p-4">
                  <div class="mb-4 flex items-center justify-between">
                    <div class="flex rounded-xl bg-slate-100 p-1 dark:bg-slate-900">
                      <button class="rounded-lg px-3 py-2 text-sm font-medium" :class="editorMode === 'visual' ? 'bg-white shadow dark:bg-slate-800' : ''" @click="editorMode = 'visual'">Visual</button>
                      <button class="rounded-lg px-3 py-2 text-sm font-medium" :class="editorMode === 'code' ? 'bg-white shadow dark:bg-slate-800' : ''" @click="showCodeSwitchDialog = true">Code</button>
                    </div>
                    <button class="rounded-xl bg-sky-500 px-4 py-2 text-sm font-semibold text-white" @click="showPermissionDialog = true">Import</button>
                  </div>
                  <div v-if="editorMode === 'visual'" class="grid gap-4">
                    <div class="rounded-2xl bg-slate-100 p-4 dark:bg-slate-900">
                      <h4 class="font-semibold">Style</h4>
                      <div class="mt-4 grid grid-cols-2 gap-3">
                        <label class="grid gap-1 text-sm">Background<select class="rounded-xl border-0 bg-white px-3 py-2 dark:bg-slate-800"><option>Gradient</option><option>Solid</option><option>Image</option><option>Video</option></select></label>
                        <label class="grid gap-1 text-sm">Pressed style<select class="rounded-xl border-0 bg-white px-3 py-2 dark:bg-slate-800"><option>Scale down</option><option>Glow</option></select></label>
                        <label class="grid gap-1 text-sm">Text size<input class="rounded-xl border-0 bg-white px-3 py-2 dark:bg-slate-800" value="18" /></label>
                        <label class="grid gap-1 text-sm">Corner radius<input class="rounded-xl border-0 bg-white px-3 py-2 dark:bg-slate-800" value="22" /></label>
                      </div>
                    </div>
                    <div class="rounded-2xl bg-slate-100 p-4 dark:bg-slate-900">
                      <div class="flex items-center justify-between">
                        <h4 class="font-semibold">Actions</h4>
                        <button class="rounded-xl bg-white px-3 py-2 text-sm font-semibold dark:bg-slate-800">Add action</button>
                      </div>
                      <p class="mt-3 text-sm text-slate-500">Triggers are unique per component. Execution calls JSAPI through trusted source injection.</p>
                    </div>
                  </div>
                  <div v-else class="grid h-[520px] grid-cols-[190px_1fr] overflow-hidden rounded-2xl border border-slate-200 dark:border-slate-800">
                    <div class="bg-slate-100 p-3 text-sm dark:bg-slate-900">
                      <p class="font-semibold">Files</p>
                      <div class="mt-3 grid gap-2 text-slate-500">
                        <span>src/SceneLauncher.vue</span>
                        <span>src/actions.ts</span>
                        <span>onedesk.component.json</span>
                      </div>
                    </div>
                    <pre class="overflow-auto bg-slate-950 p-4 text-sm text-sky-100"><code>&lt;script setup lang="ts"&gt;
const props = defineProps&lt;{ active: boolean }&gt;()
&lt;/script&gt;

&lt;template&gt;
  &lt;button class="control-tile"&gt;Launch&lt;/button&gt;
&lt;/template&gt;</code></pre>
                  </div>
                </div>
                <aside class="border-l border-slate-200 p-4 dark:border-slate-800">
                  <div class="mb-3 flex items-center justify-between">
                    <h4 class="font-semibold">Preview</h4>
                    <select v-model="previewRatio" class="rounded-xl bg-slate-100 px-3 py-2 text-sm dark:bg-slate-900">
                      <option value="1 / 1">1:1</option>
                      <option value="2 / 3">2:3</option>
                      <option value="4 / 6">4:6</option>
                    </select>
                  </div>
                  <div class="grid place-items-center rounded-2xl bg-slate-100 p-5 dark:bg-slate-900">
                    <div class="grid w-full max-w-56 place-items-center overflow-hidden rounded-3xl bg-gradient-to-br from-sky-400 to-cyan-300 text-white shadow-xl" :style="{ aspectRatio: previewRatio }">
                      <div class="text-center">
                        <Icon icon="solar:bolt-circle-bold-duotone" class="mx-auto size-12" />
                        <p class="mt-3 font-semibold">Launch</p>
                      </div>
                    </div>
                  </div>
                </aside>
              </div>
            </div>

            <div v-else-if="activeView === 'schemes'" class="grid h-full grid-cols-[280px_1fr]">
              <aside class="border-r border-slate-200 p-4 dark:border-slate-800">
                <h3 class="text-lg font-semibold">Scheme pages</h3>
                <div class="mt-4 grid gap-2">
                  <div v-for="page in pages" :key="page.name" class="rounded-2xl bg-slate-100 p-3 dark:bg-slate-900">
                    <p class="font-medium">{{ page.name }}</p>
                    <p class="text-xs text-slate-500">{{ page.grid }} · {{ page.components }} components</p>
                  </div>
                </div>
              </aside>
              <div class="relative overflow-hidden p-5">
                <h3 class="mb-5 text-lg font-semibold">Flow</h3>
                <div class="grid grid-cols-3 gap-5">
                  <div v-for="page in pages" :key="page.name" class="rounded-3xl border border-slate-200 bg-white p-5 text-center shadow-lg dark:border-slate-800 dark:bg-slate-900">
                    <Icon icon="solar:smartphone-bold-duotone" class="mx-auto size-10 text-sky-500" />
                    <p class="mt-3 font-semibold">{{ page.name }}</p>
                  </div>
                </div>
                <div class="mt-6 grid gap-3">
                  <div v-for="edge in schemeEdges" :key="edge.from" class="flex items-center justify-between rounded-2xl bg-slate-100 p-4 dark:bg-slate-900">
                    <span class="font-medium">{{ edge.from }} -> {{ edge.to }}</span>
                    <span class="text-sm text-slate-500">{{ edge.trigger }} · {{ edge.animation }}</span>
                  </div>
                </div>
              </div>
            </div>

            <div v-else class="h-full overflow-auto p-5">
              <h3 class="text-lg font-semibold">{{ views.find((item) => item.key === activeView)?.label }}</h3>
              <div class="mt-4 grid gap-3">
                <div v-for="permission in permissions" :key="permission.id" class="flex items-center justify-between rounded-2xl border border-slate-200 p-4 dark:border-slate-800">
                  <div>
                    <p class="font-medium">{{ permission.label }}</p>
                    <p class="text-sm text-slate-500">{{ permission.category }} · {{ permission.id }}</p>
                  </div>
                  <span class="rounded-full px-3 py-1 text-xs font-semibold" :class="permission.risk === 'High' ? 'bg-rose-100 text-rose-600 dark:bg-rose-950 dark:text-rose-300' : 'bg-sky-100 text-sky-600 dark:bg-sky-950 dark:text-sky-300'">{{ permission.risk }}</span>
                </div>
              </div>
            </div>
          </section>

          <aside class="grid min-h-0 grid-rows-[auto_1fr_auto] gap-3">
            <section class="rounded-2xl bg-white p-4 shadow-lg shadow-sky-950/8 dark:bg-slate-950">
              <div class="flex items-center justify-between">
                <h3 class="font-semibold">Pairing</h3>
                <Icon icon="solar:qr-code-bold-duotone" class="size-6 text-sky-500" />
              </div>
              <div class="mt-4 rounded-2xl bg-slate-100 p-4 dark:bg-slate-900">
                <div class="grid aspect-square place-items-center rounded-xl bg-white dark:bg-slate-950">
                  <Icon icon="solar:qr-code-bold-duotone" class="size-28 text-slate-900 dark:text-white" />
                </div>
                <p class="mt-3 text-center text-sm text-slate-500">192.168.1.24:48320 · 482913</p>
              </div>
            </section>

            <section class="min-h-0 overflow-auto rounded-2xl bg-white p-4 shadow-lg shadow-sky-950/8 dark:bg-slate-950">
              <h3 class="font-semibold">Activity</h3>
              <div class="mt-4 grid gap-3">
                <div v-for="label in ['Pixel Fold connected', 'Logs uploaded', 'Plugin permission changed', 'Scheme cache verified']" :key="label" class="flex items-center gap-3 rounded-2xl bg-slate-100 p-3 dark:bg-slate-900">
                  <span class="size-2 rounded-full bg-sky-500"></span>
                  <span class="text-sm">{{ label }}</span>
                </div>
              </div>
            </section>

            <section class="rounded-2xl bg-white p-4 shadow-lg shadow-sky-950/8 dark:bg-slate-950">
              <div class="flex items-center gap-3">
                <Icon icon="solar:bell-bing-bold-duotone" class="size-6 text-sky-500" />
                <p class="text-sm font-medium">{{ state.toast }}</p>
              </div>
              <div v-if="exporting" class="mt-4">
                <div class="h-2 overflow-hidden rounded-full bg-slate-100 dark:bg-slate-900">
                  <div class="h-full rounded-full bg-sky-500 transition-all" :style="{ width: `${exportProgress}%` }"></div>
                </div>
                <p class="mt-2 text-xs text-slate-500">Exporting package {{ exportProgress }}%</p>
              </div>
            </section>
          </aside>
        </div>
      </section>
    </section>

    <div v-if="showPermissionDialog" class="fixed inset-0 grid place-items-center bg-slate-950/35 p-6 backdrop-blur-sm">
      <div class="w-full max-w-xl rounded-3xl bg-white p-5 shadow-2xl dark:bg-slate-950">
        <div class="flex items-center justify-between">
          <h3 class="text-lg font-semibold">Review permissions</h3>
          <button class="grid size-9 place-items-center rounded-xl bg-slate-100 dark:bg-slate-900" @click="showPermissionDialog = false">
            <Icon icon="solar:close-circle-bold-duotone" class="size-5" />
          </button>
        </div>
        <div class="mt-4 grid gap-3">
          <label v-for="permission in permissions" :key="permission.id" class="flex items-center gap-3 rounded-2xl border border-slate-200 p-3 dark:border-slate-800">
            <input type="checkbox" checked class="size-5 accent-sky-500" />
            <span class="min-w-0 flex-1">
              <span class="block font-medium">{{ permission.label }}</span>
              <span class="block text-sm text-slate-500">{{ permission.category }}</span>
            </span>
            <span v-if="permission.risk === 'High'" class="rounded-full bg-rose-100 px-2 py-1 text-xs font-semibold text-rose-600 dark:bg-rose-950 dark:text-rose-300">High risk</span>
          </label>
        </div>
        <button class="mt-5 w-full rounded-2xl bg-sky-500 py-3 font-semibold text-white" @click="showPermissionDialog = false">Authorize and import</button>
      </div>
    </div>

    <div v-if="showCodeSwitchDialog" class="fixed inset-0 grid place-items-center bg-slate-950/35 p-6 backdrop-blur-sm">
      <div class="w-full max-w-md rounded-3xl bg-white p-5 shadow-2xl dark:bg-slate-950">
        <Icon icon="solar:danger-triangle-bold-duotone" class="size-10 text-amber-500" />
        <h3 class="mt-3 text-lg font-semibold">Switch to code editing?</h3>
        <p class="mt-2 text-sm text-slate-500">After switching, this component cannot return to visual editing because arbitrary Vue code cannot be restored into visual controls.</p>
        <div class="mt-5 flex gap-3">
          <button class="flex-1 rounded-2xl bg-slate-100 py-3 font-semibold dark:bg-slate-900" @click="showCodeSwitchDialog = false">Cancel</button>
          <button class="flex-1 rounded-2xl bg-sky-500 py-3 font-semibold text-white" @click="editorMode = 'code'; showCodeSwitchDialog = false">Continue</button>
        </div>
      </div>
    </div>
  </main>
</template>
