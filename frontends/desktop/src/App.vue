<script setup lang="ts">
import { Icon } from "@iconify/vue";
import { computed, reactive, ref } from "vue";

type ViewKey = "home" | "component" | "page" | "scheme" | "plugin" | "permission" | "log";
type ThemeMode = "light" | "dark";

const activeView = ref<ViewKey>("home");
const theme = ref<ThemeMode>("light");
const editorMode = ref<"visual" | "code">("visual");
const previewRatio = ref("1 / 1");
const showPermissionDialog = ref(false);
const showCodeSwitchDialog = ref(false);
const exporting = ref(false);
const exportProgress = ref(0);

const state = reactive({
  activeScheme: "直播控制台",
  selectedDevice: "小米平板 6",
  connectedDevices: 3,
  toast: "方案缓存已校验",
});

const views: Array<{ key: ViewKey; label: string; icon: string }> = [
  { key: "home", label: "总览", icon: "solar:home-2-bold-duotone" },
  { key: "component", label: "组件", icon: "solar:widget-5-bold-duotone" },
  { key: "page", label: "页面", icon: "solar:layers-bold-duotone" },
  { key: "scheme", label: "方案", icon: "solar:share-circle-bold-duotone" },
  { key: "plugin", label: "插件", icon: "solar:plug-circle-bold-duotone" },
  { key: "permission", label: "权限", icon: "solar:shield-keyhole-bold-duotone" },
  { key: "log", label: "日志", icon: "solar:document-text-bold-duotone" },
];

const metrics = [
  { label: "在线设备", value: "3", sub: "1 台桌面端 / 2 台移动端", icon: "solar:devices-bold-duotone" },
  { label: "当前方案", value: "直播控制台", sub: "已应用到小米平板 6", icon: "solar:smartphone-2-bold-duotone" },
  { label: "组件数量", value: "24", sub: "18 个可视化 / 6 个代码", icon: "solar:widget-5-bold-duotone" },
  { label: "待处理权限", value: "2", sub: "含 1 项高危权限", icon: "solar:shield-warning-bold-duotone" },
];

const components = [
  { name: "场景切换", mode: "可视化", actions: 3, ratio: "1:1", status: "已授权" },
  { name: "音量推子", mode: "代码", actions: 5, ratio: "2:3", status: "缺少插件" },
  { name: "专注计时", mode: "可视化", actions: 2, ratio: "4:6", status: "已授权" },
  { name: "素材标记", mode: "可视化", actions: 4, ratio: "1:1", status: "待确认" },
];

const pages = [
  { name: "采集", grid: "4 x 3", components: 9 },
  { name: "直播", grid: "5 x 3", components: 12 },
  { name: "剪辑", grid: "4 x 4", components: 10 },
  { name: "系统", grid: "3 x 3", components: 6 },
];

const permissions = [
  { id: "file.writeExternal", category: "文件管理", label: "修改私有目录外文件", risk: "高危" },
  { id: "plugin.invoke", category: "插件", label: "调用桌面端插件方法", risk: "普通" },
  { id: "notification.native", category: "通知", label: "发送系统通知", risk: "普通" },
  { id: "input.keyboardMouseSimulation", category: "输入控制", label: "模拟键盘和鼠标", risk: "高危" },
];

const logs = [
  "小米平板 6 已连接",
  "断联日志已上传，共 12 条",
  "直播控制台缓存校验完成",
  "OBS Control 插件权限已更新",
  "页面切换动画已保存",
];

const flowEdges = [
  { from: "采集", to: "直播", trigger: "三指上滑", animation: "淡入淡出" },
  { from: "直播", to: "剪辑", trigger: "三指右滑", animation: "平移" },
  { from: "剪辑", to: "系统", trigger: "五指点击", animation: "缩放" },
  { from: "系统", to: "采集", trigger: "双指下滑", animation: "淡入淡出" },
];

const title = computed(() => `${state.activeScheme} - OneDesk`);

function setTheme(next: ThemeMode) {
  theme.value = next;
  document.documentElement.classList.toggle("dark", next === "dark");
}

function startExport() {
  exporting.value = true;
  exportProgress.value = 10;
  const timer = window.setInterval(() => {
    exportProgress.value += 15;
    if (exportProgress.value >= 100) {
      exportProgress.value = 100;
      window.clearInterval(timer);
      window.setTimeout(() => {
        exporting.value = false;
        state.toast = "方案导出完成";
      }, 450);
    }
  }, 180);
}
</script>

<template>
  <main class="h-screen w-screen overflow-hidden text-slate-950 dark:text-slate-100">
    <section class="flex h-full bg-white/58 backdrop-blur-2xl dark:bg-slate-950/58">
      <aside class="flex w-[218px] shrink-0 flex-col border-r border-slate-200 bg-white px-3 py-3 dark:border-slate-800 dark:bg-slate-950">
        <div class="mb-3 flex h-11 items-center gap-3 px-1">
          <div class="grid size-9 place-items-center rounded-xl bg-sky-500 text-white shadow-sm shadow-sky-500/30">
            <Icon icon="solar:command-bold-duotone" class="size-5" />
          </div>
          <div class="min-w-0">
            <h1 class="truncate text-base font-semibold">OneDesk</h1>
            <p class="text-xs text-slate-500">控制中心</p>
          </div>
        </div>

        <nav class="grid gap-1">
          <button
            v-for="view in views"
            :key="view.key"
            type="button"
            class="flex h-9 items-center gap-2 rounded-lg px-3 text-left text-sm transition"
            :class="activeView === view.key ? 'bg-sky-500 text-white shadow-sm shadow-sky-500/20' : 'text-slate-600 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-slate-900'"
            @click="activeView = view.key"
          >
            <Icon :icon="view.icon" class="size-4" />
            <span>{{ view.label }}</span>
          </button>
        </nav>

        <div class="mt-auto rounded-xl bg-slate-50 p-3 dark:bg-slate-900">
          <div class="flex items-center justify-between text-xs text-slate-500">
            <span>连接状态</span>
            <span class="rounded-full bg-emerald-500 px-2 py-0.5 font-medium text-white">在线</span>
          </div>
          <p class="mt-2 text-sm font-semibold">{{ state.connectedDevices }} 台设备</p>
          <p class="mt-1 truncate text-xs text-slate-500">{{ state.selectedDevice }} 已应用当前方案</p>
        </div>
      </aside>

      <section class="flex min-w-0 flex-1 flex-col">
        <header class="flex h-13 shrink-0 items-center justify-between border-b border-slate-200 bg-white px-4 dark:border-slate-800 dark:bg-slate-950">
          <div class="min-w-0">
            <h2 class="truncate text-base font-semibold">{{ title }}</h2>
            <p class="truncate text-xs text-slate-500">桌面端负责设计、权限、日志、插件与 JSAPI 路由</p>
          </div>
          <div class="flex items-center gap-1.5">
            <button class="grid size-8 place-items-center rounded-lg bg-slate-100 text-slate-700 dark:bg-slate-900 dark:text-slate-200" title="浅色模式" @click="setTheme('light')">
              <Icon icon="solar:sun-2-bold-duotone" class="size-4" />
            </button>
            <button class="grid size-8 place-items-center rounded-lg bg-slate-100 text-slate-700 dark:bg-slate-900 dark:text-slate-200" title="深色模式" @click="setTheme('dark')">
              <Icon icon="solar:moon-bold-duotone" class="size-4" />
            </button>
            <button class="grid size-8 place-items-center rounded-lg bg-slate-100 text-slate-700 dark:bg-slate-900 dark:text-slate-200" title="最小化">
              <Icon icon="fluent:minimize-16-regular" class="size-4" />
            </button>
            <button class="grid size-8 place-items-center rounded-lg bg-slate-100 text-slate-700 dark:bg-slate-900 dark:text-slate-200" title="最大化">
              <Icon icon="fluent:maximize-16-regular" class="size-4" />
            </button>
            <button class="grid size-8 place-items-center rounded-lg bg-rose-500 text-white" title="关闭">
              <Icon icon="fluent:dismiss-16-regular" class="size-4" />
            </button>
          </div>
        </header>

        <div class="grid min-h-0 flex-1 grid-cols-[1fr_300px] gap-3 bg-slate-100 p-3 dark:bg-slate-900">
          <section class="min-h-0 overflow-hidden rounded-xl bg-white shadow-sm dark:bg-slate-950">
            <div v-if="activeView === 'home'" class="h-full overflow-auto p-4">
              <div class="grid grid-cols-4 gap-3">
                <div v-for="item in metrics" :key="item.label" class="rounded-xl border border-slate-200 bg-white p-3 dark:border-slate-800 dark:bg-slate-950">
                  <div class="flex items-center justify-between">
                    <span class="text-xs text-slate-500">{{ item.label }}</span>
                    <Icon :icon="item.icon" class="size-5 text-sky-500" />
                  </div>
                  <p class="mt-2 truncate text-xl font-semibold">{{ item.value }}</p>
                  <p class="mt-1 truncate text-xs text-slate-500">{{ item.sub }}</p>
                </div>
              </div>

              <div class="mt-3 grid grid-cols-[1.25fr_1fr] gap-3">
                <section class="rounded-xl border border-slate-200 bg-white p-4 dark:border-slate-800 dark:bg-slate-950">
                  <div class="mb-3 flex items-center justify-between">
                    <h3 class="font-semibold">最近组件</h3>
                    <button class="rounded-lg bg-sky-500 px-3 py-1.5 text-sm font-medium text-white">新建组件</button>
                  </div>
                  <div class="divide-y divide-slate-100 dark:divide-slate-800">
                    <div v-for="item in components" :key="item.name" class="grid grid-cols-[1fr_72px_74px_70px] items-center gap-3 py-2.5 text-sm">
                      <span class="font-medium">{{ item.name }}</span>
                      <span class="text-slate-500">{{ item.mode }}</span>
                      <span class="text-slate-500">{{ item.actions }} 动作</span>
                      <span class="text-right" :class="item.status === '缺少插件' ? 'text-rose-500' : item.status === '待确认' ? 'text-amber-500' : 'text-emerald-600'">{{ item.status }}</span>
                    </div>
                  </div>
                </section>

                <section class="rounded-xl border border-slate-200 bg-white p-4 dark:border-slate-800 dark:bg-slate-950">
                  <div class="mb-3 flex items-center justify-between">
                    <h3 class="font-semibold">方案流转</h3>
                    <button class="rounded-lg bg-slate-100 px-3 py-1.5 text-sm dark:bg-slate-900">编辑</button>
                  </div>
                  <div class="grid gap-2">
                    <div v-for="edge in flowEdges" :key="`${edge.from}-${edge.to}`" class="rounded-lg bg-slate-50 px-3 py-2 text-sm dark:bg-slate-900">
                      <div class="flex items-center justify-between">
                        <span class="font-medium">{{ edge.from }} -> {{ edge.to }}</span>
                        <span class="text-sky-600">{{ edge.animation }}</span>
                      </div>
                      <p class="mt-1 text-xs text-slate-500">{{ edge.trigger }}</p>
                    </div>
                  </div>
                </section>
              </div>
            </div>

            <div v-else-if="activeView === 'component'" class="grid h-full grid-cols-[250px_1fr_280px]">
              <aside class="border-r border-slate-200 p-3 dark:border-slate-800">
                <div class="mb-3 flex items-center justify-between">
                  <h3 class="font-semibold">组件管理</h3>
                  <button class="grid size-8 place-items-center rounded-lg bg-sky-500 text-white"><Icon icon="solar:add-circle-bold-duotone" class="size-4" /></button>
                </div>
                <div class="grid gap-2">
                  <button v-for="item in components" :key="item.name" class="rounded-lg border border-slate-200 px-3 py-2 text-left text-sm dark:border-slate-800">
                    <div class="flex items-center justify-between">
                      <span class="font-medium">{{ item.name }}</span>
                      <span class="text-xs text-slate-500">{{ item.ratio }}</span>
                    </div>
                    <p class="mt-1 text-xs text-slate-500">{{ item.mode }} · {{ item.actions }} 个动作</p>
                  </button>
                </div>
              </aside>

              <section class="min-w-0 p-3">
                <div class="mb-3 flex items-center justify-between">
                  <div class="flex rounded-lg bg-slate-100 p-1 dark:bg-slate-900">
                    <button class="rounded-md px-3 py-1.5 text-sm" :class="editorMode === 'visual' ? 'bg-white shadow-sm dark:bg-slate-800' : ''" @click="editorMode = 'visual'">可视化</button>
                    <button class="rounded-md px-3 py-1.5 text-sm" :class="editorMode === 'code' ? 'bg-white shadow-sm dark:bg-slate-800' : ''" @click="showCodeSwitchDialog = true">代码</button>
                  </div>
                  <div class="flex gap-2">
                    <button class="rounded-lg bg-slate-100 px-3 py-1.5 text-sm dark:bg-slate-900" @click="showPermissionDialog = true">导入</button>
                    <button class="rounded-lg bg-sky-500 px-3 py-1.5 text-sm font-medium text-white" @click="startExport">导出</button>
                  </div>
                </div>

                <div v-if="editorMode === 'visual'" class="grid gap-3">
                  <section class="rounded-xl bg-slate-50 p-3 dark:bg-slate-900">
                    <h4 class="mb-3 text-sm font-semibold">基础样式</h4>
                    <div class="grid grid-cols-3 gap-2">
                      <label class="grid gap-1 text-xs text-slate-500">背景<select class="rounded-lg bg-white px-2 py-2 text-sm text-slate-900 dark:bg-slate-800 dark:text-white"><option>渐变</option><option>纯色</option><option>图片</option><option>视频</option></select></label>
                      <label class="grid gap-1 text-xs text-slate-500">按下效果<select class="rounded-lg bg-white px-2 py-2 text-sm text-slate-900 dark:bg-slate-800 dark:text-white"><option>轻微缩小</option><option>高亮</option></select></label>
                      <label class="grid gap-1 text-xs text-slate-500">圆角<input class="rounded-lg bg-white px-2 py-2 text-sm text-slate-900 dark:bg-slate-800 dark:text-white" value="16" /></label>
                      <label class="grid gap-1 text-xs text-slate-500">文字<input class="rounded-lg bg-white px-2 py-2 text-sm text-slate-900 dark:bg-slate-800 dark:text-white" value="启动场景" /></label>
                      <label class="grid gap-1 text-xs text-slate-500">字号<input class="rounded-lg bg-white px-2 py-2 text-sm text-slate-900 dark:bg-slate-800 dark:text-white" value="15" /></label>
                      <label class="grid gap-1 text-xs text-slate-500">位置<select class="rounded-lg bg-white px-2 py-2 text-sm text-slate-900 dark:bg-slate-800 dark:text-white"><option>居中</option><option>靠左</option><option>靠下</option></select></label>
                    </div>
                  </section>

                  <section class="rounded-xl bg-slate-50 p-3 dark:bg-slate-900">
                    <div class="mb-2 flex items-center justify-between">
                      <h4 class="text-sm font-semibold">动作系统</h4>
                      <button class="rounded-lg bg-white px-3 py-1.5 text-sm dark:bg-slate-800">添加动作</button>
                    </div>
                    <div class="grid gap-2 text-sm">
                      <div class="flex items-center justify-between rounded-lg bg-white px-3 py-2 dark:bg-slate-800">
                        <span>三指上滑</span><span class="text-slate-500">调用 OBS Control / 切换场景</span>
                      </div>
                      <div class="flex items-center justify-between rounded-lg bg-white px-3 py-2 dark:bg-slate-800">
                        <span>长按</span><span class="text-slate-500">发送系统通知</span>
                      </div>
                    </div>
                  </section>
                </div>

                <div v-else class="grid h-[500px] grid-cols-[170px_1fr] overflow-hidden rounded-xl border border-slate-200 dark:border-slate-800">
                  <div class="bg-slate-50 p-3 text-sm dark:bg-slate-900">
                    <p class="font-semibold">文件</p>
                    <div class="mt-3 grid gap-2 text-xs text-slate-500">
                      <span>src/SceneButton.vue</span>
                      <span>src/actions.ts</span>
                      <span>onedesk.component.json</span>
                    </div>
                  </div>
                  <pre class="overflow-auto bg-slate-950 p-4 text-sm text-sky-100"><code>&lt;script setup lang="ts"&gt;
const title = '启动场景'
&lt;/script&gt;

&lt;template&gt;
  &lt;button class="control-tile"&gt;{{ title }}&lt;/button&gt;
&lt;/template&gt;</code></pre>
                </div>
              </section>

              <aside class="border-l border-slate-200 p-3 dark:border-slate-800">
                <div class="mb-3 flex items-center justify-between">
                  <h4 class="font-semibold">预览</h4>
                  <select v-model="previewRatio" class="rounded-lg bg-slate-100 px-2 py-1.5 text-sm dark:bg-slate-900">
                    <option value="1 / 1">1:1</option>
                    <option value="2 / 3">2:3</option>
                    <option value="4 / 6">4:6</option>
                  </select>
                </div>
                <div class="grid place-items-center rounded-xl bg-slate-50 p-4 dark:bg-slate-900">
                  <div class="grid w-full max-w-48 place-items-center overflow-hidden rounded-2xl bg-gradient-to-br from-sky-400 to-cyan-300 text-white shadow-sm" :style="{ aspectRatio: previewRatio }">
                    <div class="text-center">
                      <Icon icon="solar:bolt-circle-bold-duotone" class="mx-auto size-9" />
                      <p class="mt-2 text-sm font-semibold">启动场景</p>
                    </div>
                  </div>
                </div>
              </aside>
            </div>

            <div v-else-if="activeView === 'scheme'" class="grid h-full grid-cols-[250px_1fr]">
              <aside class="border-r border-slate-200 p-3 dark:border-slate-800">
                <h3 class="font-semibold">页面列表</h3>
                <div class="mt-3 grid gap-2">
                  <div v-for="page in pages" :key="page.name" class="rounded-lg bg-slate-50 px-3 py-2 text-sm dark:bg-slate-900">
                    <div class="flex items-center justify-between">
                      <span class="font-medium">{{ page.name }}</span>
                      <span class="text-xs text-slate-500">{{ page.grid }}</span>
                    </div>
                    <p class="mt-1 text-xs text-slate-500">{{ page.components }} 个组件</p>
                  </div>
                </div>
              </aside>
              <section class="p-4">
                <div class="mb-4 flex items-center justify-between">
                  <h3 class="font-semibold">方案流程</h3>
                  <button class="rounded-lg bg-sky-500 px-3 py-1.5 text-sm font-medium text-white" @click="startExport">导出方案</button>
                </div>
                <div class="grid grid-cols-4 gap-3">
                  <div v-for="page in pages" :key="page.name" class="rounded-xl border border-slate-200 bg-white p-4 text-center dark:border-slate-800 dark:bg-slate-950">
                    <Icon icon="solar:smartphone-bold-duotone" class="mx-auto size-8 text-sky-500" />
                    <p class="mt-2 font-semibold">{{ page.name }}</p>
                    <p class="text-xs text-slate-500">{{ page.grid }}</p>
                  </div>
                </div>
                <div class="mt-4 grid gap-2">
                  <div v-for="edge in flowEdges" :key="edge.from" class="grid grid-cols-[1fr_160px_100px] items-center rounded-lg bg-slate-50 px-3 py-2 text-sm dark:bg-slate-900">
                    <span class="font-medium">{{ edge.from }} -> {{ edge.to }}</span>
                    <span class="text-slate-500">{{ edge.trigger }}</span>
                    <span class="text-right text-sky-600">{{ edge.animation }}</span>
                  </div>
                </div>
              </section>
            </div>

            <div v-else class="h-full overflow-auto p-4">
              <h3 class="font-semibold">{{ views.find((item) => item.key === activeView)?.label }}</h3>
              <div class="mt-3 grid gap-2">
                <div v-for="permission in permissions" :key="permission.id" class="flex items-center justify-between rounded-lg border border-slate-200 px-3 py-2 text-sm dark:border-slate-800">
                  <div>
                    <p class="font-medium">{{ permission.label }}</p>
                    <p class="text-xs text-slate-500">{{ permission.category }} · {{ permission.id }}</p>
                  </div>
                  <span class="rounded-full px-2 py-1 text-xs font-medium" :class="permission.risk === '高危' ? 'bg-rose-100 text-rose-600 dark:bg-rose-950 dark:text-rose-300' : 'bg-sky-100 text-sky-600 dark:bg-sky-950 dark:text-sky-300'">{{ permission.risk }}</span>
                </div>
              </div>
            </div>
          </section>

          <aside class="grid min-h-0 grid-rows-[auto_1fr_auto] gap-3">
            <section class="rounded-xl bg-white p-3 shadow-sm dark:bg-slate-950">
              <div class="flex items-center justify-between">
                <h3 class="font-semibold">配对</h3>
                <Icon icon="solar:qr-code-bold-duotone" class="size-5 text-sky-500" />
              </div>
              <div class="mt-3 grid grid-cols-[104px_1fr] gap-3">
                <div class="grid aspect-square place-items-center rounded-lg bg-slate-100 dark:bg-slate-900">
                  <Icon icon="solar:qr-code-bold-duotone" class="size-16 text-slate-900 dark:text-white" />
                </div>
                <div class="min-w-0 text-sm">
                  <p class="font-medium">扫码连接</p>
                  <p class="mt-1 text-xs text-slate-500">192.168.1.24:48320</p>
                  <p class="mt-2 rounded-lg bg-slate-100 px-2 py-1.5 text-center font-mono text-base tracking-widest dark:bg-slate-900">482913</p>
                </div>
              </div>
            </section>

            <section class="min-h-0 overflow-auto rounded-xl bg-white p-3 shadow-sm dark:bg-slate-950">
              <h3 class="font-semibold">动态</h3>
              <div class="mt-3 grid gap-2">
                <div v-for="label in logs" :key="label" class="flex items-center gap-2 rounded-lg bg-slate-50 px-3 py-2 text-sm dark:bg-slate-900">
                  <span class="size-1.5 rounded-full bg-sky-500"></span>
                  <span class="truncate">{{ label }}</span>
                </div>
              </div>
            </section>

            <section class="rounded-xl bg-white p-3 shadow-sm dark:bg-slate-950">
              <div class="flex items-center gap-2">
                <Icon icon="solar:bell-bing-bold-duotone" class="size-5 text-sky-500" />
                <p class="text-sm font-medium">{{ state.toast }}</p>
              </div>
              <div v-if="exporting" class="mt-3">
                <div class="h-1.5 overflow-hidden rounded-full bg-slate-100 dark:bg-slate-900">
                  <div class="h-full rounded-full bg-sky-500 transition-all" :style="{ width: `${exportProgress}%` }"></div>
                </div>
                <p class="mt-1.5 text-xs text-slate-500">正在导出 {{ exportProgress }}%</p>
              </div>
            </section>
          </aside>
        </div>
      </section>
    </section>

    <div v-if="showPermissionDialog" class="fixed inset-0 grid place-items-center bg-slate-950/35 p-6 backdrop-blur-sm">
      <div class="w-full max-w-lg rounded-2xl bg-white p-4 shadow-2xl dark:bg-slate-950">
        <div class="flex items-center justify-between">
          <h3 class="font-semibold">确认授权</h3>
          <button class="grid size-8 place-items-center rounded-lg bg-slate-100 dark:bg-slate-900" @click="showPermissionDialog = false">
            <Icon icon="solar:close-circle-bold-duotone" class="size-5" />
          </button>
        </div>
        <div class="mt-3 grid gap-2">
          <label v-for="permission in permissions" :key="permission.id" class="flex items-center gap-3 rounded-lg border border-slate-200 px-3 py-2 text-sm dark:border-slate-800">
            <input type="checkbox" checked class="size-4 accent-sky-500" />
            <span class="min-w-0 flex-1">
              <span class="block font-medium">{{ permission.label }}</span>
              <span class="block text-xs text-slate-500">{{ permission.category }}</span>
            </span>
            <span v-if="permission.risk === '高危'" class="rounded-full bg-rose-100 px-2 py-1 text-xs font-medium text-rose-600 dark:bg-rose-950 dark:text-rose-300">高危</span>
          </label>
        </div>
        <button class="mt-4 w-full rounded-xl bg-sky-500 py-2.5 font-medium text-white" @click="showPermissionDialog = false">授权并导入</button>
      </div>
    </div>

    <div v-if="showCodeSwitchDialog" class="fixed inset-0 grid place-items-center bg-slate-950/35 p-6 backdrop-blur-sm">
      <div class="w-full max-w-md rounded-2xl bg-white p-4 shadow-2xl dark:bg-slate-950">
        <Icon icon="solar:danger-triangle-bold-duotone" class="size-9 text-amber-500" />
        <h3 class="mt-3 font-semibold">切换到代码编辑？</h3>
        <p class="mt-2 text-sm leading-6 text-slate-500">切换后无法回到可视化编辑，因为任意 Vue 代码无法完整还原为可视化配置。</p>
        <div class="mt-4 flex gap-2">
          <button class="flex-1 rounded-xl bg-slate-100 py-2.5 font-medium dark:bg-slate-900" @click="showCodeSwitchDialog = false">取消</button>
          <button class="flex-1 rounded-xl bg-sky-500 py-2.5 font-medium text-white" @click="editorMode = 'code'; showCodeSwitchDialog = false">继续</button>
        </div>
      </div>
    </div>
  </main>
</template>
