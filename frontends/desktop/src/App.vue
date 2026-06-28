<script setup lang="ts">
import { Icon } from "@iconify/vue";
import { computed, onMounted, ref } from "vue";
import QRCode from "qrcode";
import type { ComponentDefinition, PackageExportResult, PackageImportResult, PackageInspection, PageDefinition, PluginManifest, SchemeDefinition, SectionRoute, ThemeMode, TrustedPairingCredential, ViewKey } from "./domain";
import { applyScheme, loadWorkspace, navItems, quickActions, quickStart, workspace } from "./workspace";
import { closeWindow, maximizeWindow, minimizeWindow, sendShell, setShellTheme, startWindowDrag } from "./nativeBridge";

const activeView = ref<ViewKey>("home");
const theme = ref<ThemeMode>("system");
const componentRoute = ref<SectionRoute>("manager");
const pageRoute = ref<SectionRoute>("manager");
const schemeRoute = ref<SectionRoute>("manager");
const componentEditorMode = ref<"visual" | "code">("visual");
const previewRatio = ref("1:1");
const showPermissionDialog = ref(false);
const showCodeSwitchDialog = ref(false);
const showDeviceMenu = ref(false);
const showDeviceDialog = ref(false);
const exporting = ref(false);
const exportProgress = ref(0);
const isMaximized = ref(false);
const pairing = ref<{ code: string; qrPayload: string; expiresInSeconds: number; host?: string; port?: number; localIps?: string[] } | null>(null);
const pairingQrDataUrl = ref("");
const deviceRemarkDraft = ref<Record<string, string>>({});
const componentCodeDraft = ref("");
const selectedCodeFile = ref("src/Component.vue");
const codeFileDrafts = ref<Record<string, string>>({});
const pendingImportKind = ref<"Component" | "Page" | "Scheme" | null>(null);
const pendingPluginImport = ref(false);
const pendingInspection = ref<PackageInspection | null>(null);
const grantedImportCapabilities = ref<string[]>([]);
const selectedDeviceId = ref("");
const selectedPluginId = ref("");
const pluginSettingsDraft = ref<Record<string, Record<string, unknown>>>({});
const selectedCellId = ref("");

const selectedComponent = computed(() => workspace.components.find((item) => item.id === workspace.selectedComponentId) ?? workspace.components[0]);
const selectedPage = computed(() => workspace.pages.find((item) => item.id === workspace.selectedPageId) ?? workspace.pages[0]);
const selectedScheme = computed(() => workspace.schemes.find((item) => item.id === workspace.selectedSchemeId) ?? workspace.schemes[0]);
const viewTitle = computed(() => navItems.find((item) => item.key === activeView.value)?.label ?? "首页");
const permissionRows = computed(() => workspace.capabilities.flatMap((category) => category.capabilities));
const permissionSourceKey = computed(() => selectedComponent.value ? `component:${selectedComponent.value.id}` : "component:unknown");
const selectedGrants = computed(() => workspace.permissionGrants.find((grant) => grant.sourceKey === permissionSourceKey.value)?.capabilities ?? []);
const trustedDevices = computed(() => workspace.deviceStatus?.trusted ?? []);
const currentDevice = computed(() => trustedDevices.value.find((device) => device.deviceId === selectedDeviceId.value) ?? trustedDevices.value[0] ?? null);
const currentDeviceName = computed(() => currentDevice.value ? (currentDevice.value.remark || currentDevice.value.displayName) : "等待移动设备连接");
const currentDeviceIcon = computed(() => currentDevice.value ? "solar:smartphone-bold-duotone" : "solar:devices-bold-duotone");
const localPairingHost = computed(() => pairing.value?.host ?? workspace.deviceStatus?.localIps?.[0] ?? "127.0.0.1");
const pagePreviewAspect = computed(() => currentDevice.value ? "aspect-[9/16]" : "aspect-[3/4]");
const importPermissionRows = computed(() => pendingInspection.value?.permissions ?? selectedComponent.value?.requestedPermissions ?? []);
const selectedPlugin = computed(() => workspace.plugins.find((plugin) => plugin.id === selectedPluginId.value) ?? workspace.plugins[0] ?? null);
const componentCodeFiles = computed(() => [
  { path: "src/Component.vue", icon: "solar:file-text-bold-duotone" },
  { path: "src/onedesk.actions.json", icon: "solar:bolt-bold-duotone" },
  { path: "onedesk.component.json", icon: "solar:document-bold-duotone" },
  { path: "onedesk.visual.json", icon: "solar:palette-bold-duotone" },
]);
const selectedCell = computed(() => selectedPage.value?.cells.find((cell) => cell.id === selectedCellId.value) ?? selectedPage.value?.cells[0] ?? null);

onMounted(async () => {
  setTheme(theme.value);
  await loadWorkspace();
  if (!selectedDeviceId.value && trustedDevices.value[0]) selectedDeviceId.value = trustedDevices.value[0].deviceId;
  if (!selectedPluginId.value && workspace.plugins[0]) selectedPluginId.value = workspace.plugins[0].id;
});

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

async function toggleMaximize() {
  isMaximized.value = await maximizeWindow();
}

function handleWindowDrag(event: PointerEvent) {
  if (event.button !== 0 || isMaximized.value) return;
  const target = event.target instanceof Element ? event.target : null;
  if (target?.closest("button,input,select,textarea,a,nav,.soft-card,.soft-row,.soft-start,.theme-dot,.window-controls,.no-drag,.device-menu,[data-no-window-drag]")) return;
  const scroller = target?.closest(".scrollable");
  if (scroller) {
    const rect = scroller.getBoundingClientRect();
    if (event.clientX >= rect.right - 16 || event.clientY >= rect.bottom - 16) return;
  }
  void startWindowDrag();
}

function chooseComponent(component: ComponentDefinition) {
  workspace.selectedComponentId = component.id;
  componentEditorMode.value = String(component.editMode).toLowerCase() === "code" ? "code" : "visual";
  componentCodeDraft.value = generatedComponentCode(component);
  hydrateCodeFiles(component);
  componentRoute.value = "editor";
}

function choosePage(page: PageDefinition) {
  workspace.selectedPageId = page.id;
  selectedCellId.value = page.cells[0]?.id ?? "";
  pageRoute.value = "editor";
}

function chooseScheme(scheme: SchemeDefinition) {
  workspace.selectedSchemeId = scheme.id;
  schemeRoute.value = "editor";
}

async function deleteComponent(component: ComponentDefinition) {
  const response = await sendShell("workspace.deleteComponent", { id: component.id });
  workspace.toast = response.ok ? "组件已删除" : response.message ?? "组件删除失败";
  await loadWorkspace();
}

async function deletePage(page: PageDefinition) {
  const response = await sendShell("workspace.deletePage", { id: page.id });
  workspace.toast = response.ok ? "页面已删除" : response.message ?? "页面删除失败";
  await loadWorkspace();
}

async function deleteScheme(scheme: SchemeDefinition) {
  const response = await sendShell("workspace.deleteScheme", { id: scheme.id });
  workspace.toast = response.ok ? "方案已删除" : response.message ?? "方案删除失败";
  await loadWorkspace();
}

function requestCodeMode() {
  if (componentEditorMode.value === "code") return;
  showCodeSwitchDialog.value = true;
}

async function createComponent() {
  const id = `component-${crypto.randomUUID()}`;
  const component: ComponentDefinition = {
    id,
    name: "新组件",
    version: "1.0.0",
    editMode: "visual",
    entryFile: "src/Component.vue",
    visualConfigFile: "onedesk.visual.json",
    actionIds: [],
    requestedPermissions: [],
    pluginDependencies: [],
  };
  const response = await sendShell<ComponentDefinition>("workspace.saveComponent", component);
  workspace.toast = response.ok ? "组件已创建" : response.message ?? "组件创建失败";
  await loadWorkspace();
  workspace.selectedComponentId = id;
  componentEditorMode.value = "visual";
  componentCodeDraft.value = generatedComponentCode(component);
  hydrateCodeFiles(component);
  componentRoute.value = "editor";
}

async function createPage() {
  const id = `page-${crypto.randomUUID()}`;
  const page: PageDefinition = {
    id,
    name: "新页面",
    rows: 4,
    columns: 3,
    spacing: { padding: 16, rowGap: 10, columnGap: 10 },
    backgroundKind: "solid",
    backgroundValue: "#0ea5e9",
    cells: Array.from({ length: 12 }, (_, index) => ({
      id: `${id}-cell-${index + 1}`,
      row: Math.floor(index / 3) + 1,
      column: (index % 3) + 1,
      rowSpan: 1,
      columnSpan: 1,
      componentId: null,
      style: { borderRadius: 16, outlineColor: "#e2e8f0", outlineWidth: 1, outlineStyle: "solid" },
    })),
  };
  const response = await sendShell<PageDefinition>("workspace.savePage", page);
  workspace.toast = response.ok ? "页面已创建" : response.message ?? "页面创建失败";
  await loadWorkspace();
  workspace.selectedPageId = id;
  selectedCellId.value = page.cells[0]?.id ?? "";
  pageRoute.value = "editor";
}

async function createScheme() {
  const id = `scheme-${crypto.randomUUID()}`;
  const firstPageId = workspace.pages[0]?.id ?? "";
  const trigger = { id: "three-finger-swipe-up", category: "touch.standard", displayName: "三指上滑", fingerCount: 3 };
  const scheme: SchemeDefinition = {
    id,
    name: "新方案",
    version: "1.0.0",
    pageIds: firstPageId ? [firstPageId] : [],
    globalPrevious: { trigger: { ...trigger, id: "three-finger-swipe-down", displayName: "三指下滑" }, animation: "fade" },
    globalNext: { trigger, animation: "fade" },
    edges: [],
    pluginDependencies: [],
  };
  const response = await sendShell<SchemeDefinition>("workspace.saveScheme", scheme);
  workspace.toast = response.ok ? "方案已创建" : response.message ?? "方案创建失败";
  await loadWorkspace();
  workspace.selectedSchemeId = id;
  schemeRoute.value = "editor";
}

function startProgress(label = "操作完成") {
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

async function exportComponent(component?: ComponentDefinition) {
  if (!component) return;
  await runExport("workspace.exportComponent", component.id, "组件导出完成");
}

async function exportPage(page?: PageDefinition) {
  if (!page) return;
  await runExport("workspace.exportPage", page.id, "页面导出完成");
}

async function exportScheme(scheme?: SchemeDefinition) {
  if (!scheme) return;
  await runExport("workspace.exportScheme", scheme.id, "方案导出完成");
}

async function runExport(type: string, id: string, label: string) {
  exporting.value = true;
  exportProgress.value = 30;
  const response = await sendShell<PackageExportResult>(type, { id });
  exportProgress.value = 100;
  window.setTimeout(() => {
    exporting.value = false;
    workspace.toast = response.ok && response.payload ? `${label}：${response.payload.packagePath}` : response.message ?? "导出失败";
  }, 260);
}

async function togglePermission(capability: string) {
  const granted = selectedGrants.value.includes(capability);
  const response = await sendShell("permission." + (granted ? "revoke" : "grant"), { sourceKey: permissionSourceKey.value, capability });
  workspace.toast = response.ok ? (granted ? "权限已撤销" : "权限已授权") : response.message ?? "权限操作失败";
  await loadWorkspace();
}

function toggleImportCapability(capability: string) {
  grantedImportCapabilities.value = grantedImportCapabilities.value.includes(capability)
    ? grantedImportCapabilities.value.filter((item) => item !== capability)
    : [...grantedImportCapabilities.value, capability];
}

async function saveComponent() {
  if (!selectedComponent.value) return;
  if (componentEditorMode.value === "code") {
    codeFileDrafts.value[selectedCodeFile.value] = componentCodeDraft.value;
    selectedComponent.value.editMode = "code";
    selectedComponent.value.visualConfigFile = null;
  }
  const response = await sendShell<ComponentDefinition>("workspace.saveComponent", selectedComponent.value);
  workspace.toast = response.ok ? "组件已保存" : response.message ?? "组件保存失败";
  await loadWorkspace();
}

function generatedComponentCode(component?: ComponentDefinition) {
  const name = component?.name ?? "新组件";
  return `<script setup lang="ts">\nconst title = '${name}'\n<\/script>\n\n<template>\n  <button class="onedesk-control-tile">{{ title }}</button>\n</template>\n\n<style scoped>\n.onedesk-control-tile {\n  width: 100%;\n  height: 100%;\n  border-radius: 16px;\n  overflow: hidden;\n  background: linear-gradient(135deg, #0ea5e9, #22d3ee);\n  color: white;\n  font-size: 14px;\n  font-weight: 700;\n}\n</style>`;
}

function hydrateCodeFiles(component?: ComponentDefinition) {
  selectedCodeFile.value = "src/Component.vue";
  codeFileDrafts.value = {
    "src/Component.vue": generatedComponentCode(component),
    "src/onedesk.actions.json": JSON.stringify(workspace.actions.filter((action) => component?.actionIds.includes(action.id)), null, 2),
    "onedesk.component.json": JSON.stringify(component ?? {}, null, 2),
    "onedesk.visual.json": JSON.stringify({
      background: { kind: "gradient", value: "sky-cyan" },
      text: { content: component?.name ?? "新组件", fontSize: 14, color: "#ffffff", position: "center" },
      image: { source: "", size: "cover", position: "center", margin: 0 },
      states: { locked: "opacity-60", pressed: "scale-95" },
    }, null, 2),
  };
  componentCodeDraft.value = codeFileDrafts.value[selectedCodeFile.value];
}

function selectCodeFile(path: string) {
  codeFileDrafts.value[selectedCodeFile.value] = componentCodeDraft.value;
  selectedCodeFile.value = path;
  componentCodeDraft.value = codeFileDrafts.value[path] ?? "";
}

async function savePage() {
  if (!selectedPage.value) return;
  const response = await sendShell<PageDefinition>("workspace.savePage", selectedPage.value);
  workspace.toast = response.ok ? "页面已保存" : response.message ?? "页面保存失败";
  await loadWorkspace();
}

async function saveScheme() {
  if (!selectedScheme.value) return;
  const response = await sendShell<SchemeDefinition>("workspace.saveScheme", selectedScheme.value);
  workspace.toast = response.ok ? "方案已保存" : response.message ?? "方案保存失败";
  await loadWorkspace();
}

function addPageToScheme(pageId: string) {
  if (!selectedScheme.value || !pageId || selectedScheme.value.pageIds.includes(pageId)) return;
  selectedScheme.value.pageIds = [...selectedScheme.value.pageIds, pageId];
}

function removePageFromScheme(pageId: string) {
  if (!selectedScheme.value) return;
  selectedScheme.value.pageIds = selectedScheme.value.pageIds.filter((id) => id !== pageId);
  selectedScheme.value.edges = selectedScheme.value.edges.filter((edge) => edge.fromPageId !== pageId && edge.toPageId !== pageId);
}

function moveSchemePage(pageId: string, direction: -1 | 1) {
  if (!selectedScheme.value) return;
  const pages = [...selectedScheme.value.pageIds];
  const index = pages.indexOf(pageId);
  const nextIndex = index + direction;
  if (index < 0 || nextIndex < 0 || nextIndex >= pages.length) return;
  [pages[index], pages[nextIndex]] = [pages[nextIndex], pages[index]];
  selectedScheme.value.pageIds = pages;
}

function handleAddSchemePage(event: Event) {
  const select = event.target instanceof HTMLSelectElement ? event.target : null;
  if (!select) return;
  addPageToScheme(select.value);
  select.value = "";
}

async function importWorkspace(kind: "Component" | "Page" | "Scheme") {
  pendingImportKind.value = kind;
  pendingPluginImport.value = false;
  const response = await sendShell<PackageInspection>("workspace.inspectImport", { kind });
  if (!response.ok || !response.payload) {
    workspace.toast = response.message ?? "已取消导入";
    pendingImportKind.value = null;
    return;
  }
  pendingInspection.value = response.payload;
  grantedImportCapabilities.value = response.payload.permissions.map((permission) => permission.capability);
  showPermissionDialog.value = true;
}

async function confirmPermissionDialog() {
  if (pendingPluginImport.value && pendingInspection.value) {
    const token = pendingInspection.value.token;
    pendingPluginImport.value = false;
    showPermissionDialog.value = false;
    const response = await sendShell("plugin.confirmImport", { token, grantedCapabilities: grantedImportCapabilities.value });
    pendingInspection.value = null;
    workspace.toast = response.ok ? "插件已导入并注册" : response.message ?? "插件导入失败";
    await loadWorkspace();
    return;
  }
  if (!pendingImportKind.value || !pendingInspection.value) {
    showPermissionDialog.value = false;
    return;
  }
  const kind = pendingImportKind.value;
  const token = pendingInspection.value.token;
  pendingImportKind.value = null;
  showPermissionDialog.value = false;
  const response = await sendShell<PackageImportResult>("workspace.confirmImport", { token, grantedCapabilities: grantedImportCapabilities.value });
  pendingInspection.value = null;
  workspace.toast = response.ok ? `${kind === "Component" ? "组件" : kind === "Page" ? "页面" : "方案"}导入完成，授权已保存` : response.message ?? "导入失败";
  await loadWorkspace();
}

async function renameDevice(device: TrustedPairingCredential) {
  const response = await sendShell<TrustedPairingCredential>("device.rename", { deviceId: device.deviceId, remark: deviceRemarkDraft.value[device.deviceId] ?? "" });
  workspace.toast = response.ok ? "设备备注已保存" : response.message ?? "设备备注保存失败";
  await loadWorkspace();
}

async function importPlugin() {
  pendingImportKind.value = null;
  pendingPluginImport.value = true;
  const response = await sendShell<PackageInspection>("plugin.inspectImport");
  if (!response.ok || !response.payload) {
    workspace.toast = response.message ?? "已取消插件导入";
    pendingPluginImport.value = false;
    return;
  }
  pendingInspection.value = response.payload;
  grantedImportCapabilities.value = response.payload.permissions.map((permission) => permission.capability);
  showPermissionDialog.value = true;
}

async function openDeviceDialog(generateCode = false) {
  showDeviceMenu.value = false;
  showDeviceDialog.value = true;
  if (!pairing.value || generateCode) {
    const response = await sendShell<{ code: string; qrPayload: string; expiresInSeconds: number; host?: string; port?: number; localIps?: string[] }>("pairing.generate", { port: 48320 });
    if (response.ok && response.payload) {
      pairing.value = response.payload;
      pairingQrDataUrl.value = await QRCode.toDataURL(response.payload.qrPayload, { margin: 1, width: 168, color: { dark: "#020617", light: "#ffffff" } });
    }
  }
}

function componentModeLabel(component: ComponentDefinition) {
  return String(component.editMode).toLowerCase() === "code" ? "代码" : "可视化";
}

function pageComponentCount(page: PageDefinition) {
  return page.cells.filter((cell) => Boolean(cell.componentId)).length;
}

function previewGradient(seed: string) {
  const presets = ["from-sky-400 to-cyan-300", "from-violet-400 to-sky-400", "from-emerald-400 to-sky-400", "from-amber-300 to-rose-400"];
  return presets[Math.abs(seed.split("").reduce((sum, char) => sum + char.charCodeAt(0), 0)) % presets.length];
}

function pluginSettingFields(plugin?: PluginManifest | null) {
  const properties = plugin?.settingsSchema?.properties;
  if (!properties || typeof properties !== "object" || Array.isArray(properties)) return [];
  return Object.entries(properties as Record<string, Record<string, unknown>>).map(([key, schema]) => ({
    key,
    title: String(schema.title ?? key),
    type: String(schema.type ?? "string"),
    description: String(schema.description ?? ""),
  }));
}

function pluginDraft(plugin: PluginManifest) {
  pluginSettingsDraft.value[plugin.id] ??= {};
  return pluginSettingsDraft.value[plugin.id];
}
</script>

<template>
  <main class="h-screen w-screen overflow-hidden text-slate-950 dark:text-slate-100" @pointerdown="handleWindowDrag">
    <section class="app-shell flex h-full min-h-[720px] min-w-[1120px] overflow-hidden bg-white/72 backdrop-blur-2xl dark:bg-black/72">
      <aside class="flex w-[96px] shrink-0 items-start justify-center py-9">
        <nav class="side-nav flex w-[54px] flex-col items-center gap-4 bg-white shadow-[0_16px_40px_rgba(15,23,42,0.08)] dark:bg-slate-950">
          <button v-for="item in navItems" :key="item.key" class="grid size-10 place-items-center rounded-full transition" :class="activeView === item.key ? 'bg-sky-500 text-white shadow-[0_10px_24px_rgba(14,165,233,0.35)]' : 'text-slate-500 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-slate-800'" :title="item.label" @click="openView(item.key)">
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
            <div class="device-menu relative">
              <button class="flex h-9 max-w-[220px] items-center gap-2 rounded-full bg-white px-3 text-[12px] font-medium text-slate-600 shadow-sm hover:text-sky-500 dark:bg-slate-950 dark:text-slate-300" title="设备" @click="showDeviceMenu = !showDeviceMenu">
                <Icon :icon="currentDeviceIcon" class="size-4 text-sky-500" />
                <span class="truncate">{{ currentDeviceName }}</span>
              </button>
              <div v-if="showDeviceMenu" class="absolute right-0 top-11 z-30 w-[260px] rounded-[22px] bg-white p-2 shadow-2xl shadow-slate-950/12 dark:bg-slate-950">
                <button class="menu-row" @click="openDeviceDialog(true)"><Icon icon="solar:qr-code-bold-duotone" class="size-5 text-sky-500" />显示连接码</button>
                <button class="menu-row" @click="openDeviceDialog(false)"><Icon icon="solar:devices-bold-duotone" class="size-5 text-sky-500" />设备管理与备注</button>
                <div class="my-1 h-px bg-slate-100 dark:bg-slate-800"></div>
                <p class="px-3 py-2 text-[11px] leading-5 text-slate-500">桌面端显示本机局域网 IP 和验证码，由手机端主动连接。</p>
              </div>
            </div>

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
                <div><h2 class="text-[16px] font-semibold">移动设备</h2><p class="mt-2 text-[12px] text-slate-500 dark:text-slate-400">已信任 {{ trustedDevices.length }} 个移动设备</p></div>
                <span class="flex items-center gap-1.5 text-[12px] text-slate-500 dark:text-slate-400"><i class="size-2 rounded-full" :class="currentDevice ? 'bg-green-500' : 'bg-slate-400'"></i>{{ currentDevice ? '可用' : '等待连接' }}</span>
              </div>
              <div class="flex items-center gap-5">
                <div class="grid size-[68px] shrink-0 grid-cols-3 gap-1 rounded-xl bg-slate-900 p-2 shadow-lg shadow-slate-950/12 dark:bg-slate-800"><span v-for="index in 9" :key="index" class="rounded-[3px] bg-slate-600"></span></div>
                <div class="min-w-0 flex-1">
                  <p class="truncate text-[14px] font-semibold">{{ currentDeviceName }}</p>
                  <p class="mt-2 text-[12px] text-slate-500 dark:text-slate-400">{{ currentDevice ? '移动端页面预览将使用该设备比例' : '手机端输入本机 IP 和验证码后会出现在这里' }}</p>
                  <p class="mt-1 flex items-center gap-1 text-[12px]" :class="currentDevice ? 'text-green-600' : 'text-sky-600'"><Icon icon="solar:verified-check-bold-duotone" class="size-4" />{{ currentDevice ? '长期信任已建立' : `本机 IP ${localPairingHost}` }}</p>
                </div>
              </div>
              <div class="mt-5 flex justify-end"><button class="rounded-full border border-sky-500/60 px-4 py-2 text-[12px] font-medium text-sky-600 hover:bg-sky-50 dark:hover:bg-sky-950/40" @click="openDeviceDialog(false)">设备管理</button></div>
            </div>

            <div class="soft-card p-5">
              <h2 class="text-[16px] font-semibold">快捷操作</h2>
              <div class="mt-4 grid gap-3">
                <button v-for="item in quickActions" :key="item.label" class="soft-row group" @click="item.label.includes('创建') ? createScheme() : item.label.includes('导入') ? importWorkspace('Scheme') : startProgress('动作编辑器已打开')">
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

          <section v-else-if="activeView === 'component' && componentRoute === 'manager'" class="scrollable h-full overflow-auto" data-no-window-drag>
            <div class="mb-4 flex items-center justify-between"><div><h2 class="text-[16px] font-semibold">组件管理</h2><p class="mt-1 text-[12px] text-slate-500">组件以卡片管理，预览使用上一次保存时的截图状态。</p></div><div class="flex gap-2"><button class="rounded-full bg-white px-4 py-2 text-[12px] font-medium text-sky-600 shadow-sm dark:bg-slate-900" @click="importWorkspace('Component')">导入组件</button><button class="rounded-full bg-sky-500 px-4 py-2 text-[12px] font-medium text-white" @click="createComponent">新建组件</button></div></div>
            <div class="grid grid-cols-3 gap-4">
              <article v-for="item in workspace.components" :key="item.id" class="manager-card">
                <div :class="['manager-preview bg-gradient-to-br', previewGradient(item.id)]"><Icon icon="solar:bolt-circle-bold-duotone" class="size-8 text-white" /><span class="mt-2 text-[12px] font-semibold text-white">{{ item.name }}</span></div>
                <div class="mt-3 flex items-start justify-between gap-3"><div class="min-w-0"><p class="truncate text-[14px] font-semibold">{{ item.name }}</p><p class="mt-1 text-[12px] text-slate-500">{{ componentModeLabel(item) }} · {{ item.actionIds.length }} 动作 · {{ item.version }}</p></div><span class="rounded-full bg-sky-50 px-2 py-1 text-[11px] text-sky-600 dark:bg-sky-950/50">{{ item.requestedPermissions.length }} 权限</span></div>
                <div class="mt-4 grid grid-cols-2 gap-2"><button class="card-action" @click="chooseComponent(item)"><Icon icon="solar:pen-bold-duotone" class="size-4" />修改</button><button class="card-action danger" @click="deleteComponent(item)"><Icon icon="solar:trash-bin-trash-bold-duotone" class="size-4" />删除</button></div>
              </article>
            </div>
          </section>

          <section v-else-if="activeView === 'component'" class="grid h-full grid-cols-[260px_1fr_260px] gap-4">
            <aside class="soft-card p-4">
              <button class="mb-4 flex items-center gap-2 text-[12px] text-sky-600" @click="componentRoute = 'manager'"><Icon icon="solar:alt-arrow-left-linear" class="size-4" />返回组件管理</button>
              <template v-if="componentEditorMode === 'visual'">
                <h2 class="text-[16px] font-semibold">{{ selectedComponent?.name }}</h2><p class="mt-1 text-[12px] text-slate-500">可视化组件 · {{ previewRatio }}</p>
                <div class="mt-5 grid gap-2"><button class="editor-nav-active">基础样式</button><button class="editor-nav">背景与媒体</button><button class="editor-nav">文字与图标</button><button class="editor-nav">锁定/按下状态</button><button class="editor-nav">动作系统</button><button class="editor-nav" @click="showPermissionDialog = true">权限声明</button></div>
              </template>
              <template v-else>
                <h2 class="text-[16px] font-semibold">组件文件</h2><p class="mt-1 text-[12px] text-slate-500">代码编辑后无法回到可视化。</p>
                <div class="mt-5 grid gap-1.5 text-[12px]"><button v-for="file in componentCodeFiles" :key="file.path" :class="selectedCodeFile === file.path ? 'file-row file-row-active' : 'file-row'" @click="selectCodeFile(file.path)"><Icon :icon="file.icon" class="size-4" />{{ file.path }}</button></div>
              </template>
            </aside>
            <section class="soft-card min-w-0 p-4">
              <div class="mb-4 flex items-center justify-between gap-3"><div class="flex rounded-full bg-white p-1 text-[12px] shadow-sm dark:bg-slate-900"><button class="rounded-full px-3 py-1.5" :class="componentEditorMode === 'visual' ? 'bg-sky-500 text-white' : ''" @click="componentEditorMode = 'visual'">可视化</button><button class="rounded-full px-3 py-1.5" :class="componentEditorMode === 'code' ? 'bg-sky-500 text-white' : ''" @click="requestCodeMode">代码</button></div><input v-if="selectedComponent" v-model="selectedComponent.name" class="field min-w-0 flex-1" placeholder="组件名称" /><div class="flex gap-2"><button class="rounded-full bg-white px-4 py-2 text-[12px] font-medium text-sky-600 shadow-sm dark:bg-slate-900" @click="exportComponent(selectedComponent)">导出组件</button><button class="rounded-full bg-sky-500 px-4 py-2 text-[12px] font-medium text-white" @click="saveComponent">保存</button></div></div>
              <div v-if="componentEditorMode === 'visual'" class="grid gap-3">
                <div class="rounded-[18px] bg-white p-4 shadow-sm dark:bg-slate-900"><h3 class="text-[13px] font-semibold">样式配置</h3><div class="mt-3 grid grid-cols-4 gap-2 text-[12px]"><select class="field"><option>渐变背景</option><option>纯色背景</option><option>图片背景</option><option>视频背景</option></select><input class="field" value="圆角 16" /><input class="field" value="边距 8" /><select class="field"><option>居中</option><option>靠左</option><option>靠右</option><option>靠下</option></select><input v-if="selectedComponent" v-model="selectedComponent.name" class="field" placeholder="显示文字" /><input class="field" value="字号 14" /><input class="field" value="#0ea5e9" /><select class="field"><option>按下缩小</option><option>按下高亮</option></select></div><p class="mt-3 text-[11px] text-slate-500">保存时会保留可视化配置文件标记，切换代码编辑后将不可回退。</p></div>
                <div class="rounded-[18px] bg-white p-4 shadow-sm dark:bg-slate-900"><div class="mb-3 flex items-center justify-between"><h3 class="text-[13px] font-semibold">动作配置</h3><button class="text-[12px] text-sky-600">添加动作</button></div><div class="grid gap-2 text-[12px]"><div v-for="actionId in selectedComponent?.actionIds" :key="actionId" class="grid grid-cols-[120px_1fr_80px] rounded-xl bg-slate-50 px-3 py-2 dark:bg-slate-800"><span>{{ workspace.actions.find((action) => action.id === actionId)?.trigger.displayName ?? '未设置' }}</span><span>{{ workspace.actions.find((action) => action.id === actionId)?.name ?? actionId }}</span><span class="text-right text-green-600">已授权</span></div></div></div>
              </div>
              <div v-else class="overflow-hidden rounded-[18px] bg-slate-950"><div class="flex h-9 items-center border-b border-slate-800 px-4 text-[12px] text-slate-400">{{ selectedCodeFile }}</div><textarea v-model="componentCodeDraft" class="scrollable h-[390px] w-full resize-none overflow-auto bg-slate-950 p-4 font-mono text-[12px] leading-6 text-sky-100 outline-none" data-no-window-drag spellcheck="false"></textarea></div>
            </section>
            <aside class="soft-card p-4"><div class="flex items-center justify-between"><h3 class="text-[13px] font-semibold">实时预览</h3><select v-model="previewRatio" class="field w-[82px]"><option>1:1</option><option>2:3</option><option>4:6</option></select></div><div class="mt-4 grid overflow-hidden rounded-[22px] bg-gradient-to-br from-sky-400 to-cyan-300 text-white shadow-lg shadow-sky-500/18" :class="previewRatio === '1:1' ? 'aspect-square' : previewRatio === '2:3' ? 'aspect-[2/3]' : 'aspect-[4/6]'"><div class="grid place-items-center text-center"><div><Icon icon="solar:bolt-circle-bold-duotone" class="mx-auto size-10" /><p class="mt-2 text-[13px] font-semibold">{{ selectedComponent?.name }}</p></div></div></div><div class="mt-4 grid gap-2 text-[12px] text-slate-500"><p>预览比例：{{ previewRatio }}</p><p>溢出策略：隐藏</p><p>权限：{{ selectedComponent?.requestedPermissions.length }} 项</p></div></aside>
          </section>

          <section v-else-if="activeView === 'page' && pageRoute === 'manager'" class="scrollable h-full overflow-auto" data-no-window-drag>
            <div class="mb-4 flex items-center justify-between"><div><h2 class="text-[16px] font-semibold">页面管理</h2><p class="mt-1 text-[12px] text-slate-500">页面以卡片管理，预览为上次保存的格子矩阵状态。</p></div><div class="flex gap-2"><button class="rounded-full bg-white px-4 py-2 text-[12px] font-medium text-sky-600 shadow-sm dark:bg-slate-900" @click="importWorkspace('Page')">导入页面</button><button class="rounded-full bg-sky-500 px-4 py-2 text-[12px] font-medium text-white" @click="createPage">新建页面</button></div></div>
            <div class="grid grid-cols-3 gap-4"><article v-for="page in workspace.pages" :key="page.id" class="manager-card"><div class="manager-preview bg-slate-100 dark:bg-slate-800"><div class="grid h-full w-full gap-1 rounded-2xl p-2" :style="{ gridTemplateColumns: `repeat(${page.columns}, minmax(0, 1fr))`, gridTemplateRows: `repeat(${page.rows}, minmax(0, 1fr))` }"><span v-for="cell in page.cells.slice(0, 12)" :key="cell.id" class="rounded-md bg-white dark:bg-slate-950" :class="cell.componentId ? 'ring-1 ring-sky-400' : ''"></span></div></div><p class="mt-3 text-[14px] font-semibold">{{ page.name }}</p><p class="mt-1 text-[12px] text-slate-500">{{ page.rows }} x {{ page.columns }} · {{ pageComponentCount(page) }} 组件 · {{ page.backgroundKind }}</p><div class="mt-4 grid grid-cols-2 gap-2"><button class="card-action" @click="choosePage(page)"><Icon icon="solar:pen-bold-duotone" class="size-4" />修改</button><button class="card-action danger" @click="deletePage(page)"><Icon icon="solar:trash-bin-trash-bold-duotone" class="size-4" />删除</button></div></article></div>
          </section>

          <section v-else-if="activeView === 'page'" class="soft-card h-full p-5">
            <div class="mb-4 flex items-center justify-between"><button class="flex items-center gap-2 text-[12px] text-sky-600" @click="pageRoute = 'manager'"><Icon icon="solar:alt-arrow-left-linear" class="size-4" />返回页面管理</button><div class="flex gap-2"><button class="rounded-full bg-white px-4 py-2 text-[12px] font-medium text-sky-600 shadow-sm dark:bg-slate-900" @click="exportPage(selectedPage)">导出页面</button><button class="rounded-full bg-sky-500 px-4 py-2 text-[12px] font-medium text-white" @click="savePage">保存页面</button></div></div>
            <div class="grid h-[calc(100%-32px)] grid-cols-[300px_1fr] gap-5"><aside class="scrollable overflow-auto rounded-[18px] bg-white p-4 shadow-sm dark:bg-slate-900" data-no-window-drag><input v-if="selectedPage" v-model="selectedPage.name" class="field w-full text-[15px] font-semibold" placeholder="页面名称" /><div class="mt-4 grid gap-2 text-[12px]"><input v-if="selectedPage" v-model.number="selectedPage.rows" type="number" min="1" max="12" class="field" placeholder="行数" /><input v-if="selectedPage" v-model.number="selectedPage.columns" type="number" min="1" max="12" class="field" placeholder="列数" /><input v-if="selectedPage" v-model.number="selectedPage.spacing.padding" type="number" class="field" placeholder="页边距" /><input v-if="selectedPage" v-model.number="selectedPage.spacing.rowGap" type="number" class="field" placeholder="行间距" /><input v-if="selectedPage" v-model.number="selectedPage.spacing.columnGap" type="number" class="field" placeholder="列间距" /><select v-if="selectedPage" v-model="selectedPage.backgroundKind" class="field"><option value="solid">纯色背景</option><option value="gradient">渐变背景</option><option value="image">图片背景</option><option value="video">视频背景</option></select><input v-if="selectedPage" v-model="selectedPage.backgroundValue" class="field" placeholder="背景值" /></div><div v-if="selectedCell" class="mt-5 grid gap-2 text-[12px]"><h3 class="text-[13px] font-semibold">当前格子</h3><input v-model.number="selectedCell.rowSpan" type="number" min="1" :max="selectedPage?.rows ?? 12" class="field" placeholder="跨行" /><input v-model.number="selectedCell.columnSpan" type="number" min="1" :max="selectedPage?.columns ?? 12" class="field" placeholder="跨列" /><select v-model="selectedCell.componentId" class="field"><option :value="null">不绑定组件</option><option v-for="component in workspace.components" :key="component.id" :value="component.id">{{ component.name }}</option></select><input v-model.number="selectedCell.style.borderRadius" type="number" class="field" placeholder="圆角" /><input v-model="selectedCell.style.outlineColor" class="field" placeholder="轮廓颜色" /><input v-model.number="selectedCell.style.outlineWidth" type="number" class="field" placeholder="轮廓宽度" /><select v-model="selectedCell.style.outlineStyle" class="field"><option value="solid">实线</option><option value="dashed">虚线</option><option value="dotted">点线</option></select></div><p class="mt-4 text-[11px] leading-5 text-slate-500">预览比例来自当前选择移动设备：{{ currentDeviceName }}</p></aside><div class="grid place-items-center rounded-[22px] bg-white p-6 shadow-sm dark:bg-slate-900"><div class="grid w-full max-w-[420px] overflow-hidden rounded-[24px] bg-slate-100 dark:bg-slate-800" :class="pagePreviewAspect" :style="{ padding: `${selectedPage?.spacing.padding ?? 12}px`, gap: `${selectedPage?.spacing.rowGap ?? 8}px ${selectedPage?.spacing.columnGap ?? 8}px`, gridTemplateColumns: `repeat(${selectedPage?.columns ?? 3}, minmax(0, 1fr))`, gridTemplateRows: `repeat(${selectedPage?.rows ?? 3}, minmax(0, 1fr))` }"><button v-for="cell in selectedPage?.cells" :key="cell.id" class="overflow-hidden bg-white text-[10px] text-slate-500 dark:bg-slate-900" :class="selectedCellId === cell.id ? 'ring-2 ring-sky-400' : ''" :style="{ gridColumn: `span ${cell.columnSpan} / span ${cell.columnSpan}`, gridRow: `span ${cell.rowSpan} / span ${cell.rowSpan}`, borderRadius: `${cell.style.borderRadius}px`, border: `${cell.style.outlineWidth}px ${cell.style.outlineStyle} ${cell.style.outlineColor}` }" @click="selectedCellId = cell.id"><span v-if="cell.componentId" class="grid h-full place-items-center">{{ workspace.components.find((item) => item.id === cell.componentId)?.name }}</span></button></div></div></div>
          </section>

          <section v-else-if="activeView === 'scheme' && schemeRoute === 'manager'" class="scrollable h-full overflow-auto" data-no-window-drag>
            <div class="mb-4 flex items-center justify-between"><div><h2 class="text-[16px] font-semibold">方案管理</h2><p class="mt-1 text-[12px] text-slate-500">方案是最终应用到移动端的唯一成品。</p></div><div class="flex gap-2"><button class="rounded-full bg-white px-4 py-2 text-[12px] font-medium text-sky-600 shadow-sm dark:bg-slate-900" @click="importWorkspace('Scheme')">导入方案</button><button class="rounded-full bg-sky-500 px-4 py-2 text-[12px] font-medium text-white" @click="createScheme">新建方案</button></div></div>
            <div class="grid grid-cols-3 gap-4"><article v-for="scheme in workspace.schemes" :key="scheme.id" class="manager-card"><div :class="['manager-preview bg-gradient-to-br', previewGradient(scheme.id)]"><Icon icon="solar:play-circle-bold-duotone" class="size-9 text-white" /><span class="mt-2 text-[12px] font-semibold text-white">{{ scheme.name }}</span></div><p class="mt-3 text-[14px] font-semibold">{{ scheme.name }}</p><p class="mt-1 text-[12px] text-slate-500">{{ scheme.pageIds.length }} 页面 · {{ scheme.pluginDependencies.length }} 插件依赖</p><p class="mt-2 text-[12px]" :class="workspace.activeSchemeId === scheme.id ? 'text-sky-600' : 'text-slate-500'">{{ workspace.activeSchemeId === scheme.id ? '已应用' : '未应用' }}</p><div class="mt-4 grid grid-cols-2 gap-2"><button class="card-action" @click="chooseScheme(scheme)"><Icon icon="solar:pen-bold-duotone" class="size-4" />修改</button><button class="card-action danger" @click="deleteScheme(scheme)"><Icon icon="solar:trash-bin-trash-bold-duotone" class="size-4" />删除</button></div></article></div>
          </section>

          <section v-else-if="activeView === 'scheme'" class="soft-card h-full p-5">
            <div class="mb-4 flex items-center justify-between"><button class="flex items-center gap-2 text-[12px] text-sky-600" @click="schemeRoute = 'manager'"><Icon icon="solar:alt-arrow-left-linear" class="size-4" />返回方案管理</button><div class="flex gap-2"><button class="rounded-full bg-white px-4 py-2 text-[12px] font-medium text-sky-600 shadow-sm dark:bg-slate-900" @click="exportScheme(selectedScheme)">导出方案</button><button class="rounded-full bg-white px-4 py-2 text-[12px] font-medium text-sky-600 shadow-sm dark:bg-slate-900" @click="saveScheme">保存方案</button><button class="rounded-full bg-sky-500 px-4 py-2 text-[12px] font-medium text-white" @click="selectedScheme && applyScheme(selectedScheme.id)">应用方案</button></div></div>
            <div class="grid h-[calc(100%-42px)] grid-cols-[280px_1fr] gap-5"><aside class="scrollable overflow-auto rounded-[18px] bg-white p-4 shadow-sm dark:bg-slate-900" data-no-window-drag><input v-if="selectedScheme" v-model="selectedScheme.name" class="field w-full text-[15px] font-semibold" placeholder="方案名称" /><select class="field mt-4 w-full text-[12px]" @change="handleAddSchemePage"><option value="">添加页面到方案</option><option v-for="page in workspace.pages.filter((page) => !selectedScheme?.pageIds.includes(page.id))" :key="page.id" :value="page.id">{{ page.name }}</option></select><div class="mt-4 grid gap-2 text-[12px]"><div v-for="pageId in selectedScheme?.pageIds" :key="pageId" class="rounded-xl bg-slate-50 px-3 py-2 dark:bg-slate-800"><div class="font-semibold">{{ workspace.pages.find((page) => page.id === pageId)?.name ?? pageId }}</div><div class="mt-2 flex gap-1"><button class="card-action h-7 flex-1" @click="moveSchemePage(pageId, -1)">上移</button><button class="card-action h-7 flex-1" @click="moveSchemePage(pageId, 1)">下移</button><button class="card-action danger h-7 flex-1" @click="removePageFromScheme(pageId)">删除</button></div></div></div><div class="mt-4 grid gap-2 text-[12px]"><h3 class="text-[13px] font-semibold">全局切换</h3><select v-if="selectedScheme" v-model="selectedScheme.globalPrevious.animation" class="field"><option value="fade">渐入渐退</option><option value="slide">滑动</option><option value="none">无动画</option></select><select v-if="selectedScheme" v-model="selectedScheme.globalNext.animation" class="field"><option value="fade">渐入渐退</option><option value="slide">滑动</option><option value="none">无动画</option></select></div></aside><div class="rounded-[22px] bg-white p-5 shadow-sm dark:bg-slate-900"><h3 class="text-[13px] font-semibold">页面流程</h3><div class="mt-5 flex items-center gap-3 overflow-auto pb-2" data-no-window-drag><template v-for="(pageId, index) in selectedScheme?.pageIds" :key="pageId"><div class="min-w-[132px] rounded-2xl border border-slate-200 p-4 text-center dark:border-slate-700"><Icon icon="solar:smartphone-bold-duotone" class="mx-auto size-8 text-sky-500" /><p class="mt-2 text-[13px] font-semibold">{{ workspace.pages.find((page) => page.id === pageId)?.name ?? pageId }}</p></div><div v-if="index < (selectedScheme?.pageIds.length ?? 0) - 1" class="min-w-[96px] text-center text-[11px] text-slate-500"><Icon icon="solar:alt-arrow-right-linear" class="mx-auto size-5" /><p>{{ selectedScheme?.globalNext.trigger.displayName }}</p><p>{{ selectedScheme?.globalNext.animation }}</p></div></template></div><div class="mt-5 grid gap-2 text-[12px] text-slate-500"><p>全局上一页：{{ selectedScheme?.globalPrevious.trigger.displayName }} / {{ selectedScheme?.globalPrevious.animation }}</p><p>全局下一页：{{ selectedScheme?.globalNext.trigger.displayName }} / {{ selectedScheme?.globalNext.animation }}</p><p v-for="edge in selectedScheme?.edges" :key="`${edge.fromPageId}-${edge.toPageId}`">{{ edge.fromPageId }} -> {{ edge.toPageId }}：{{ edge.trigger.displayName }} / {{ edge.animation }}</p></div></div></div>
          </section>

          <section v-else class="soft-card scrollable h-full overflow-auto p-5" data-no-window-drag>
            <div class="mb-4 flex items-center justify-between"><h2 class="text-[16px] font-semibold">{{ viewTitle }}</h2><button class="rounded-full bg-sky-500 px-3 py-1.5 text-[12px] font-medium text-white" @click="activeView === 'plugin' ? importPlugin() : startProgress('操作完成')">{{ activeView === 'plugin' ? '导入插件' : '执行操作' }}</button></div>
            <div v-if="activeView === 'plugin'" class="grid h-[calc(100%-44px)] grid-cols-[320px_1fr] gap-4">
              <div class="scrollable grid content-start gap-3 overflow-auto pr-1" data-no-window-drag>
                <button v-for="plugin in workspace.plugins" :key="plugin.id" class="rounded-2xl bg-white px-4 py-3 text-left text-[13px] shadow-sm dark:bg-slate-900" :class="selectedPlugin?.id === plugin.id ? 'ring-2 ring-sky-400' : ''" @click="selectedPluginId = plugin.id">
                  <div class="flex items-start justify-between gap-3"><div class="min-w-0"><p class="truncate font-semibold">{{ plugin.name }}</p><p class="mt-1 text-[12px] text-slate-500">{{ plugin.id }} · {{ plugin.version }}</p></div><span class="rounded-full bg-sky-50 px-2 py-1 text-[11px] text-sky-600 dark:bg-sky-950">已注册</span></div>
                  <p class="mt-2 text-[12px] text-slate-500">{{ plugin.persistent ? '允许常驻后台' : '按需调用' }} · {{ plugin.permissions.length }} 权限</p>
                </button>
                <div v-if="!workspace.plugins.length" class="rounded-2xl bg-white px-4 py-8 text-center text-[13px] text-slate-500 shadow-sm dark:bg-slate-900">暂无插件。导入插件包后，OneDesk 会读取插件清单、显示权限并注册后端进程。</div>
              </div>
              <div class="rounded-2xl bg-white p-4 shadow-sm dark:bg-slate-900">
                <template v-if="selectedPlugin">
                  <h3 class="text-[15px] font-semibold">{{ selectedPlugin.name }}</h3>
                  <p class="mt-1 text-[12px] text-slate-500">{{ selectedPlugin.id }} · {{ selectedPlugin.version }}</p>
                  <div class="mt-4 grid gap-2">
                    <h4 class="text-[13px] font-semibold">设置表单</h4>
                    <div v-if="pluginSettingFields(selectedPlugin).length" class="grid gap-2">
                      <label v-for="field in pluginSettingFields(selectedPlugin)" :key="field.key" class="grid gap-1 text-[12px] font-medium">
                        {{ field.title }}
                        <input v-if="field.type === 'string'" v-model="pluginDraft(selectedPlugin)[field.key]" class="field" />
                        <input v-else-if="field.type === 'number' || field.type === 'integer'" v-model.number="pluginDraft(selectedPlugin)[field.key]" type="number" class="field" />
                        <label v-else-if="field.type === 'boolean'" class="flex items-center gap-2 rounded-xl bg-slate-50 px-3 py-2 dark:bg-slate-950"><input v-model="pluginDraft(selectedPlugin)[field.key]" type="checkbox" class="accent-sky-500" />启用</label>
                        <input v-else v-model="pluginDraft(selectedPlugin)[field.key]" class="field" />
                        <span v-if="field.description" class="text-[11px] font-normal text-slate-500">{{ field.description }}</span>
                      </label>
                    </div>
                    <p v-else class="rounded-2xl bg-slate-50 px-3 py-3 text-[12px] text-slate-500 dark:bg-slate-950">该插件没有提交设置 schema。</p>
                  </div>
                  <div class="mt-4 grid gap-2">
                    <h4 class="text-[13px] font-semibold">权限</h4>
                    <div v-for="permission in selectedPlugin.permissions" :key="permission.capability" class="rounded-2xl bg-slate-50 px-3 py-2 text-[12px] dark:bg-slate-950"><span class="font-semibold">{{ permission.capability }}</span><span v-if="permission.highRisk" class="ml-2 text-rose-500">高危</span><p class="mt-1 text-slate-500">{{ permission.description }}</p></div>
                  </div>
                </template>
                <p v-else class="text-[13px] text-slate-500">选择一个插件后查看设置和权限。</p>
              </div>
            </div>
            <div v-else class="grid gap-2"><div class="rounded-2xl bg-white px-4 py-3 text-[13px] shadow-sm dark:bg-slate-900"><p class="font-semibold">授权对象：{{ selectedComponent?.name ?? '未选择组件' }}</p><p class="mt-1 text-[12px] text-slate-500">{{ permissionSourceKey }} · 大类授权会覆盖全部小类，小类授权只开放单项能力。</p></div><div v-for="item in permissionRows" :key="item.id" class="rounded-2xl bg-white px-4 py-3 text-[13px] shadow-sm dark:bg-slate-900"><div class="flex items-center justify-between gap-4"><div class="min-w-0"><p class="truncate font-semibold">{{ item.name }}</p><p class="mt-1 truncate text-[12px] text-slate-500">{{ item.categoryName }} · {{ item.id }}</p></div><div class="flex items-center gap-2"><span :class="item.highRisk ? 'text-rose-500' : 'text-sky-600'">{{ item.highRisk ? '高危' : '普通' }}</span><button class="rounded-full px-3 py-1.5 text-[12px] font-medium" :class="selectedGrants.includes(item.id) ? 'bg-sky-500 text-white' : 'bg-slate-100 text-slate-600 dark:bg-slate-800 dark:text-slate-300'" @click="togglePermission(item.id)">{{ selectedGrants.includes(item.id) ? '已授权' : '授权' }}</button></div></div></div></div>
          </section>
        </div>

        <footer class="h-8 shrink-0"><div v-if="exporting" class="mt-3 h-1.5 overflow-hidden rounded-full bg-white dark:bg-slate-900"><div class="h-full rounded-full bg-sky-500 transition-all" :style="{ width: `${exportProgress}%` }"></div></div><p v-else class="mt-3 text-[12px] text-slate-500">{{ workspace.loading ? "正在同步工作区..." : workspace.toast }}</p></footer>
      </section>
    </section>

    <div v-if="showDeviceDialog" class="fixed inset-0 z-40 grid place-items-center bg-slate-950/30 p-6 backdrop-blur-sm">
      <div class="w-full max-w-[760px] rounded-[30px] bg-white p-5 shadow-2xl dark:bg-slate-950">
        <div class="flex items-center justify-between"><div><h3 class="text-[17px] font-semibold">设备管理</h3><p class="mt-1 text-[12px] text-slate-500">手机端输入本机局域网 IP、端口和验证码后连接桌面端。</p></div><button class="grid size-8 place-items-center rounded-full bg-slate-100 dark:bg-slate-900" @click="showDeviceDialog = false"><Icon icon="solar:close-circle-bold-duotone" class="size-5" /></button></div>
        <div class="mt-5 grid grid-cols-[1fr_300px] gap-4">
          <section class="rounded-[22px] bg-slate-50 p-4 dark:bg-slate-900"><h4 class="text-[13px] font-semibold">移动设备</h4><div class="mt-3 grid gap-2"><div v-for="device in trustedDevices" :key="device.deviceId" class="rounded-2xl bg-white p-3 text-[12px] dark:bg-slate-950"><div class="flex items-center justify-between"><span class="font-semibold">{{ device.remark || device.displayName }}</span><span :class="currentDevice?.deviceId === device.deviceId ? 'text-sky-600' : 'text-green-600'">{{ currentDevice?.deviceId === device.deviceId ? '当前预览' : '已信任' }}</span></div><p class="mt-1 truncate text-slate-500">{{ device.deviceId }} · {{ new Date(device.createdAt).toLocaleString() }}</p><div class="mt-3 flex gap-2"><input v-model="deviceRemarkDraft[device.deviceId]" class="field min-w-0 flex-1" :placeholder="device.remark || '设备备注'" /><button class="rounded-xl bg-slate-100 px-3 text-sky-600 dark:bg-slate-800" @click="selectedDeviceId = device.deviceId">设为当前</button><button class="rounded-xl bg-sky-500 px-3 text-white" @click="renameDevice(device)">保存</button></div></div><div v-if="!trustedDevices.length" class="rounded-2xl bg-white p-4 text-[12px] leading-6 text-slate-500 dark:bg-slate-950">暂无移动设备。请在手机端打开 OneDesk，输入右侧本机 IP、端口和验证码建立首次信任。</div></div><h4 class="mt-4 text-[13px] font-semibold">在线连接</h4><div class="mt-3 grid gap-2"><div v-for="peer in workspace.gatewayStatus?.peers" :key="peer.deviceId" class="rounded-2xl bg-white p-3 text-[12px] dark:bg-slate-950"><div class="flex items-center justify-between"><span class="font-semibold">{{ peer.deviceId }}</span><span :class="peer.online ? 'text-green-600' : 'text-slate-400'">{{ peer.online ? '在线' : '离线' }}</span></div><p class="mt-1 text-slate-500">{{ peer.endpoint }}</p></div><div v-if="!workspace.gatewayStatus?.peers.length" class="rounded-2xl bg-white p-3 text-[12px] text-slate-500 dark:bg-slate-950">暂无在线移动端</div></div></section>
          <section class="rounded-[22px] bg-slate-50 p-4 dark:bg-slate-900"><h4 class="text-[13px] font-semibold">本机连接信息</h4><div class="mt-3 grid gap-2 text-[12px]"><div class="rounded-2xl bg-white p-3 dark:bg-slate-950"><p class="text-slate-500">局域网 IP</p><p class="mt-1 text-[18px] font-semibold text-sky-500">{{ localPairingHost }}</p><p class="mt-2 text-[11px] text-slate-500">可用 IP：{{ pairing?.localIps?.join(' / ') || workspace.deviceStatus?.localIps?.join(' / ') || '未检测到' }}</p></div><div class="rounded-2xl bg-white p-3 dark:bg-slate-950"><p class="text-slate-500">端口</p><p class="mt-1 text-[18px] font-semibold">{{ pairing?.port ?? workspace.gatewayStatus?.port ?? 48320 }}</p></div><div class="rounded-2xl bg-white p-4 text-center dark:bg-slate-950"><p class="text-[28px] font-semibold tracking-[0.3em] text-sky-500">{{ pairing?.code ?? "------" }}</p><p class="mt-1 text-[11px] text-slate-500">验证码 5 分钟内有效，只用于首次换取长期信任凭据</p></div><div class="rounded-2xl bg-white p-3 text-center dark:bg-slate-950"><p class="mb-2 text-left text-[12px] font-semibold">扫码连接</p><img v-if="pairingQrDataUrl" :src="pairingQrDataUrl" alt="OneDesk 配对二维码" class="mx-auto size-[168px] rounded-2xl bg-white p-2" /><p v-else class="rounded-2xl bg-slate-50 p-6 text-[12px] text-slate-500 dark:bg-slate-900">点击生成验证码后显示二维码</p><p class="mt-2 break-all text-left text-[11px] leading-5 text-slate-500">{{ pairing?.qrPayload ?? "" }}</p></div><button class="rounded-2xl bg-sky-500 py-2.5 text-[13px] font-medium text-white" @click="openDeviceDialog(true)">生成验证码</button></div></section>
        </div>
      </div>
    </div>

    <div v-if="showPermissionDialog" class="fixed inset-0 z-40 grid place-items-center bg-slate-950/28 p-6 backdrop-blur-sm">
      <div class="w-full max-w-[520px] rounded-3xl bg-white p-5 shadow-2xl dark:bg-slate-950"><div class="flex items-center justify-between"><div><h3 class="text-[16px] font-semibold">确认授权</h3><p v-if="pendingInspection" class="mt-1 truncate text-[12px] text-slate-500">{{ pendingInspection.name }} · {{ pendingInspection.kind }}</p></div><button class="grid size-8 place-items-center rounded-full bg-slate-100 dark:bg-slate-900" @click="pendingImportKind = null; pendingPluginImport = false; pendingInspection = null; showPermissionDialog = false"><Icon icon="solar:close-circle-bold-duotone" class="size-5" /></button></div><p class="mt-3 text-[12px] leading-5 text-slate-500">导入或安装前会按照能力目录授权，默认同意；高危权限会明确标记，后续可在设置里修改。</p><div v-if="pendingInspection?.pluginDependencies.length" class="mt-3 rounded-2xl bg-amber-50 px-3 py-2 text-[12px] text-amber-700 dark:bg-amber-950/40 dark:text-amber-200">依赖插件：{{ pendingInspection.pluginDependencies.map((item) => `${item.id}@${item.version}`).join(' / ') }}</div><div class="mt-4 grid max-h-[300px] gap-2 overflow-auto pr-1" data-no-window-drag><label v-for="permission in importPermissionRows" :key="permission.capability" class="flex items-center gap-3 rounded-2xl bg-slate-50 px-3 py-2.5 text-[13px] dark:bg-slate-900"><input type="checkbox" :checked="grantedImportCapabilities.includes(permission.capability)" class="size-4 accent-sky-500" @change="toggleImportCapability(permission.capability)" /><span class="min-w-0 flex-1"><span class="block font-medium">{{ permission.capability }}</span><span class="mt-0.5 block text-[12px] text-slate-500">{{ permission.description }}</span></span><span v-if="permission.highRisk" class="rounded-full bg-rose-100 px-2 py-1 text-[11px] font-medium text-rose-600 dark:bg-rose-950 dark:text-rose-300">高危</span></label><div v-if="!importPermissionRows.length" class="rounded-2xl bg-slate-50 px-3 py-2.5 text-[13px] text-slate-500 dark:bg-slate-900">当前对象没有声明额外权限。</div></div><button class="mt-4 w-full rounded-2xl bg-sky-500 py-2.5 text-[13px] font-medium text-white" @click="confirmPermissionDialog">{{ pendingImportKind || pendingPluginImport ? '授权并导入' : '确认授权' }}</button></div>
    </div>

    <div v-if="showCodeSwitchDialog" class="fixed inset-0 z-40 grid place-items-center bg-slate-950/28 p-6 backdrop-blur-sm">
      <div class="w-full max-w-[420px] rounded-3xl bg-white p-5 shadow-2xl dark:bg-slate-950"><Icon icon="solar:danger-triangle-bold-duotone" class="size-9 text-amber-500" /><h3 class="mt-3 text-[16px] font-semibold">切换到代码编辑？</h3><p class="mt-2 text-[13px] leading-6 text-slate-500">切换后无法回到可视化编辑，因为任意 Vue 代码无法完整还原为可视化配置。</p><div class="mt-4 flex gap-2"><button class="flex-1 rounded-2xl bg-slate-100 py-2.5 text-[13px] font-medium dark:bg-slate-900" @click="showCodeSwitchDialog = false">取消</button><button class="flex-1 rounded-2xl bg-sky-500 py-2.5 text-[13px] font-medium text-white" @click="componentCodeDraft = generatedComponentCode(selectedComponent); componentEditorMode = 'code'; showCodeSwitchDialog = false">继续</button></div></div>
    </div>
  </main>
</template>
