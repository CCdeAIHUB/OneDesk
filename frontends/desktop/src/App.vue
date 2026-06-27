<script setup lang="ts">
import { Icon } from "@iconify/vue";
import { computed, reactive, ref } from "vue";

type ViewKey = "home" | "component" | "page" | "scheme" | "plugin" | "permission" | "log";
type ThemeMode = "light" | "dark";

const activeView = ref<ViewKey>("home");
const theme = ref<ThemeMode>("light");
const showPermissionDialog = ref(false);
const showCodeSwitchDialog = ref(false);
const exporting = ref(false);
const exportProgress = ref(0);

const state = reactive({
  activeScheme: "直播控制台",
  selectedDevice: "OneDesk Stream Deck",
  toast: "方案缓存已校验",
});

const navItems: Array<{ key: ViewKey; label: string; icon: string }> = [
  { key: "home", label: "首页", icon: "solar:widget-2-bold-duotone" },
  { key: "component", label: "组件", icon: "solar:card-bold-duotone" },
  { key: "page", label: "页面", icon: "solar:layers-bold-duotone" },
  { key: "scheme", label: "方案", icon: "solar:play-square-bold-duotone" },
  { key: "plugin", label: "插件", icon: "solar:puzzle-bold-duotone" },
  { key: "permission", label: "设置", icon: "solar:settings-bold-duotone" },
  { key: "log", label: "账户", icon: "solar:user-rounded-bold-duotone" },
];

const quickActions = [
  { label: "创建新方案", icon: "solar:add-circle-bold-duotone", color: "text-sky-500" },
  { label: "导入方案", icon: "solar:download-minimalistic-bold-duotone", color: "text-green-500" },
  { label: "打开动作编辑器", icon: "solar:bolt-bold-duotone", color: "text-violet-500" },
];

const quickStart = [
  { label: "连接新设备", desc: "连接并设置新的控制设备", icon: "solar:usb-bold-duotone", color: "text-sky-500" },
  { label: "浏览插件", desc: "扩展你的 OneDesk 能力", icon: "solar:puzzle-bold-duotone", color: "text-green-500" },
  { label: "使用帮助", desc: "查看使用文档和教程", icon: "solar:question-circle-bold-duotone", color: "text-violet-500" },
];

const components = [
  { name: "场景切换", mode: "可视化", actions: 3, status: "已授权" },
  { name: "音量推子", mode: "代码", actions: 5, status: "缺少插件" },
  { name: "素材标记", mode: "可视化", actions: 4, status: "待确认" },
  { name: "专注计时", mode: "可视化", actions: 2, status: "已授权" },
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

const logs = ["小米平板 6 已连接", "断联日志已上传，共 12 条", "OBS Control 插件权限已更新", "页面切换动画已保存"];

const viewTitle = computed(() => navItems.find((item) => item.key === activeView.value)?.label ?? "首页");

function setTheme(next: ThemeMode) {
  theme.value = next;
  document.documentElement.classList.toggle("dark", next === "dark");
}

function startExport() {
  exporting.value = true;
  exportProgress.value = 12;
  const timer = window.setInterval(() => {
    exportProgress.value += 16;
    if (exportProgress.value >= 100) {
      exportProgress.value = 100;
      window.clearInterval(timer);
      window.setTimeout(() => {
        exporting.value = false;
        state.toast = "方案导出完成";
      }, 420);
    }
  }, 180);
}
</script>

<template>
  <main class="desktop-stage h-screen w-screen overflow-hidden p-5 text-slate-950 dark:text-slate-100">
    <section class="mx-auto flex h-full max-w-[1180px] overflow-hidden rounded-[24px] border border-white/55 bg-white/72 shadow-[0_28px_80px_rgba(15,23,42,0.18)] backdrop-blur-2xl dark:border-white/10 dark:bg-slate-950/74 dark:shadow-[0_28px_80px_rgba(0,0,0,0.35)]">
      <aside class="flex w-[96px] shrink-0 items-start justify-center bg-white/54 py-9 dark:bg-slate-950/24">
        <nav class="flex w-[54px] flex-col items-center gap-4 rounded-[28px] bg-white/92 px-2 py-4 shadow-[0_16px_40px_rgba(15,23,42,0.08)] dark:bg-slate-900/78 dark:shadow-[0_16px_40px_rgba(0,0,0,0.25)]">
          <button
            v-for="item in navItems"
            :key="item.key"
            class="grid size-10 place-items-center rounded-full transition"
            :class="activeView === item.key ? 'bg-sky-500 text-white shadow-[0_10px_24px_rgba(14,165,233,0.35)]' : 'text-slate-500 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-slate-800'"
            :title="item.label"
            @click="activeView = item.key"
          >
            <Icon :icon="item.icon" class="size-[21px]" />
          </button>
        </nav>
      </aside>

      <section class="flex min-w-0 flex-1 flex-col px-8 py-6">
        <header class="flex h-11 shrink-0 items-center justify-between">
          <div class="min-w-0">
            <h1 class="truncate text-[20px] font-semibold leading-6">你好，OneDesk！</h1>
            <p class="mt-1 text-[12px] text-slate-500 dark:text-slate-400">欢迎回来，今天也要高效控制每一个瞬间</p>
          </div>

          <div class="flex items-center gap-3">
            <label class="flex h-9 w-[300px] items-center gap-2 rounded-full bg-white/82 px-4 text-[12px] text-slate-400 shadow-sm dark:bg-slate-900/72 dark:text-slate-500">
              <Icon icon="solar:magnifer-bold-duotone" class="size-4" />
              <input class="min-w-0 flex-1 bg-transparent outline-none placeholder:text-slate-400" placeholder="搜索（设备、方案、动作等）" />
            </label>
            <button class="grid size-8 place-items-center rounded-full text-slate-500 hover:bg-white/80 dark:text-slate-300 dark:hover:bg-slate-900" title="浅色模式" @click="setTheme('light')">
              <Icon icon="solar:sun-2-bold-duotone" class="size-4" />
            </button>
            <button class="grid size-8 place-items-center rounded-full text-slate-500 hover:bg-white/80 dark:text-slate-300 dark:hover:bg-slate-900" title="深色模式" @click="setTheme('dark')">
              <Icon icon="solar:moon-bold-duotone" class="size-4" />
            </button>
            <div class="ml-2 flex items-center gap-1 text-slate-500 dark:text-slate-300">
              <button class="grid size-8 place-items-center rounded-full hover:bg-white/80 dark:hover:bg-slate-900" title="最小化">
                <Icon icon="fluent:minimize-16-regular" class="size-4" />
              </button>
              <button class="grid size-8 place-items-center rounded-full hover:bg-white/80 dark:hover:bg-slate-900" title="最大化">
                <Icon icon="fluent:maximize-16-regular" class="size-4" />
              </button>
              <button class="grid size-8 place-items-center rounded-full hover:bg-rose-50 hover:text-rose-500 dark:hover:bg-rose-950/60" title="关闭">
                <Icon icon="fluent:dismiss-16-regular" class="size-4" />
              </button>
            </div>
          </div>
        </header>

        <div class="min-h-0 flex-1 pt-8">
          <section v-if="activeView === 'home'" class="grid h-full grid-cols-[1.25fr_0.9fr] grid-rows-[240px_1fr] gap-5">
            <div class="soft-card p-5">
              <div class="mb-4 flex items-start justify-between">
                <div>
                  <h2 class="text-[16px] font-semibold">设备状态</h2>
                  <p class="mt-2 text-[12px] text-slate-500 dark:text-slate-400">已连接 1 个设备</p>
                </div>
                <span class="flex items-center gap-1.5 text-[12px] text-slate-500 dark:text-slate-400">
                  <i class="size-2 rounded-full bg-green-500"></i>
                  在线
                </span>
              </div>

              <div class="flex items-center gap-5">
                <div class="grid size-[68px] shrink-0 grid-cols-3 gap-1 rounded-xl bg-slate-900 p-2 shadow-lg shadow-slate-950/12 dark:bg-slate-800">
                  <span v-for="index in 9" :key="index" class="rounded-[3px] bg-slate-600"></span>
                </div>
                <div class="min-w-0 flex-1">
                  <p class="truncate text-[14px] font-semibold">{{ state.selectedDevice }}</p>
                  <p class="mt-2 text-[12px] text-slate-500 dark:text-slate-400">15 按键</p>
                  <p class="mt-1 flex items-center gap-1 text-[12px] text-green-600">
                    <Icon icon="solar:battery-charge-bold-duotone" class="size-4" />
                    100%
                  </p>
                </div>
              </div>

              <div class="mt-5 flex justify-end">
                <button class="rounded-full border border-sky-500/60 px-4 py-2 text-[12px] font-medium text-sky-600 hover:bg-sky-50 dark:hover:bg-sky-950/40">
                  设备管理
                </button>
              </div>
            </div>

            <div class="soft-card p-5">
              <h2 class="text-[16px] font-semibold">快捷操作</h2>
              <div class="mt-4 grid gap-3">
                <button v-for="item in quickActions" :key="item.label" class="soft-row group">
                  <span class="grid size-8 place-items-center rounded-xl bg-white shadow-sm dark:bg-slate-800">
                    <Icon :icon="item.icon" :class="['size-5', item.color]" />
                  </span>
                  <span class="min-w-0 flex-1 truncate text-left text-[13px] font-medium">{{ item.label }}</span>
                  <Icon icon="solar:alt-arrow-right-linear" class="size-4 text-slate-400 transition group-hover:translate-x-0.5" />
                </button>
              </div>
            </div>

            <div class="soft-card col-span-2 p-5">
              <h2 class="text-[16px] font-semibold">快速开始</h2>
              <div class="mt-4 grid grid-cols-3 gap-4">
                <button v-for="item in quickStart" :key="item.label" class="soft-start">
                  <span class="grid size-10 place-items-center rounded-2xl bg-white shadow-sm dark:bg-slate-800">
                    <Icon :icon="item.icon" :class="['size-6', item.color]" />
                  </span>
                  <span class="min-w-0">
                    <span class="block truncate text-[13px] font-semibold">{{ item.label }}</span>
                    <span class="mt-1 block truncate text-[12px] text-slate-500 dark:text-slate-400">{{ item.desc }}</span>
                  </span>
                </button>
              </div>
            </div>
          </section>

          <section v-else-if="activeView === 'component'" class="grid h-full grid-cols-[300px_1fr] gap-5">
            <div class="soft-card overflow-hidden p-5">
              <div class="mb-4 flex items-center justify-between">
                <h2 class="text-[16px] font-semibold">组件管理</h2>
                <button class="rounded-full bg-sky-500 px-3 py-1.5 text-[12px] font-medium text-white">新建</button>
              </div>
              <div class="grid gap-2">
                <button v-for="item in components" :key="item.name" class="rounded-2xl bg-white/72 px-3 py-2.5 text-left shadow-sm dark:bg-slate-900/70">
                  <div class="flex items-center justify-between gap-3">
                    <span class="truncate text-[13px] font-semibold">{{ item.name }}</span>
                    <span class="text-[11px]" :class="item.status === '缺少插件' ? 'text-rose-500' : item.status === '待确认' ? 'text-amber-500' : 'text-green-600'">{{ item.status }}</span>
                  </div>
                  <p class="mt-1 text-[12px] text-slate-500">{{ item.mode }} · {{ item.actions }} 个动作</p>
                </button>
              </div>
            </div>

            <div class="soft-card p-5">
              <div class="mb-4 flex items-center justify-between">
                <h2 class="text-[16px] font-semibold">组件编辑</h2>
                <div class="flex gap-2">
                  <button class="rounded-full bg-white px-3 py-1.5 text-[12px] shadow-sm dark:bg-slate-800" @click="showPermissionDialog = true">导入</button>
                  <button class="rounded-full bg-sky-500 px-3 py-1.5 text-[12px] font-medium text-white" @click="startExport">导出</button>
                </div>
              </div>
              <div class="grid grid-cols-[1fr_220px] gap-4">
                <div class="grid gap-3">
                  <div class="rounded-2xl bg-white/72 p-4 shadow-sm dark:bg-slate-900/70">
                    <h3 class="text-[13px] font-semibold">可视化样式</h3>
                    <div class="mt-3 grid grid-cols-3 gap-2 text-[12px]">
                      <select class="field"><option>渐变背景</option><option>纯色背景</option><option>图片背景</option></select>
                      <select class="field"><option>按下缩小</option><option>按下高亮</option></select>
                      <input class="field" value="圆角 16" />
                      <input class="field" value="启动场景" />
                      <input class="field" value="字号 14" />
                      <select class="field"><option>居中</option><option>靠左</option><option>靠下</option></select>
                    </div>
                  </div>
                  <div class="rounded-2xl bg-white/72 p-4 shadow-sm dark:bg-slate-900/70">
                    <div class="flex items-center justify-between">
                      <h3 class="text-[13px] font-semibold">动作</h3>
                      <button class="text-[12px] text-sky-600">添加动作</button>
                    </div>
                    <div class="mt-3 grid gap-2 text-[12px]">
                      <div class="flex justify-between rounded-xl bg-slate-50 px-3 py-2 dark:bg-slate-800"><span>三指上滑</span><span class="text-slate-500">切换场景</span></div>
                      <div class="flex justify-between rounded-xl bg-slate-50 px-3 py-2 dark:bg-slate-800"><span>长按</span><span class="text-slate-500">发送通知</span></div>
                    </div>
                  </div>
                </div>
                <div>
                  <h3 class="mb-3 text-[13px] font-semibold">预览</h3>
                  <div class="grid aspect-square place-items-center overflow-hidden rounded-[22px] bg-gradient-to-br from-sky-400 to-cyan-300 text-white shadow-lg shadow-sky-500/18">
                    <div class="text-center">
                      <Icon icon="solar:bolt-circle-bold-duotone" class="mx-auto size-10" />
                      <p class="mt-2 text-[13px] font-semibold">启动场景</p>
                    </div>
                  </div>
                  <button class="mt-4 w-full rounded-full bg-white py-2 text-[12px] shadow-sm dark:bg-slate-800" @click="showCodeSwitchDialog = true">切换到代码编辑</button>
                </div>
              </div>
            </div>
          </section>

          <section v-else class="soft-card h-full overflow-auto p-5">
            <div class="mb-4 flex items-center justify-between">
              <h2 class="text-[16px] font-semibold">{{ viewTitle }}</h2>
              <button class="rounded-full bg-sky-500 px-3 py-1.5 text-[12px] font-medium text-white" @click="startExport">执行操作</button>
            </div>

            <div v-if="activeView === 'scheme'" class="grid grid-cols-4 gap-3">
              <div v-for="page in pages" :key="page.name" class="rounded-2xl bg-white/72 p-4 text-center shadow-sm dark:bg-slate-900/70">
                <Icon icon="solar:smartphone-bold-duotone" class="mx-auto size-8 text-sky-500" />
                <p class="mt-2 text-[13px] font-semibold">{{ page.name }}</p>
                <p class="mt-1 text-[12px] text-slate-500">{{ page.grid }} · {{ page.components }} 组件</p>
              </div>
            </div>

            <div v-else-if="activeView === 'permission'" class="grid gap-2">
              <div v-for="permission in permissions" :key="permission.id" class="flex items-center justify-between rounded-2xl bg-white/72 px-4 py-3 shadow-sm dark:bg-slate-900/70">
                <div>
                  <p class="text-[13px] font-semibold">{{ permission.label }}</p>
                  <p class="mt-1 text-[12px] text-slate-500">{{ permission.category }} · {{ permission.id }}</p>
                </div>
                <span class="rounded-full px-2.5 py-1 text-[11px] font-medium" :class="permission.risk === '高危' ? 'bg-rose-100 text-rose-600 dark:bg-rose-950 dark:text-rose-300' : 'bg-sky-100 text-sky-600 dark:bg-sky-950 dark:text-sky-300'">{{ permission.risk }}</span>
              </div>
            </div>

            <div v-else class="grid gap-2">
              <div v-for="item in logs" :key="item" class="flex items-center gap-2 rounded-2xl bg-white/72 px-4 py-3 text-[13px] shadow-sm dark:bg-slate-900/70">
                <span class="size-1.5 rounded-full bg-sky-500"></span>
                <span>{{ item }}</span>
              </div>
            </div>
          </section>
        </div>

        <footer class="h-8 shrink-0">
          <div v-if="exporting" class="mt-3 h-1.5 overflow-hidden rounded-full bg-white/70 dark:bg-slate-900">
            <div class="h-full rounded-full bg-sky-500 transition-all" :style="{ width: `${exportProgress}%` }"></div>
          </div>
          <p v-else class="mt-3 text-[12px] text-slate-500">{{ state.toast }}</p>
        </footer>
      </section>
    </section>

    <div v-if="showPermissionDialog" class="fixed inset-0 grid place-items-center bg-slate-950/28 p-6 backdrop-blur-sm">
      <div class="w-full max-w-[460px] rounded-3xl bg-white p-5 shadow-2xl dark:bg-slate-950">
        <div class="flex items-center justify-between">
          <h3 class="text-[16px] font-semibold">确认授权</h3>
          <button class="grid size-8 place-items-center rounded-full bg-slate-100 dark:bg-slate-900" @click="showPermissionDialog = false">
            <Icon icon="solar:close-circle-bold-duotone" class="size-5" />
          </button>
        </div>
        <div class="mt-4 grid gap-2">
          <label v-for="permission in permissions" :key="permission.id" class="flex items-center gap-3 rounded-2xl bg-slate-50 px-3 py-2.5 text-[13px] dark:bg-slate-900">
            <input type="checkbox" checked class="size-4 accent-sky-500" />
            <span class="min-w-0 flex-1">
              <span class="block font-medium">{{ permission.label }}</span>
              <span class="mt-0.5 block text-[12px] text-slate-500">{{ permission.category }}</span>
            </span>
            <span v-if="permission.risk === '高危'" class="rounded-full bg-rose-100 px-2 py-1 text-[11px] font-medium text-rose-600 dark:bg-rose-950 dark:text-rose-300">高危</span>
          </label>
        </div>
        <button class="mt-4 w-full rounded-2xl bg-sky-500 py-2.5 text-[13px] font-medium text-white" @click="showPermissionDialog = false">授权并导入</button>
      </div>
    </div>

    <div v-if="showCodeSwitchDialog" class="fixed inset-0 grid place-items-center bg-slate-950/28 p-6 backdrop-blur-sm">
      <div class="w-full max-w-[420px] rounded-3xl bg-white p-5 shadow-2xl dark:bg-slate-950">
        <Icon icon="solar:danger-triangle-bold-duotone" class="size-9 text-amber-500" />
        <h3 class="mt-3 text-[16px] font-semibold">切换到代码编辑？</h3>
        <p class="mt-2 text-[13px] leading-6 text-slate-500">切换后无法回到可视化编辑，因为任意 Vue 代码无法完整还原为可视化配置。</p>
        <div class="mt-4 flex gap-2">
          <button class="flex-1 rounded-2xl bg-slate-100 py-2.5 text-[13px] font-medium dark:bg-slate-900" @click="showCodeSwitchDialog = false">取消</button>
          <button class="flex-1 rounded-2xl bg-sky-500 py-2.5 text-[13px] font-medium text-white" @click="showCodeSwitchDialog = false">继续</button>
        </div>
      </div>
    </div>
  </main>
</template>
