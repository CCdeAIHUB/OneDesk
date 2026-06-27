<script setup lang="ts">
import { Icon } from "@iconify/vue";
import { computed, ref } from "vue";
import type { SectionRoute, ThemeMode, ViewKey } from "./domain";
import { closeWindow, maximizeWindow, minimizeWindow } from "./nativeBridge";
import { components, logs, navItems, pages, permissions, quickActions, quickStart, schemes, workspace } from "./workspace";

const activeView = ref<ViewKey>("home");
const theme = ref<ThemeMode>("system");
const componentRoute = ref<SectionRoute>("manager");
const pageRoute = ref<SectionRoute>("manager");
const schemeRoute = ref<SectionRoute>("manager");
const componentEditorMode = ref<"visual" | "code">("visual");
const showPermissionDialog = ref(false);
const showCodeSwitchDialog = ref(false);
const exporting = ref(false);
const exportProgress = ref(0);

const viewTitle = computed(() => navItems.find((item) => item.key === activeView.value)?.label ?? "首页");

function openView(view: ViewKey) {
  activeView.value = view;
  if (view === "component") componentRoute.value = "manager";
  if (view === "page") pageRoute.value = "manager";
  if (view === "scheme") schemeRoute.value = "manager";
}

function setTheme(next: ThemeMode) {
  theme.value = next;
  const resolved = next === "system" ? (window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light") : next;
  document.documentElement.classList.toggle("dark", resolved === "dark");
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
        workspace.toast = "导出完成";
      }, 420);
    }
  }, 180);
}
</script>

<template>
  <main class="h-screen w-screen overflow-hidden text-slate-950 dark:text-slate-100">
    <section class="flex h-full overflow-hidden bg-white/72 backdrop-blur-2xl dark:bg-slate-950/76">
      <aside class="flex w-[96px] shrink-0 items-start justify-center bg-white/54 py-9 dark:bg-slate-950/24">
        <nav class="flex w-[54px] flex-col items-center gap-4 rounded-[28px] bg-white/92 px-2 py-4 shadow-[0_16px_40px_rgba(15,23,42,0.08)] dark:bg-slate-900/78">
          <button
            v-for="item in navItems"
            :key="item.key"
            class="grid size-10 place-items-center rounded-full transition"
            :class="activeView === item.key ? 'bg-sky-500 text-white shadow-[0_10px_24px_rgba(14,165,233,0.35)]' : 'text-slate-500 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-slate-800'"
            :title="item.label"
            @click="openView(item.key)"
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

            <div class="group flex h-8 w-8 items-center justify-end overflow-hidden rounded-full transition-all duration-200 hover:w-[116px] hover:bg-white/92 hover:px-1.5 hover:shadow-lg hover:shadow-slate-950/10 dark:hover:bg-slate-900/92">
              <button class="grid size-8 shrink-0 place-items-center rounded-full text-slate-500 dark:text-slate-300" title="主题">
                <Icon :icon="theme === 'dark' ? 'solar:moon-bold-duotone' : theme === 'light' ? 'solar:sun-2-bold-duotone' : 'solar:monitor-bold-duotone'" class="size-4" />
              </button>
              <div class="flex w-0 items-center gap-1 overflow-hidden opacity-0 transition-all duration-200 group-hover:w-[78px] group-hover:opacity-100">
                <button class="theme-dot" :class="theme === 'light' ? 'theme-dot-active' : ''" title="浅色" @click="setTheme('light')">
                  <Icon icon="solar:sun-2-bold-duotone" class="size-4" />
                </button>
                <button class="theme-dot" :class="theme === 'dark' ? 'theme-dot-active' : ''" title="深色" @click="setTheme('dark')">
                  <Icon icon="solar:moon-bold-duotone" class="size-4" />
                </button>
                <button class="theme-dot" :class="theme === 'system' ? 'theme-dot-active' : ''" title="跟随系统" @click="setTheme('system')">
                  <Icon icon="solar:monitor-bold-duotone" class="size-4" />
                </button>
              </div>
            </div>

            <div class="ml-2 flex items-center gap-1 text-slate-500 dark:text-slate-300">
              <button class="grid size-8 place-items-center rounded-full hover:bg-white/80 dark:hover:bg-slate-900" title="最小化" @click="minimizeWindow">
                <Icon icon="fluent:minimize-16-regular" class="size-4" />
              </button>
              <button class="grid size-8 place-items-center rounded-full hover:bg-white/80 dark:hover:bg-slate-900" title="最大化" @click="maximizeWindow">
                <Icon icon="fluent:maximize-16-regular" class="size-4" />
              </button>
              <button class="grid size-8 place-items-center rounded-full hover:bg-rose-50 hover:text-rose-500 dark:hover:bg-rose-950/60" title="关闭" @click="closeWindow">
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
                <span class="flex items-center gap-1.5 text-[12px] text-slate-500 dark:text-slate-400"><i class="size-2 rounded-full bg-green-500"></i>在线</span>
              </div>
              <div class="flex items-center gap-5">
                <div class="grid size-[68px] shrink-0 grid-cols-3 gap-1 rounded-xl bg-slate-900 p-2 shadow-lg shadow-slate-950/12 dark:bg-slate-800">
                  <span v-for="index in 9" :key="index" class="rounded-[3px] bg-slate-600"></span>
                </div>
                <div class="min-w-0 flex-1">
                  <p class="truncate text-[14px] font-semibold">{{ workspace.selectedDevice }}</p>
                  <p class="mt-2 text-[12px] text-slate-500 dark:text-slate-400">15 按键</p>
                  <p class="mt-1 flex items-center gap-1 text-[12px] text-green-600"><Icon icon="solar:battery-charge-bold-duotone" class="size-4" />100%</p>
                </div>
              </div>
              <div class="mt-5 flex justify-end">
                <button class="rounded-full border border-sky-500/60 px-4 py-2 text-[12px] font-medium text-sky-600 hover:bg-sky-50 dark:hover:bg-sky-950/40">设备管理</button>
              </div>
            </div>

            <div class="soft-card p-5">
              <h2 class="text-[16px] font-semibold">快捷操作</h2>
              <div class="mt-4 grid gap-3">
                <button v-for="item in quickActions" :key="item.label" class="soft-row group">
                  <span class="grid size-8 place-items-center rounded-xl bg-white shadow-sm dark:bg-slate-800"><Icon :icon="item.icon" :class="['size-5', item.color]" /></span>
                  <span class="min-w-0 flex-1 truncate text-left text-[13px] font-medium">{{ item.label }}</span>
                  <Icon icon="solar:alt-arrow-right-linear" class="size-4 text-slate-400 transition group-hover:translate-x-0.5" />
                </button>
              </div>
            </div>

            <div class="soft-card col-span-2 p-5">
              <h2 class="text-[16px] font-semibold">快速开始</h2>
              <div class="mt-4 grid grid-cols-3 gap-4">
                <button v-for="item in quickStart" :key="item.label" class="soft-start">
                  <span class="grid size-10 place-items-center rounded-2xl bg-white shadow-sm dark:bg-slate-800"><Icon :icon="item.icon" :class="['size-6', item.color]" /></span>
                  <span class="min-w-0"><span class="block truncate text-[13px] font-semibold">{{ item.label }}</span><span class="mt-1 block truncate text-[12px] text-slate-500 dark:text-slate-400">{{ item.desc }}</span></span>
                </button>
              </div>
            </div>
          </section>

          <section v-else-if="activeView === 'component' && componentRoute === 'manager'" class="soft-card h-full p-5">
            <div class="mb-5 flex items-center justify-between">
              <div><h2 class="text-[16px] font-semibold">组件管理</h2><p class="mt-1 text-[12px] text-slate-500">管理组件、导入导出组件包，选择组件后进入编辑页面</p></div>
              <button class="rounded-full bg-sky-500 px-4 py-2 text-[12px] font-medium text-white">新建组件</button>
            </div>
            <div class="grid grid-cols-2 gap-3">
              <button v-for="item in components" :key="item.name" class="rounded-[20px] bg-white/72 p-4 text-left shadow-sm transition hover:-translate-y-0.5 hover:shadow-md dark:bg-slate-900/70" @click="componentRoute = 'editor'">
                <div class="flex items-center justify-between">
                  <span class="text-[14px] font-semibold">{{ item.name }}</span>
                  <span class="text-[11px]" :class="item.status === '缺少插件' ? 'text-rose-500' : item.status === '待确认' ? 'text-amber-500' : 'text-green-600'">{{ item.status }}</span>
                </div>
                <p class="mt-3 text-[12px] text-slate-500">{{ item.mode }} · {{ item.actions }} 个动作 · {{ item.ratio }}</p>
              </button>
            </div>
          </section>

          <section v-else-if="activeView === 'component'" class="grid h-full grid-cols-[260px_1fr_260px] gap-4">
            <aside class="soft-card p-4">
              <button class="mb-4 flex items-center gap-2 text-[12px] text-sky-600" @click="componentRoute = 'manager'"><Icon icon="solar:alt-arrow-left-linear" class="size-4" />返回组件管理</button>
              <template v-if="componentEditorMode === 'visual'">
                <h2 class="text-[16px] font-semibold">场景切换</h2>
                <p class="mt-1 text-[12px] text-slate-500">可视化组件 · 1:1</p>
                <div class="mt-5 grid gap-2">
                <button class="editor-nav-active">基础样式</button>
                <button class="editor-nav">背景与媒体</button>
                <button class="editor-nav">文字与图标</button>
                <button class="editor-nav">锁定/按下状态</button>
                <button class="editor-nav">动作系统</button>
                <button class="editor-nav" @click="showPermissionDialog = true">权限声明</button>
                </div>
              </template>
              <template v-else>
                <h2 class="text-[16px] font-semibold">文件</h2>
                <p class="mt-1 text-[12px] text-slate-500">场景切换组件目录</p>
                <div class="mt-5 grid gap-1.5 text-[12px]">
                  <button class="file-row file-row-active"><Icon icon="solar:file-text-bold-duotone" class="size-4" />SceneButton.vue</button>
                  <button class="file-row"><Icon icon="solar:code-file-bold-duotone" class="size-4" />actions.ts</button>
                  <button class="file-row"><Icon icon="solar:code-file-bold-duotone" class="size-4" />onedesk.component.json</button>
                  <button class="file-row"><Icon icon="solar:folder-bold-duotone" class="size-4" />assets</button>
                </div>
              </template>
            </aside>

            <section class="soft-card min-w-0 p-4">
              <div class="mb-4 flex items-center justify-between">
                <div class="flex rounded-full bg-white/72 p-1 text-[12px] shadow-sm dark:bg-slate-900">
                  <button class="rounded-full px-3 py-1.5" :class="componentEditorMode === 'visual' ? 'bg-sky-500 text-white' : ''" @click="componentEditorMode = 'visual'">可视化</button>
                  <button class="rounded-full px-3 py-1.5" :class="componentEditorMode === 'code' ? 'bg-sky-500 text-white' : ''" @click="showCodeSwitchDialog = true">代码</button>
                </div>
                <button class="rounded-full bg-sky-500 px-4 py-2 text-[12px] font-medium text-white" @click="startExport">导出组件</button>
              </div>

              <div v-if="componentEditorMode === 'visual'" class="grid gap-3">
                <div class="rounded-[18px] bg-white/72 p-4 shadow-sm dark:bg-slate-900/70">
                  <h3 class="text-[13px] font-semibold">样式配置</h3>
                  <div class="mt-3 grid grid-cols-4 gap-2 text-[12px]">
                    <select class="field"><option>渐变背景</option><option>纯色背景</option><option>图片背景</option><option>视频背景</option></select>
                    <input class="field" value="圆角 16" />
                    <input class="field" value="边距 8" />
                    <select class="field"><option>居中</option><option>靠左</option><option>靠右</option><option>靠下</option></select>
                    <input class="field" value="启动场景" />
                    <input class="field" value="字号 14" />
                    <input class="field" value="#0ea5e9" />
                    <select class="field"><option>按下缩小</option><option>按下高亮</option></select>
                  </div>
                </div>

                <div class="rounded-[18px] bg-white/72 p-4 shadow-sm dark:bg-slate-900/70">
                  <div class="mb-3 flex items-center justify-between"><h3 class="text-[13px] font-semibold">动作配置</h3><button class="text-[12px] text-sky-600">添加动作</button></div>
                  <div class="grid gap-2 text-[12px]">
                    <div class="grid grid-cols-[120px_1fr_80px] rounded-xl bg-slate-50 px-3 py-2 dark:bg-slate-800"><span>三指上滑</span><span>调用 OBS Control / 切换场景</span><span class="text-right text-green-600">已授权</span></div>
                    <div class="grid grid-cols-[120px_1fr_80px] rounded-xl bg-slate-50 px-3 py-2 dark:bg-slate-800"><span>长按</span><span>发送系统通知</span><span class="text-right text-green-600">已授权</span></div>
                  </div>
                </div>
              </div>
              <pre v-else class="h-[430px] overflow-auto rounded-[18px] bg-slate-950 p-4 text-[12px] leading-6 text-sky-100"><code>&lt;script setup lang="ts"&gt;
const title = '启动场景'
&lt;/script&gt;

&lt;template&gt;
  &lt;button class="control-tile"&gt;&#123;&#123; title &#125;&#125;&lt;/button&gt;
&lt;/template&gt;</code></pre>
            </section>

            <aside class="soft-card p-4">
              <h3 class="text-[13px] font-semibold">实时预览</h3>
              <div class="mt-4 grid aspect-square place-items-center overflow-hidden rounded-[22px] bg-gradient-to-br from-sky-400 to-cyan-300 text-white shadow-lg shadow-sky-500/18">
                <div class="text-center"><Icon icon="solar:bolt-circle-bold-duotone" class="mx-auto size-10" /><p class="mt-2 text-[13px] font-semibold">启动场景</p></div>
              </div>
              <div class="mt-4 grid gap-2 text-[12px] text-slate-500">
                <p>预览比例：1:1</p><p>溢出策略：隐藏</p><p>权限：插件调用、系统通知</p>
              </div>
            </aside>
          </section>

          <section v-else-if="activeView === 'page' && pageRoute === 'manager'" class="soft-card h-full p-5">
            <div class="mb-5 flex items-center justify-between"><div><h2 class="text-[16px] font-semibold">页面管理</h2><p class="mt-1 text-[12px] text-slate-500">页面包含格子矩阵和组件绑定，选择页面后进入编辑页面</p></div><button class="rounded-full bg-sky-500 px-4 py-2 text-[12px] font-medium text-white">新建页面</button></div>
            <div class="grid grid-cols-4 gap-3">
              <button v-for="page in pages" :key="page.name" class="rounded-[20px] bg-white/72 p-4 text-left shadow-sm dark:bg-slate-900/70" @click="pageRoute = 'editor'">
                <Icon icon="solar:smartphone-bold-duotone" class="size-8 text-sky-500" /><p class="mt-3 text-[14px] font-semibold">{{ page.name }}</p><p class="mt-1 text-[12px] text-slate-500">{{ page.grid }} · {{ page.components }} 组件 · {{ page.background }}</p>
              </button>
            </div>
          </section>

          <section v-else-if="activeView === 'page'" class="soft-card h-full p-5">
            <button class="mb-4 flex items-center gap-2 text-[12px] text-sky-600" @click="pageRoute = 'manager'"><Icon icon="solar:alt-arrow-left-linear" class="size-4" />返回页面管理</button>
            <div class="grid h-[calc(100%-32px)] grid-cols-[260px_1fr] gap-5">
              <aside class="rounded-[18px] bg-white/72 p-4 shadow-sm dark:bg-slate-900/70">
                <h2 class="text-[16px] font-semibold">采集页面</h2>
                <div class="mt-4 grid gap-2 text-[12px]">
                  <input class="field" value="4 行" /><input class="field" value="3 列" /><input class="field" value="格子间距 10" /><input class="field" value="页面边距 自动居中" /><select class="field"><option>渐变背景</option><option>图片背景</option><option>视频背景</option></select>
                </div>
              </aside>
              <div class="grid place-items-center rounded-[22px] bg-white/72 p-6 shadow-sm dark:bg-slate-900/70">
                <div class="grid aspect-[4/3] w-full max-w-[520px] grid-cols-4 grid-rows-3 gap-2 overflow-hidden rounded-[20px] bg-slate-100 p-3 dark:bg-slate-800">
                  <div v-for="index in 12" :key="index" class="rounded-xl border border-slate-200 bg-white/80 dark:border-slate-700 dark:bg-slate-900/80"></div>
                </div>
              </div>
            </div>
          </section>

          <section v-else-if="activeView === 'scheme' && schemeRoute === 'manager'" class="soft-card h-full p-5">
            <div class="mb-5 flex items-center justify-between"><div><h2 class="text-[16px] font-semibold">方案管理</h2><p class="mt-1 text-[12px] text-slate-500">方案是最终应用到移动端的唯一成品</p></div><button class="rounded-full bg-sky-500 px-4 py-2 text-[12px] font-medium text-white">新建方案</button></div>
            <div class="grid grid-cols-3 gap-3">
              <button v-for="scheme in schemes" :key="scheme.name" class="rounded-[20px] bg-white/72 p-4 text-left shadow-sm dark:bg-slate-900/70" @click="schemeRoute = 'editor'">
                <p class="text-[14px] font-semibold">{{ scheme.name }}</p><p class="mt-2 text-[12px] text-slate-500">{{ scheme.pages }} 页面 · {{ scheme.devices }} 设备</p><p class="mt-3 text-[12px] text-sky-600">{{ scheme.status }}</p>
              </button>
            </div>
          </section>

          <section v-else-if="activeView === 'scheme'" class="soft-card h-full p-5">
            <button class="mb-4 flex items-center gap-2 text-[12px] text-sky-600" @click="schemeRoute = 'manager'"><Icon icon="solar:alt-arrow-left-linear" class="size-4" />返回方案管理</button>
            <div class="grid h-[calc(100%-32px)] grid-cols-[230px_1fr] gap-5">
              <aside class="rounded-[18px] bg-white/72 p-4 shadow-sm dark:bg-slate-900/70">
                <h2 class="text-[16px] font-semibold">直播控制台</h2>
                <div class="mt-4 grid gap-2 text-[12px]">
                  <button v-for="page in pages" :key="page.name" class="rounded-xl bg-slate-50 px-3 py-2 text-left dark:bg-slate-800">{{ page.name }}</button>
                </div>
              </aside>
              <div class="rounded-[22px] bg-white/72 p-5 shadow-sm dark:bg-slate-900/70">
                <h3 class="text-[13px] font-semibold">页面流程</h3>
                <div class="mt-5 grid grid-cols-4 gap-4">
                  <div v-for="page in pages" :key="page.name" class="rounded-2xl border border-slate-200 p-4 text-center dark:border-slate-700"><Icon icon="solar:smartphone-bold-duotone" class="mx-auto size-8 text-sky-500" /><p class="mt-2 text-[13px] font-semibold">{{ page.name }}</p></div>
                </div>
                <div class="mt-5 grid gap-2 text-[12px] text-slate-500">
                  <p>采集 -> 直播：三指上滑 / 淡入淡出</p><p>直播 -> 剪辑：三指右滑 / 平移</p><p>剪辑 -> 系统：五指点击 / 缩放</p>
                </div>
              </div>
            </div>
          </section>

          <section v-else class="soft-card h-full overflow-auto p-5">
            <div class="mb-4 flex items-center justify-between"><h2 class="text-[16px] font-semibold">{{ viewTitle }}</h2><button class="rounded-full bg-sky-500 px-3 py-1.5 text-[12px] font-medium text-white" @click="startExport">执行操作</button></div>
            <div class="grid gap-2">
              <div v-for="item in activeView === 'permission' ? permissions : logs" :key="typeof item === 'string' ? item : item.id" class="rounded-2xl bg-white/72 px-4 py-3 text-[13px] shadow-sm dark:bg-slate-900/70">
                <template v-if="typeof item === 'string'">{{ item }}</template>
                <template v-else><div class="flex items-center justify-between"><div><p class="font-semibold">{{ item.label }}</p><p class="mt-1 text-[12px] text-slate-500">{{ item.category }} · {{ item.id }}</p></div><span :class="item.risk === '高危' ? 'text-rose-500' : 'text-sky-600'">{{ item.risk }}</span></div></template>
              </div>
            </div>
          </section>
        </div>

        <footer class="h-8 shrink-0">
          <div v-if="exporting" class="mt-3 h-1.5 overflow-hidden rounded-full bg-white/70 dark:bg-slate-900"><div class="h-full rounded-full bg-sky-500 transition-all" :style="{ width: `${exportProgress}%` }"></div></div>
          <p v-else class="mt-3 text-[12px] text-slate-500">{{ workspace.toast }}</p>
        </footer>
      </section>
    </section>

    <div v-if="showPermissionDialog" class="fixed inset-0 grid place-items-center bg-slate-950/28 p-6 backdrop-blur-sm">
      <div class="w-full max-w-[460px] rounded-3xl bg-white p-5 shadow-2xl dark:bg-slate-950">
        <div class="flex items-center justify-between"><h3 class="text-[16px] font-semibold">确认授权</h3><button class="grid size-8 place-items-center rounded-full bg-slate-100 dark:bg-slate-900" @click="showPermissionDialog = false"><Icon icon="solar:close-circle-bold-duotone" class="size-5" /></button></div>
        <div class="mt-4 grid gap-2">
          <label v-for="permission in permissions" :key="permission.id" class="flex items-center gap-3 rounded-2xl bg-slate-50 px-3 py-2.5 text-[13px] dark:bg-slate-900">
            <input type="checkbox" checked class="size-4 accent-sky-500" /><span class="min-w-0 flex-1"><span class="block font-medium">{{ permission.label }}</span><span class="mt-0.5 block text-[12px] text-slate-500">{{ permission.category }}</span></span><span v-if="permission.risk === '高危'" class="rounded-full bg-rose-100 px-2 py-1 text-[11px] font-medium text-rose-600 dark:bg-rose-950 dark:text-rose-300">高危</span>
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
        <div class="mt-4 flex gap-2"><button class="flex-1 rounded-2xl bg-slate-100 py-2.5 text-[13px] font-medium dark:bg-slate-900" @click="showCodeSwitchDialog = false">取消</button><button class="flex-1 rounded-2xl bg-sky-500 py-2.5 text-[13px] font-medium text-white" @click="componentEditorMode = 'code'; showCodeSwitchDialog = false">继续</button></div>
      </div>
    </div>
  </main>
</template>
