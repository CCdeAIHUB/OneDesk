<script setup lang="ts">
import { Icon } from "@iconify/vue";
import { computed, onMounted, ref } from "vue";
import type { ComponentDefinition, PageDefinition, SchemeDefinition, SectionRoute, ThemeMode, ViewKey } from "./domain";
import { closeWindow, maximizeWindow, minimizeWindow, setShellTheme, startWindowDrag } from "./nativeBridge";
import { applyScheme, loadWorkspace, navItems, quickActions, quickStart, workspace } from "./workspace";

const activeView = ref<ViewKey>("home");
const theme = ref<ThemeMode>("system");
const componentRoute = ref<SectionRoute>("manager");
const pageRoute = ref<SectionRoute>("manager");
const schemeRoute = ref<SectionRoute>("manager");
const componentEditorMode = ref<"visual" | "code">("visual");
const previewRatio = ref("1:1");
const showPermissionDialog = ref(false);
const showCodeSwitchDialog = ref(false);
const exporting = ref(false);
const exportProgress = ref(0);
const isMaximized = ref(false);

const selectedComponent = computed<ComponentDefinition | undefined>(() => workspace.components.find((item) => item.id === workspace.selectedComponentId) ?? workspace.components[0]);
const selectedPage = computed<PageDefinition | undefined>(() => workspace.pages.find((item) => item.id === workspace.selectedPageId) ?? workspace.pages[0]);
const selectedScheme = computed<SchemeDefinition | undefined>(() => workspace.schemes.find((item) => item.id === workspace.selectedSchemeId) ?? workspace.schemes[0]);
const viewTitle = computed(() => navItems.find((item) => item.key === activeView.value)?.label ?? "首页");
const permissionRows = computed(() => workspace.capabilities.flatMap((category) => category.capabilities));

function navIcon(item: (typeof navItems)[number]) {
  return activeView.value === item.key ? item.icon.replace("-bold-duotone", "-bold") : item.icon;
}

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
  void setShellTheme(resolved);
}

onMounted(async () => {
  setTheme(theme.value);
  await loadWorkspace();
});

async function toggleMaximize() {
  isMaximized.value = await maximizeWindow();
}

function handleWindowDrag(event: PointerEvent) {
  if (event.button !== 0 || isMaximized.value) return;
  const target = event.target instanceof Element ? event.target : null;
  if (target?.closest("button,input,select,textarea,a,nav,.soft-card,.soft-row,.soft-start,.theme-dot,.window-controls,.no-drag")) return;
  void startWindowDrag();
}

function chooseComponent(component: ComponentDefinition) {
  workspace.selectedComponentId = component.id;
  componentEditorMode.value = String(component.editMode).toLowerCase() === "code" ? "code" : "visual";
  componentRoute.value = "editor";
}

function choosePage(page: PageDefinition) {
  workspace.selectedPageId = page.id;
  pageRoute.value = "editor";
}

function chooseScheme(scheme: SchemeDefinition) {
  workspace.selectedSchemeId = scheme.id;
  schemeRoute.value = "editor";
}

function requestCodeMode() {
  if (componentEditorMode.value === "code") return;
  showCodeSwitchDialog.value = true;
}

function startExport(label = "导出完成") {
  exporting.value = true;
  exportProgress.value = 10;
  const timer = window.setInterval(() => {
    exportProgress.value += 18;
    if (exportProgress.value >= 100) {
      exportProgress.value = 100;
      window.clearInterval(timer);
      window.setTimeout(() => {
        exporting.value = false;
        workspace.toast = label;
      }, 360);
    }
  }, 160);
}
</script>

<template>
  <main class="h-screen w-screen overflow-hidden text-slate-950 dark:text-slate-100" @pointerdown="handleWindowDrag">
    <section class="app-shell flex h-full min-h-[720px] min-w-[1120px] overflow-hidden bg-white/75 backdrop-blur-2xl dark:bg-black/80">
      <aside class="flex w-[96px] shrink-0 items-start justify-center py-9">
        <nav class="flex w-[54px] flex-col items-center gap-4 rounded-[28px] bg-white px-2 py-4 shadow-[0_16px_40px_rgba(15,23,42,0.08)] dark:bg-slate-950">
          <button
            v-for="item in navItems"
            :key="item.key"
            class="grid size-10 place-items-center rounded-full transition"
            :class="activeView === item.key ? 'bg-sky-500 text-white shadow-[0_10px_24px_rgba(14,165,233,0.35)]' : 'text-slate-500 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-slate-800'"
            :title="item.label"
            @click="openView(item.key)"
          >
            <Icon :icon="navIcon(item)" class="size-[21px]" />
          </button>
        </nav>
      </aside>

      <section class="flex min-w-0 flex-1 flex-col px-8 py-6">
        <header class="flex h-11 shrink-0 items-center justify-between gap-5">
          <div class="w-[320px] shrink-0 overflow-hidden">
            <h1 class="truncate text-[20px] font-semibold leading-6">你好，OneDesk!</h1>
            <p class="mt-1 text-[12px] text-slate-500 dark:text-slate-400">今天也要高效控制每一个瞬间</p>
          </div>

          <div class="flex min-w-0 flex-1 items-center justify-end gap-3">
            <div class="group relative flex size-8 items-start justify-center overflow-visible rounded-full">
              <button class="grid size-8 shrink-0 place-items-center rounded-full text-slate-500 dark:text-slate-300" title="主题">
                <Icon :icon="theme === 'dark' ? 'solar:moon-bold-duotone' : theme === 'light' ? 'solar:sun-2-bold-duotone' : 'solar:monitor-bold-duotone'" class="size-4" />
              </button>
              <div class="absolute left-1/2 top-0 z-20 flex h-8 w-8 -translate-x-1/2 flex-col items-center gap-1 overflow-hidden rounded-full bg-white p-1 opacity-0 shadow-lg shadow-slate-950/10 transition-all duration-200 group-hover:h-[88px] group-hover:opacity-100 dark:bg-slate-950">
                <button class="theme-dot" :class="theme === 'light' ? 'theme-dot-active' : ''" title="浅色" @click="setTheme('light')"><Icon icon="solar:sun-2-bold-duotone" class="size-4" /></button>
                <button class="theme-dot" :class="theme === 'dark' ? 'theme-dot-active' : ''" title="深色" @click="setTheme('dark')"><Icon icon="solar:moon-bold-duotone" class="size-4" /></button>
                <button class="theme-dot" :class="theme === 'system' ? 'theme-dot-active' : ''" title="跟随系统" @click="setTheme('system')"><Icon icon="solar:monitor-bold-duotone" class="size-4" /></button>
              </div>
            </div>

            <div class="window-controls ml-2 flex items-center gap-1 text-slate-500 dark:text-slate-300">
              <button class="window-control" title="最小化" @click="minimizeWindow"><span class="win-symbol">&#xE921;</span></button>
              <button class="window-control" :title="isMaximized ? '还原' : '最大化'" @click="toggleMaximize"><span class="win-symbol" v-html="isMaximized ? '&#xE923;' : '&#xE922;'"></span></button>
              <button class="window-control window-control-close" title="关闭" @click="closeWindow"><span class="win-symbol">&#xE8BB;</span></button>
            </div>
          </div>
        </header>

        <div class="min-h-0 flex-1 pt-8">
          <section v-if="activeView === 'home'" class="grid h-full grid-cols-[1.25fr_0.9fr] grid-rows-[240px_1fr] gap-5">
            <div class="soft-card p-5">
              <div class="mb-4 flex items-start justify-between">
                <div>
                  <h2 class="text-[16px] font-semibold">设备状态</h2>
                  <p class="mt-2 text-[12px] text-slate-500 dark:text-slate-400">已发现 {{ workspace.devices.length || 1 }} 个设备</p>
                </div>
                <span class="flex items-center gap-1.5 text-[12px] text-slate-500 dark:text-slate-400"><i class="size-2 rounded-full bg-green-500"></i>在线</span>
              </div>
              <div class="flex items-center gap-5">
                <div class="grid size-[68px] shrink-0 grid-cols-3 gap-1 rounded-xl bg-slate-900 p-2 shadow-lg shadow-slate-950/12 dark:bg-slate-800">
                  <span v-for="index in 9" :key="index" class="rounded-[3px] bg-slate-600"></span>
                </div>
                <div class="min-w-0 flex-1">
                  <p class="truncate text-[14px] font-semibold">{{ workspace.selectedDevice }}</p>
                  <p class="mt-2 text-[12px] text-slate-500 dark:text-slate-400">{{ workspace.components.length }} 组件 · {{ workspace.pages.length }} 页面 · {{ workspace.schemes.length }} 方案</p>
                  <p class="mt-1 flex items-center gap-1 text-[12px] text-green-600"><Icon icon="solar:verified-check-bold-duotone" class="size-4" />工作区已校验</p>
                </div>
              </div>
              <div class="mt-5 flex justify-end"><button class="rounded-full border border-sky-500/60 px-4 py-2 text-[12px] font-medium text-sky-600 hover:bg-sky-50 dark:hover:bg-sky-950/40">设备管理</button></div>
            </div>

            <div class="soft-card p-5">
              <h2 class="text-[16px] font-semibold">快捷操作</h2>
              <div class="mt-4 grid gap-3">
                <button v-for="item in quickActions" :key="item.label" class="soft-row group" @click="startExport(item.label.includes('导入') ? '导入完成' : '操作完成')">
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

          <section v-else-if="activeView === 'component' && componentRoute === 'manager'" class="soft-card h-full overflow-auto p-5">
            <div class="mb-5 flex items-center justify-between">
              <div><h2 class="text-[16px] font-semibold">组件管理</h2><p class="mt-1 text-[12px] text-slate-500">组件独立管理，进入后再编辑可视化配置或代码文件。</p></div>
              <button class="rounded-full bg-sky-500 px-4 py-2 text-[12px] font-medium text-white" @click="startExport('组件已创建')">新建组件</button>
            </div>
            <div class="grid grid-cols-2 gap-3">
              <button v-for="item in workspace.components" :key="item.id" class="rounded-[20px] bg-white p-4 text-left shadow-sm transition hover:-translate-y-0.5 hover:shadow-md dark:bg-slate-900" @click="chooseComponent(item)">
                <div class="flex items-center justify-between"><span class="text-[14px] font-semibold">{{ item.name }}</span><span class="text-[11px] text-sky-600">{{ String(item.editMode).toLowerCase() === 'code' ? '代码' : '可视化' }}</span></div>
                <p class="mt-3 text-[12px] text-slate-500">{{ item.actionIds.length }} 个动作 · {{ item.requestedPermissions.length }} 项权限 · {{ item.version }}</p>
              </button>
            </div>
          </section>

          <section v-else-if="activeView === 'component'" class="grid h-full grid-cols-[260px_1fr_260px] gap-4">
            <aside class="soft-card p-4">
              <button class="mb-4 flex items-center gap-2 text-[12px] text-sky-600" @click="componentRoute = 'manager'"><Icon icon="solar:alt-arrow-left-linear" class="size-4" />返回组件管理</button>
              <template v-if="componentEditorMode === 'visual'">
                <h2 class="text-[16px] font-semibold">{{ selectedComponent?.name }}</h2>
                <p class="mt-1 text-[12px] text-slate-500">可视化组件 · {{ previewRatio }}</p>
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
                <h2 class="text-[16px] font-semibold">组件文件</h2>
                <p class="mt-1 text-[12px] text-slate-500">{{ selectedComponent?.entryFile }}</p>
                <div class="mt-5 grid gap-1.5 text-[12px]">
                  <button class="file-row file-row-active"><Icon icon="solar:file-text-bold-duotone" class="size-4" />{{ selectedComponent?.entryFile }}</button>
                  <button class="file-row"><Icon icon="solar:code-file-bold-duotone" class="size-4" />actions.ts</button>
                  <button class="file-row"><Icon icon="solar:code-file-bold-duotone" class="size-4" />onedesk.component.json</button>
                  <button class="file-row"><Icon icon="solar:folder-bold-duotone" class="size-4" />assets</button>
                </div>
              </template>
            </aside>

            <section class="soft-card min-w-0 p-4">
              <div class="mb-4 flex items-center justify-between">
                <div class="flex rounded-full bg-white p-1 text-[12px] shadow-sm dark:bg-slate-900">
                  <button class="rounded-full px-3 py-1.5" :class="componentEditorMode === 'visual' ? 'bg-sky-500 text-white' : ''" @click="componentEditorMode = 'visual'">可视化</button>
                  <button class="rounded-full px-3 py-1.5" :class="componentEditorMode === 'code' ? 'bg-sky-500 text-white' : ''" @click="requestCodeMode">代码</button>
                </div>
                <button class="rounded-full bg-sky-500 px-4 py-2 text-[12px] font-medium text-white" @click="startExport('组件导出完成')">导出组件</button>
              </div>

              <div v-if="componentEditorMode === 'visual'" class="grid gap-3">
                <div class="rounded-[18px] bg-white p-4 shadow-sm dark:bg-slate-900">
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

                <div class="rounded-[18px] bg-white p-4 shadow-sm dark:bg-slate-900">
                  <div class="mb-3 flex items-center justify-between"><h3 class="text-[13px] font-semibold">动作配置</h3><button class="text-[12px] text-sky-600">添加动作</button></div>
                  <div class="grid gap-2 text-[12px]">
                    <div v-for="actionId in selectedComponent?.actionIds" :key="actionId" class="grid grid-cols-[120px_1fr_80px] rounded-xl bg-slate-50 px-3 py-2 dark:bg-slate-800">
                      <span>{{ workspace.actions.find((action) => action.id === actionId)?.trigger.displayName ?? '未设置' }}</span>
                      <span>{{ workspace.actions.find((action) => action.id === actionId)?.name ?? actionId }}</span>
                      <span class="text-right text-green-600">已授权</span>
                    </div>
                  </div>
                </div>
              </div>
              <pre v-else class="h-[430px] overflow-auto rounded-[18px] bg-slate-950 p-4 text-[12px] leading-6 text-sky-100"><code>&lt;script setup lang="ts"&gt;
const title = '{{ selectedComponent?.name }}'
&lt;/script&gt;

&lt;template&gt;
  &lt;button class="control-tile"&gt;&#123;&#123; title &#125;&#125;&lt;/button&gt;
&lt;/template&gt;</code></pre>
            </section>

            <aside class="soft-card p-4">
              <div class="flex items-center justify-between"><h3 class="text-[13px] font-semibold">实时预览</h3><select v-model="previewRatio" class="field w-[82px]"><option>1:1</option><option>2:3</option><option>4:6</option></select></div>
              <div class="mt-4 grid overflow-hidden rounded-[22px] bg-gradient-to-br from-sky-400 to-cyan-300 text-white shadow-lg shadow-sky-500/18" :class="previewRatio === '1:1' ? 'aspect-square' : previewRatio === '2:3' ? 'aspect-[2/3]' : 'aspect-[4/6]'">
                <div class="grid place-items-center text-center"><div><Icon icon="solar:bolt-circle-bold-duotone" class="mx-auto size-10" /><p class="mt-2 text-[13px] font-semibold">{{ selectedComponent?.name }}</p></div></div>
              </div>
              <div class="mt-4 grid gap-2 text-[12px] text-slate-500"><p>预览比例：{{ previewRatio }}</p><p>溢出策略：隐藏</p><p>权限：{{ selectedComponent?.requestedPermissions.length }} 项</p></div>
            </aside>
          </section>

          <section v-else-if="activeView === 'page' && pageRoute === 'manager'" class="soft-card h-full overflow-auto p-5">
            <div class="mb-5 flex items-center justify-between"><div><h2 class="text-[16px] font-semibold">页面管理</h2><p class="mt-1 text-[12px] text-slate-500">页面包含格子矩阵和组件绑定，选择页面后进入编辑。</p></div><button class="rounded-full bg-sky-500 px-4 py-2 text-[12px] font-medium text-white" @click="startExport('页面已创建')">新建页面</button></div>
            <div class="grid grid-cols-4 gap-3">
              <button v-for="page in workspace.pages" :key="page.id" class="rounded-[20px] bg-white p-4 text-left shadow-sm dark:bg-slate-900" @click="choosePage(page)">
                <Icon icon="solar:smartphone-bold-duotone" class="size-8 text-sky-500" />
                <p class="mt-3 text-[14px] font-semibold">{{ page.name }}</p>
                <p class="mt-1 text-[12px] text-slate-500">{{ page.rows }} x {{ page.columns }} · {{ page.cells.filter((cell) => cell.componentId).length }} 组件 · {{ page.backgroundKind }}</p>
              </button>
            </div>
          </section>

          <section v-else-if="activeView === 'page'" class="soft-card h-full p-5">
            <button class="mb-4 flex items-center gap-2 text-[12px] text-sky-600" @click="pageRoute = 'manager'"><Icon icon="solar:alt-arrow-left-linear" class="size-4" />返回页面管理</button>
            <div class="grid h-[calc(100%-32px)] grid-cols-[260px_1fr] gap-5">
              <aside class="rounded-[18px] bg-white p-4 shadow-sm dark:bg-slate-900">
                <h2 class="text-[16px] font-semibold">{{ selectedPage?.name }}</h2>
                <div class="mt-4 grid gap-2 text-[12px]">
                  <input class="field" :value="`${selectedPage?.rows ?? 0} 行`" />
                  <input class="field" :value="`${selectedPage?.columns ?? 0} 列`" />
                  <input class="field" :value="`行间距 ${selectedPage?.spacing.rowGap ?? 0}`" />
                  <input class="field" :value="`列间距 ${selectedPage?.spacing.columnGap ?? 0}`" />
                  <select class="field"><option>{{ selectedPage?.backgroundKind }}</option><option>图片背景</option><option>视频背景</option></select>
                </div>
              </aside>
              <div class="grid place-items-center rounded-[22px] bg-white p-6 shadow-sm dark:bg-slate-900">
                <div class="grid aspect-[4/3] w-full max-w-[520px] gap-2 overflow-hidden rounded-[20px] bg-slate-100 p-3 dark:bg-slate-800" :style="{ gridTemplateColumns: `repeat(${selectedPage?.columns ?? 3}, minmax(0, 1fr))`, gridTemplateRows: `repeat(${selectedPage?.rows ?? 3}, minmax(0, 1fr))` }">
                  <div v-for="cell in selectedPage?.cells" :key="cell.id" class="overflow-hidden rounded-xl border border-slate-200 bg-white text-[10px] text-slate-500 dark:border-slate-700 dark:bg-slate-900" :style="{ gridColumn: `span ${cell.columnSpan} / span ${cell.columnSpan}`, gridRow: `span ${cell.rowSpan} / span ${cell.rowSpan}` }">
                    <span v-if="cell.componentId" class="grid h-full place-items-center">{{ workspace.components.find((item) => item.id === cell.componentId)?.name }}</span>
                  </div>
                </div>
              </div>
            </div>
          </section>

          <section v-else-if="activeView === 'scheme' && schemeRoute === 'manager'" class="soft-card h-full overflow-auto p-5">
            <div class="mb-5 flex items-center justify-between"><div><h2 class="text-[16px] font-semibold">方案管理</h2><p class="mt-1 text-[12px] text-slate-500">方案是最终应用到移动端的唯一成品。</p></div><button class="rounded-full bg-sky-500 px-4 py-2 text-[12px] font-medium text-white" @click="startExport('方案已创建')">新建方案</button></div>
            <div class="grid grid-cols-3 gap-3">
              <button v-for="scheme in workspace.schemes" :key="scheme.id" class="rounded-[20px] bg-white p-4 text-left shadow-sm dark:bg-slate-900" @click="chooseScheme(scheme)">
                <p class="text-[14px] font-semibold">{{ scheme.name }}</p>
                <p class="mt-2 text-[12px] text-slate-500">{{ scheme.pageIds.length }} 页面 · {{ scheme.pluginDependencies.length }} 插件依赖</p>
                <p class="mt-3 text-[12px]" :class="workspace.activeSchemeId === scheme.id ? 'text-sky-600' : 'text-slate-500'">{{ workspace.activeSchemeId === scheme.id ? '已应用' : '未应用' }}</p>
              </button>
            </div>
          </section>

          <section v-else-if="activeView === 'scheme'" class="soft-card h-full p-5">
            <div class="mb-4 flex items-center justify-between">
              <button class="flex items-center gap-2 text-[12px] text-sky-600" @click="schemeRoute = 'manager'"><Icon icon="solar:alt-arrow-left-linear" class="size-4" />返回方案管理</button>
              <button class="rounded-full bg-sky-500 px-4 py-2 text-[12px] font-medium text-white" @click="selectedScheme && applyScheme(selectedScheme.id)">应用方案</button>
            </div>
            <div class="grid h-[calc(100%-42px)] grid-cols-[230px_1fr] gap-5">
              <aside class="rounded-[18px] bg-white p-4 shadow-sm dark:bg-slate-900">
                <h2 class="text-[16px] font-semibold">{{ selectedScheme?.name }}</h2>
                <div class="mt-4 grid gap-2 text-[12px]">
                  <button v-for="pageId in selectedScheme?.pageIds" :key="pageId" class="rounded-xl bg-slate-50 px-3 py-2 text-left dark:bg-slate-800">{{ workspace.pages.find((page) => page.id === pageId)?.name ?? pageId }}</button>
                </div>
              </aside>
              <div class="rounded-[22px] bg-white p-5 shadow-sm dark:bg-slate-900">
                <h3 class="text-[13px] font-semibold">页面流程</h3>
                <div class="mt-5 grid grid-cols-4 gap-4">
                  <div v-for="pageId in selectedScheme?.pageIds" :key="pageId" class="rounded-2xl border border-slate-200 p-4 text-center dark:border-slate-700"><Icon icon="solar:smartphone-bold-duotone" class="mx-auto size-8 text-sky-500" /><p class="mt-2 text-[13px] font-semibold">{{ workspace.pages.find((page) => page.id === pageId)?.name ?? pageId }}</p></div>
                </div>
                <div class="mt-5 grid gap-2 text-[12px] text-slate-500">
                  <p>全局上一页：{{ selectedScheme?.globalPrevious.trigger.displayName }} / {{ selectedScheme?.globalPrevious.animation }}</p>
                  <p>全局下一页：{{ selectedScheme?.globalNext.trigger.displayName }} / {{ selectedScheme?.globalNext.animation }}</p>
                  <p v-for="edge in selectedScheme?.edges" :key="`${edge.fromPageId}-${edge.toPageId}`">{{ edge.fromPageId }} -> {{ edge.toPageId }}：{{ edge.trigger.displayName }} / {{ edge.animation }}</p>
                </div>
              </div>
            </div>
          </section>

          <section v-else class="soft-card h-full overflow-auto p-5">
            <div class="mb-4 flex items-center justify-between"><h2 class="text-[16px] font-semibold">{{ viewTitle }}</h2><button class="rounded-full bg-sky-500 px-3 py-1.5 text-[12px] font-medium text-white" @click="startExport('操作完成')">执行操作</button></div>
            <div v-if="activeView === 'plugin'" class="grid grid-cols-2 gap-3">
              <div class="rounded-2xl bg-white px-4 py-3 text-[13px] shadow-sm dark:bg-slate-900"><p class="font-semibold">OBS Control</p><p class="mt-1 text-[12px] text-slate-500">后端插件 · JSON-RPC · 按需调用</p></div>
              <div class="rounded-2xl bg-white px-4 py-3 text-[13px] shadow-sm dark:bg-slate-900"><p class="font-semibold">系统助手</p><p class="mt-1 text-[12px] text-slate-500">后端插件 · 常驻需授权</p></div>
            </div>
            <div v-else class="grid gap-2">
              <div v-for="item in permissionRows" :key="item.id" class="rounded-2xl bg-white px-4 py-3 text-[13px] shadow-sm dark:bg-slate-900">
                <div class="flex items-center justify-between"><div><p class="font-semibold">{{ item.name }}</p><p class="mt-1 text-[12px] text-slate-500">{{ item.categoryName }} · {{ item.id }}</p></div><span :class="item.highRisk ? 'text-rose-500' : 'text-sky-600'">{{ item.highRisk ? '高危' : '普通' }}</span></div>
              </div>
            </div>
          </section>
        </div>

        <footer class="h-8 shrink-0">
          <div v-if="exporting" class="mt-3 h-1.5 overflow-hidden rounded-full bg-white dark:bg-slate-900"><div class="h-full rounded-full bg-sky-500 transition-all" :style="{ width: `${exportProgress}%` }"></div></div>
          <p v-else class="mt-3 text-[12px] text-slate-500">{{ workspace.loading ? "正在同步工作区..." : workspace.toast }}</p>
        </footer>
      </section>
    </section>

    <div v-if="showPermissionDialog" class="fixed inset-0 grid place-items-center bg-slate-950/28 p-6 backdrop-blur-sm">
      <div class="w-full max-w-[460px] rounded-3xl bg-white p-5 shadow-2xl dark:bg-slate-950">
        <div class="flex items-center justify-between"><h3 class="text-[16px] font-semibold">确认授权</h3><button class="grid size-8 place-items-center rounded-full bg-slate-100 dark:bg-slate-900" @click="showPermissionDialog = false"><Icon icon="solar:close-circle-bold-duotone" class="size-5" /></button></div>
        <div class="mt-4 grid gap-2">
          <label v-for="permission in selectedComponent?.requestedPermissions" :key="permission.capability" class="flex items-center gap-3 rounded-2xl bg-slate-50 px-3 py-2.5 text-[13px] dark:bg-slate-900">
            <input type="checkbox" checked class="size-4 accent-sky-500" /><span class="min-w-0 flex-1"><span class="block font-medium">{{ permission.capability }}</span><span class="mt-0.5 block text-[12px] text-slate-500">{{ permission.description }}</span></span><span v-if="permission.highRisk" class="rounded-full bg-rose-100 px-2 py-1 text-[11px] font-medium text-rose-600 dark:bg-rose-950 dark:text-rose-300">高危</span>
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
