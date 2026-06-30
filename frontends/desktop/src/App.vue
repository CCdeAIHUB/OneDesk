<script setup lang="ts">
import { Icon } from "@iconify/vue";
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from "vue";
import QRCode from "qrcode";
import CodeMirrorEditor from "./components/CodeMirrorEditor.vue";
import type { ActionDefinition, ComponentDefinition, MediaResourceCopyResult, MediaResourceDefinition, PackageExportResult, PackageImportResult, PackageInspection, PageDefinition, PluginManifest, SchemeDefinition, SectionRoute, ThemeMode, TriggerDefinition, TrustedPairingCredential, ViewKey } from "./domain";
import { applyScheme, loadWorkspace, navItems, quickActions, quickStart, refreshDeviceConnectivity, workspace } from "./workspace";
import { closeWindow, maximizeWindow, minimizeWindow, moveWindowBy, sendShell, setShellTheme, startWindowResize } from "./nativeBridge";
import {
  applyDpiScaling,
  componentPreviewStyle as buildComponentPreviewStyle,
  componentTileStyle,
  defaultVisualConfig,
  generatedComponentCode,
  gradientPresets,
  pageBackgroundStyle,
  pageGridStyle as buildPageGridStyle,
  parseVisualConfig,
  textPositionStyle,
  visualVideoSource,
  type VisualConfig,
  type VisualTextLayer,
} from "./editorVisualConfig";

const activeView = ref<ViewKey>("home");
const theme = ref<ThemeMode>("system");
const settingsSection = ref<"general" | "connection" | "permission" | "logs" | "plugins" | "resources">("general");
const componentRoute = ref<SectionRoute>("manager");
const pageRoute = ref<SectionRoute>("manager");
const schemeRoute = ref<SectionRoute>("manager");
const componentEditorMode = ref<"visual" | "code">("visual");
const componentVisualSection = ref<"base" | "background" | "text" | "state" | "action" | "permission">("base");
const componentVisualScrollHost = ref<HTMLElement | null>(null);
const previewRatio = ref("1:1");
const showPermissionDialog = ref(false);
const showCodeSwitchDialog = ref(false);
const showActionDesignerDialog = ref(false);
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
const actionDraftParametersText = ref<Record<number, string>>({});
const pendingImportKind = ref<"Component" | "Page" | "Scheme" | null>(null);
const pendingPluginImport = ref(false);
const pendingInspection = ref<PackageInspection | null>(null);
const grantedImportCapabilities = ref<string[]>([]);
const selectedDeviceId = ref("");
const selectedPluginId = ref("");
const pluginSettingsDraft = ref<Record<string, Record<string, unknown>>>({});
const actionDraft = ref<ActionDefinition | null>(null);
const pendingEditorLeave = ref<null | { title: string; proceed: () => void | Promise<void> }>(null);
const editorSavedSnapshot = ref("");
const selectedCellId = ref("");
const pageLivePreview = ref(false);
const showResourcePicker = ref(false);
const resourcePickerTarget = ref<"page-background" | "component-background" | null>(null);
const componentVisualCache = ref<Record<string, VisualConfig>>({});
const pendingDelete = ref<{ kind: "component" | "page" | "scheme" | "plugin" | "action"; id: string; name: string } | null>(null);
const componentPreviewEl = ref<HTMLElement | null>(null);
const draggingTextLayerId = ref<string | null>(null);
const resizingTextLayerId = ref<string | null>(null);
const enableStartup = ref(false);
const connectionPort = ref(48320);
const toasts = ref<Array<{ id: number; message: string }>>([]);
const draggingSchemePageIndex = ref<number | null>(null);
const permissionSourceKind = ref<"component" | "plugin">("component");
const permissionSourceId = ref("");
const visualConfig = ref<VisualConfig>(defaultVisualConfig());
const loadingComponentId = ref("");
const componentVideoPreviewState = ref<"idle" | "loading" | "ready" | "error">("idle");
const componentVideoPreviewKey = ref(0);
let toastSequence = 0;
let componentLoadSequence = 0;
let windowMovePointerId = -1;
let windowMoveLastScreenX = 0;
let windowMoveLastScreenY = 0;
let pendingWindowMoveX = 0;
let pendingWindowMoveY = 0;
let pendingWindowMoveFrame = 0;
let deviceRefreshTimer = 0;

const triggerCatalog: Array<{ category: string; label: string; triggers: Array<{ id: string; displayName: string; fingerCount?: number }> }> = [
  {
    category: "touch.standard",
    label: "标准触摸",
    triggers: [
      { id: "tap", displayName: "单击" },
      { id: "double-tap", displayName: "双击" },
      { id: "long-press", displayName: "长按" },
      { id: "press-and-hold", displayName: "按住" },
      { id: "swipe-up", displayName: "上滑", fingerCount: 1 },
      { id: "swipe-down", displayName: "下滑", fingerCount: 1 },
      { id: "swipe-left", displayName: "左滑", fingerCount: 1 },
      { id: "swipe-right", displayName: "右滑", fingerCount: 1 },
      { id: "horizontal-swipe", displayName: "横向滑动", fingerCount: 1 },
      { id: "vertical-swipe", displayName: "纵向滑动", fingerCount: 1 },
      { id: "pinch-in", displayName: "捏合" },
      { id: "pinch-out", displayName: "张开" },
      { id: "rotate", displayName: "旋转" },
    ],
  },
  {
    category: "touch.multi",
    label: "多指触摸",
    triggers: [2, 3, 4, 5].flatMap((finger) =>
      ["up", "down", "left", "right"].map((dir) => ({
        id: `${finger}-finger-swipe-${dir}`,
        displayName: `${finger}指${dir === "up" ? "上" : dir === "down" ? "下" : dir === "left" ? "左" : "右"}滑`,
        fingerCount: finger,
      })),
    ),
  },
  {
    category: "sensor",
    label: "设备传感器",
    triggers: [
      { id: "shake", displayName: "摇晃" },
      { id: "orientation-change", displayName: "方向变化" },
      { id: "tilt-up", displayName: "向上倾斜" },
      { id: "tilt-down", displayName: "向下倾斜" },
      { id: "tilt-left", displayName: "向左倾斜" },
      { id: "tilt-right", displayName: "向右倾斜" },
    ],
  },
];

const triggerOptions = computed(() => triggerCatalog.flatMap((group) => group.triggers.map((trigger) => ({ ...trigger, category: group.category }))));

function findTrigger(id: string) {
  return triggerOptions.value.find((trigger) => trigger.id === id) ?? { id, displayName: id, category: "touch.standard" };
}

function buildTriggerDefinition(trigger: { id: string; category: string; displayName: string; fingerCount?: number; platformLimited?: boolean }): TriggerDefinition {
  return {
    id: trigger.id,
    category: trigger.category,
    displayName: trigger.displayName,
    fingerCount: trigger.fingerCount ?? (trigger.category === "sensor" ? 0 : 1),
    platformLimited: trigger.platformLimited,
  };
}

function triggerLabel(trigger: { id: string; displayName: string }) {
  return trigger.displayName;
}

const selectedComponent = computed(() => workspace.components.find((item) => item.id === workspace.selectedComponentId) ?? workspace.components[0]);
const selectedPage = computed(() => workspace.pages.find((item) => item.id === workspace.selectedPageId) ?? workspace.pages[0]);
const selectedScheme = computed(() => workspace.schemes.find((item) => item.id === workspace.selectedSchemeId) ?? workspace.schemes[0]);
const viewTitle = computed(() => navItems.find((item) => item.key === activeView.value)?.label ?? "首页");
const permissionRows = computed(() => workspace.capabilities.flatMap((category) => category.capabilities));
const permissionSourceKey = computed(() => permissionSourceKind.value === "plugin" ? `plugin:${permissionSourceId.value || "unknown"}` : `component:${permissionSourceId.value || selectedComponent.value?.id || "unknown"}`);
const permissionSourceOptions = computed(() => [
  ...workspace.components.map((component) => ({ kind: "component" as const, id: component.id, label: `组件 · ${component.name}` })),
  ...workspace.plugins.map((plugin) => ({ kind: "plugin" as const, id: plugin.id, label: `插件 · ${plugin.name}` })),
]);
const permissionSourceLabel = computed(() => permissionSourceOptions.value.find((option) => option.kind === permissionSourceKind.value && option.id === permissionSourceId.value)?.label ?? "未选择授权对象");
const selectedGrants = computed(() => workspace.permissionGrants.find((grant) => grant.sourceKey === permissionSourceKey.value)?.capabilities ?? []);
const trustedDevices = computed(() => workspace.deviceStatus?.trusted ?? []);
const actionDraftInvocation = computed(() => actionDraft.value?.invocations[0] ?? null);
const actionDraftTriggerLabel = computed(() => actionDraft.value ? triggerLabel(actionDraft.value.trigger) : "\u672a\u9009\u62e9\u89e6\u53d1");
const actionDraftCapabilityLabel = computed(() => actionDraft.value?.invocations.length ? `${actionDraft.value.invocations.length} JSAPI` : "\u672a\u9009\u62e9 JSAPI");
const currentDevice = computed(() => trustedDevices.value.find((device) => device.deviceId === selectedDeviceId.value) ?? trustedDevices.value[0] ?? null);
const currentDeviceName = computed(() => currentDevice.value ? (currentDevice.value.remark || currentDevice.value.displayName) : "等待移动设备连接");
const currentDeviceIcon = computed(() => currentDevice.value ? "solar:smartphone-bold-duotone" : "solar:devices-bold-duotone");
const localPairingHost = computed(() => pairing.value?.host ?? workspace.deviceStatus?.localIps?.[0] ?? "127.0.0.1");
const pagePreviewRatioWidth = ref(21);
const pagePreviewRatioHeight = ref(9);
const previewAspectStyle = computed(() => {
  const match = previewRatio.value.trim().match(/^(\d+(?:\.\d+)?)\s*[:/]\s*(\d+(?:\.\d+)?)$/);
  return { aspectRatio: match ? `${match[1]} / ${match[2]}` : "1 / 1" };
});

const componentVisualSections = [
  { id: "base", label: "基础样式" },
  { id: "background", label: "背景与媒体" },
  { id: "text", label: "文字内容" },
  { id: "state", label: "锁定/按下状态" },
  { id: "action", label: "动作系统" },
  { id: "permission", label: "权限声明" },
] as const;

const componentPreviewStyle = computed(() => buildComponentPreviewStyle(visualConfig.value));
const componentPreviewVideoSource = computed(() => visualVideoSource(visualConfig.value));
const componentHasEnteredCodeMode = computed(() => componentEditorMode.value === "code" || String(selectedComponent.value?.editMode).toLowerCase() === "code");
const isComponentVideoPreviewActive = computed(() =>
  activeView.value === "component" &&
  componentRoute.value === "editor" &&
  componentEditorMode.value === "visual" &&
  Boolean(componentPreviewVideoSource.value),
);
const componentVideoPreviewLabel = computed(() => componentVideoPreviewState.value === "error" ? "\u89c6\u9891\u52a0\u8f7d\u5931\u8d25" : "\u89c6\u9891\u52a0\u8f7d\u4e2d");
const pagePreviewBackgroundStyle = computed(() => pageBackgroundStyle(selectedPage.value));
const pagePreviewFrameStyle = computed(() => {
  const size = pagePreviewFrameSize.value;
  return {
    ...pagePreviewBackgroundStyle.value,
    width: size.width > 0 ? `${size.width}px` : "100%",
    height: size.height > 0 ? `${size.height}px` : "100%",
  };
});
const resourcePickerTitle = computed(() => resourcePickerTarget.value === "component-background" ? "选择组件媒体资源" : "选择页面媒体资源");
const resourcePickerKind = computed(() => {
  if (resourcePickerTarget.value === "component-background") return visualConfig.value.background.kind === "video" ? "video" : "image";
  return selectedPage.value?.backgroundKind === "video" ? "video" : "image";
});
const resourcePickerItems = computed(() => workspace.resources.filter((resource) => resource.kind === resourcePickerKind.value));
const pagePreviewStageEl = ref<HTMLElement | null>(null);
const pagePreviewFrameSize = ref({ width: 0, height: 0 });
let pagePreviewResizeObserver: ResizeObserver | null = null;

function measurePagePreviewFrame() {
  const el = pagePreviewStageEl.value;
  if (!el) return;
  const ratioWidth = normalizeRatioNumber(pagePreviewRatioWidth.value, 21);
  const ratioHeight = normalizeRatioNumber(pagePreviewRatioHeight.value, 9);
  pagePreviewFrameSize.value = calculatePreviewFrameSize(el.clientWidth, el.clientHeight, ratioWidth, ratioHeight);
}

function normalizeRatioNumber(value: number, fallback: number) {
  const parsed = Number(value);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
}

function calculatePreviewFrameSize(parentWidth: number, parentHeight: number, ratioWidth: number, ratioHeight: number) {
  const safeWidth = Math.max(0, parentWidth);
  const safeHeight = Math.max(0, parentHeight);
  if (safeWidth <= 0 || safeHeight <= 0) return { width: 0, height: 0 };

  // 页面预览必须完整收进父容器，所以先按可用宽度推导高度，过高时再以高度反算宽度。
  const ratio = ratioWidth / ratioHeight;
  let width = safeWidth;
  let height = width / ratio;
  if (height > safeHeight) {
    height = safeHeight;
    width = height * ratio;
  }
  return { width: Math.floor(width), height: Math.floor(height) };
}

function swapPagePreviewRatio() {
  const width = pagePreviewRatioWidth.value;
  pagePreviewRatioWidth.value = pagePreviewRatioHeight.value;
  pagePreviewRatioHeight.value = width;
}

function bindPagePreviewObserver() {
  if (pagePreviewResizeObserver) {
    pagePreviewResizeObserver.disconnect();
    pagePreviewResizeObserver = null;
  }
  const el = pagePreviewStageEl.value;
  if (!el || typeof ResizeObserver === "undefined") return;
  // 预览容器刚进入 DOM 时 aspect-ratio 可能还没稳定，所以立即测量后再等两个布局帧复测。
  measurePagePreviewFrame();
  requestAnimationFrame(() => requestAnimationFrame(measurePagePreviewFrame));
  pagePreviewResizeObserver = new ResizeObserver(() => {
    measurePagePreviewFrame();
  });
  pagePreviewResizeObserver.observe(el);
}

const pageGridStyle = computed(() => buildPageGridStyle(selectedPage.value, pagePreviewFrameSize.value));
const importPermissionRows = computed(() => pendingInspection.value?.permissions ?? selectedComponent.value?.requestedPermissions ?? []);
const selectedPlugin = computed(() => workspace.plugins.find((plugin) => plugin.id === selectedPluginId.value) ?? workspace.plugins[0] ?? null);
const componentCodeFiles = computed(() => [
  { path: "src/Component.vue", icon: "solar:file-text-bold-duotone" },
  { path: "src/onedesk.actions.json", icon: "solar:bolt-bold-duotone" },
  { path: "onedesk.component.json", icon: "solar:document-bold-duotone" },
  { path: "onedesk.visual.json", icon: "solar:palette-bold-duotone" },
]);
const selectedCell = computed(() => selectedPage.value?.cells.find((cell) => cell.id === selectedCellId.value) ?? selectedPage.value?.cells[0] ?? null);
const selectedComponentActions = computed(() => workspace.actions.filter((action) => selectedComponent.value?.actionIds.includes(action.id)));
const schemeFlowNodes = computed(() => (selectedScheme.value?.pageIds ?? []).map((pageId, index) => ({
  pageId,
  index,
  page: workspace.pages.find((page) => page.id === pageId),
  x: 14 + (index % 3) * 34,
  y: 18 + Math.floor(index / 3) * 30,
})));
const schemeFlowEdges = computed(() => {
  const nodes = schemeFlowNodes.value;
  const edges = selectedScheme.value?.edges ?? [];
  const result: Array<{ edge: (typeof edges)[number]; index: number; from: (typeof nodes)[number]; to: (typeof nodes)[number] }> = [];
  edges.forEach((edge, index) => {
    const from = nodes.find((node) => node.pageId === edge.fromPageId);
    const to = nodes.find((node) => node.pageId === edge.toPageId);
    if (from && to) result.push({ edge, index, from, to });
  });
  return result;
});
const isEditorView = computed(() =>
  (activeView.value === "component" && componentRoute.value === "editor") ||
  (activeView.value === "page" && pageRoute.value === "editor") ||
  (activeView.value === "scheme" && schemeRoute.value === "editor"),
);
const isManagerView = computed(() =>
  (activeView.value === "component" && componentRoute.value === "manager") ||
  (activeView.value === "page" && pageRoute.value === "manager") ||
  (activeView.value === "scheme" && schemeRoute.value === "manager"),
);
const headerTitle = computed(() => {
  if (activeView.value === "home") return "你好，OneDesk!";
  if (activeView.value === "component" && componentRoute.value === "editor") return selectedComponent.value?.name ?? "组件编辑";
  if (activeView.value === "page" && pageRoute.value === "editor") return selectedPage.value?.name ?? "页面编辑";
  if (activeView.value === "scheme" && schemeRoute.value === "editor") return selectedScheme.value?.name ?? "方案编辑";
  return viewTitle.value;
});
const headerSubtitle = computed(() => {
  if (activeView.value === "home") return "\u6b22\u8fce\u56de\u6765\uff0c\u4eca\u5929\u4e5f\u8981\u9ad8\u6548\u63a7\u5236\u6bcf\u4e00\u4e2a\u77ac\u95f4";
  if (isEditorView.value) return "\u540d\u79f0\u53ef\u76f4\u63a5\u7f16\u8f91\uff0c\u4fdd\u5b58\u540e\u540c\u6b65\u5230\u5de5\u4f5c\u533a";
  return "\u7ba1\u7406\u3001\u5bfc\u5165\u3001\u7f16\u8f91\u4e0e\u5e94\u7528\u90fd\u4ece\u8fd9\u91cc\u5f00\u59cb";
});

const editorNameConflict = computed(() => {
  if (activeView.value === "component" && componentRoute.value === "editor" && selectedComponent.value) {
    return findNameConflict("component", selectedComponent.value.id, selectedComponent.value.name);
  }
  if (activeView.value === "page" && pageRoute.value === "editor" && selectedPage.value) {
    return findNameConflict("page", selectedPage.value.id, selectedPage.value.name);
  }
  if (activeView.value === "scheme" && schemeRoute.value === "editor" && selectedScheme.value) {
    return findNameConflict("scheme", selectedScheme.value.id, selectedScheme.value.name);
  }
  return null;
});

onMounted(async () => {
  applyDpiScaling(document.documentElement);
  window.addEventListener("resize", () => applyDpiScaling(document.documentElement));
  setTheme(theme.value);
  await loadWorkspace();
  deviceRefreshTimer = window.setInterval(() => {
    void refreshDeviceConnectivity();
  }, 2000);
  if (!selectedDeviceId.value && trustedDevices.value[0]) selectedDeviceId.value = trustedDevices.value[0].deviceId;
  if (!selectedPluginId.value && workspace.plugins[0]) selectedPluginId.value = workspace.plugins[0].id;
  if (!permissionSourceId.value && workspace.components[0]) permissionSourceId.value = workspace.components[0].id;
});

onUnmounted(() => {
  if (deviceRefreshTimer) window.clearInterval(deviceRefreshTimer);
});

// 页面预览舞台出现或变化时，重新绑定尺寸观察器。
watch(pagePreviewStageEl, () => {
  nextTick(() => bindPagePreviewObserver());
});

watch([pagePreviewRatioWidth, pagePreviewRatioHeight], () => {
  nextTick(() => measurePagePreviewFrame());
});

// 进入页面编辑器时重新测量，保证初次渲染就能按 1:1 计算格子。
watch(() => activeView.value === "page" && pageRoute.value === "editor", (isEditor) => {
  if (isEditor) {
    nextTick(() => bindPagePreviewObserver());
  }
});

watch(() => [selectedPage.value?.rows, selectedPage.value?.columns, selectedPage.value?.spacing?.padding, selectedPage.value?.spacing?.rowGap, selectedPage.value?.spacing?.columnGap], () => {
  nextTick(() => measurePagePreviewFrame());
});

watch(() => workspace.toast, (message) => {
  if (!message || workspace.loading) return;
  pushToast(message);
});

watch(visualConfig, () => {
  syncVisualCodeDraft();
}, { deep: true });

watch(() => selectedComponent.value?.name, () => {
  syncVisualCodeDraft();
});

watch(
  () => [componentPreviewVideoSource.value, activeView.value, componentRoute.value, componentEditorMode.value] as const,
  ([source]) => {
    unloadComponentVideoPreview();
    if (source && isComponentVideoPreviewActive.value) {
      nextTick(() => startComponentVideoPreview());
    }
  },
  { immediate: true },
);

watch(
  () => [selectedPage.value?.id, selectedPage.value?.rows, selectedPage.value?.columns],
  () => ensurePageGridCells(selectedPage.value),
  { immediate: true },
);

function pushToast(message: string) {
  const id = ++toastSequence;
  toasts.value = [...toasts.value, { id, message }].slice(-4);
  window.setTimeout(() => {
    toasts.value = toasts.value.filter((toast) => toast.id !== id);
  }, 3200);
}

function announceToast(message: string) {
  workspace.toast = "";
  window.setTimeout(() => {
    workspace.toast = message;
  }, 0);
}

function startComponentVideoPreview() {
  if (!componentPreviewVideoSource.value || !isComponentVideoPreviewActive.value) {
    unloadComponentVideoPreview();
    return;
  }
  componentVideoPreviewKey.value += 1;
  componentVideoPreviewState.value = "loading";
}

function unloadComponentVideoPreview() {
  componentVideoPreviewKey.value += 1;
  componentVideoPreviewState.value = "idle";
}

function markComponentVideoReady() {
  if (componentVideoPreviewState.value !== "idle") {
    componentVideoPreviewState.value = "ready";
  }
}

function markComponentVideoError() {
  if (componentVideoPreviewState.value !== "idle") {
    componentVideoPreviewState.value = "error";
  }
}

function normalizeName(name: string) {
  return name.trim().toLocaleLowerCase();
}

function findNameConflict(kind: "component" | "page" | "scheme" | "plugin", id: string, name: string) {
  const normalized = normalizeName(name);
  if (!normalized) return null;
  const pools = [
    ...workspace.components.map((item) => ({ kind: "component", id: item.id, name: item.name })),
    ...workspace.pages.map((item) => ({ kind: "page", id: item.id, name: item.name })),
    ...workspace.schemes.map((item) => ({ kind: "scheme", id: item.id, name: item.name })),
    ...workspace.plugins.map((item) => ({ kind: "plugin", id: item.id, name: item.name })),
  ] as const;
  return pools.find((item) => !(item.kind === kind && item.id === id) && normalizeName(item.name) === normalized) ?? null;
}

function ensureUniqueDraftName(baseName: string) {
  const existing = new Set([
    ...workspace.components.map((item) => normalizeName(item.name)),
    ...workspace.pages.map((item) => normalizeName(item.name)),
    ...workspace.schemes.map((item) => normalizeName(item.name)),
    ...workspace.plugins.map((item) => normalizeName(item.name)),
  ]);
  if (!existing.has(normalizeName(baseName))) return baseName;
  let index = 2;
  let nextName = `${baseName} ${index}`;
  while (existing.has(normalizeName(nextName))) {
    index += 1;
    nextName = `${baseName} ${index}`;
  }
  return nextName;
}

function ensureEditorNameAvailable() {
  if (!editorNameConflict.value) return true;
  announceToast(`名称冲突：已存在「${editorNameConflict.value.name}」`);
  return false;
}

async function scrollToVisualSection(sectionId: typeof componentVisualSections[number]["id"]) {
  componentVisualSection.value = sectionId;
  await nextTick();
  const host = componentVisualScrollHost.value;
  const target = host?.querySelector(`[data-visual-section="${sectionId}"]`);
  if (target instanceof HTMLElement) target.scrollIntoView({ behavior: "smooth", block: "start" });
}

function syncVisualSectionFromScroll() {
  const host = componentVisualScrollHost.value;
  if (!host) return;
  const sections = Array.from(host.querySelectorAll("[data-visual-section]"));
  const hostTop = host.getBoundingClientRect().top;
  let active = componentVisualSection.value;
  let minOffset = Number.POSITIVE_INFINITY;
  sections.forEach((section) => {
    if (!(section instanceof HTMLElement)) return;
    const offset = Math.abs(section.getBoundingClientRect().top - hostTop - 24);
    if (offset < minOffset) {
      minOffset = offset;
      active = section.dataset.visualSection as typeof componentVisualSection.value;
    }
  });
  if (active) componentVisualSection.value = active;
}

function openResourcePicker(target: "page-background" | "component-background") {
  resourcePickerTarget.value = target;
  showResourcePicker.value = true;
}

async function addMediaResource() {
  const response = await sendShell<MediaResourceDefinition>("resource.add");
  announceToast(response.ok ? "资源已添加" : response.message ?? "资源添加失败");
  await loadWorkspace({ preserveSelection: true, selectedComponentId: workspace.selectedComponentId, selectedPageId: workspace.selectedPageId, selectedSchemeId: workspace.selectedSchemeId });
}

async function deleteMediaResource(resource: MediaResourceDefinition) {
  const response = await sendShell("resource.delete", { id: resource.id });
  announceToast(response.ok ? "资源已删除" : response.message ?? "资源删除失败");
  await loadWorkspace({ preserveSelection: true, selectedComponentId: workspace.selectedComponentId, selectedPageId: workspace.selectedPageId, selectedSchemeId: workspace.selectedSchemeId });
}

async function chooseMediaResource(resource: MediaResourceDefinition) {
  if (resourcePickerTarget.value === "page-background" && selectedPage.value) {
    const response = await sendShell<MediaResourceCopyResult>("resource.copyToPage", { resourceId: resource.id, targetId: selectedPage.value.id });
    if (!response.ok || !response.payload) {
      announceToast(response.message ?? "资源复制失败");
      return;
    }
    selectedPage.value.backgroundKind = resource.kind === "video" ? "video" : "image";
    selectedPage.value.backgroundResourceId = resource.id;
    selectedPage.value.backgroundValue = resource.id;
    selectedPage.value.backgroundMediaSource = response.payload.fileUri;
    announceToast("资源已复制到页面");
  } else if (resourcePickerTarget.value === "component-background" && selectedComponent.value) {
    const response = await sendShell<MediaResourceCopyResult>("resource.copyToComponent", { resourceId: resource.id, targetId: selectedComponent.value.id });
    if (!response.ok || !response.payload) {
      announceToast(response.message ?? "资源复制失败");
      return;
    }
    visualConfig.value.background.kind = resource.kind === "video" ? "video" : "image";
    visualConfig.value.background.value = resource.id;
    visualConfig.value.background.mediaSource = response.payload.fileUri;
    codeFileDrafts.value["onedesk.visual.json"] = JSON.stringify(visualConfig.value, null, 2);
    announceToast("资源已复制到组件");
  }
  showResourcePicker.value = false;
}

async function enablePageLivePreview() {
  pageLivePreview.value = !pageLivePreview.value;
  if (!pageLivePreview.value) return;
  const ids = Array.from(new Set((selectedPage.value?.cells ?? []).map((cell) => cell.componentId).filter(Boolean))) as string[];
  await Promise.all(ids.map(async (id) => {
    if (componentVisualCache.value[id]) return;
    const component = workspace.components.find((item) => item.id === id);
    const response = await sendShell<Record<string, string>>("workspace.readComponentFiles", { id });
    componentVisualCache.value = {
      ...componentVisualCache.value,
      [id]: parseVisualConfig(response.ok ? response.payload?.["onedesk.visual.json"] : undefined, component),
    };
  }));
}

function componentPreviewForCell(componentId?: string | null) {
  const component = workspace.components.find((item) => item.id === componentId);
  const config = componentId ? (componentVisualCache.value[componentId] ?? parseVisualConfig(undefined, component)) : null;
  return { component, config };
}

function componentPreviewTextsForCell(componentId?: string | null) {
  return componentPreviewForCell(componentId).config?.texts ?? [];
}

function navIcon(item: (typeof navItems)[number]) {
  return activeView.value === item.key ? item.icon.replace("-bold-duotone", "-bold") : item.icon;
}

function openView(view: ViewKey) {
  void runWithEditorLeaveGuard(() => {
    openViewNow(view);
  });
}

function openViewNow(view: ViewKey) {
  activeView.value = view;
  if (view !== "component") componentLoadSequence += 1;
  if (view === "component") componentRoute.value = "manager";
  if (view === "page") pageRoute.value = "manager";
  if (view === "scheme") schemeRoute.value = "manager";
  rememberEditorSavedSnapshot();
}

function editorSnapshot() {
  if (activeView.value === "component" && componentRoute.value === "editor" && selectedComponent.value) {
    return JSON.stringify({
      component: selectedComponent.value,
      visualConfig: visualConfig.value,
      files: codeFileDrafts.value,
      mode: componentEditorMode.value,
    });
  }
  if (activeView.value === "page" && pageRoute.value === "editor" && selectedPage.value) {
    ensurePageGridCells(selectedPage.value);
    return JSON.stringify(selectedPage.value);
  }
  if (activeView.value === "scheme" && schemeRoute.value === "editor" && selectedScheme.value) {
    return JSON.stringify(selectedScheme.value);
  }
  return "";
}

function rememberEditorSavedSnapshot() {
  editorSavedSnapshot.value = editorSnapshot();
}

function hasUnsavedEditorChanges() {
  return Boolean(editorSavedSnapshot.value) && editorSnapshot() !== editorSavedSnapshot.value;
}

async function saveCurrentEditor() {
  if (activeView.value === "component") await saveComponent();
  else if (activeView.value === "page") await savePage();
  else if (activeView.value === "scheme") await saveScheme();
}

async function runWithEditorLeaveGuard(proceed: () => void | Promise<void>) {
  if (!hasUnsavedEditorChanges()) {
    await proceed();
    return;
  }
  pendingEditorLeave.value = { title: "\u5f53\u524d\u7f16\u8f91\u5185\u5bb9\u5c1a\u672a\u4fdd\u5b58", proceed };
}

async function confirmEditorLeave(saveBeforeLeave: boolean) {
  const pending = pendingEditorLeave.value;
  if (!pending) return;
  pendingEditorLeave.value = null;
  if (saveBeforeLeave) {
    await saveCurrentEditor();
  }
  await pending.proceed();
}

function cancelEditorLeave() {
  pendingEditorLeave.value = null;
}

function requestCloseWindow() {
  void runWithEditorLeaveGuard(() => closeWindow());
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
  if (event.button !== 0) return;
  const target = event.target instanceof Element ? event.target : null;
  if (!isMaximized.value) {
    const edge = resizeEdgeFromPointer(event);
    if (edge) {
      void startWindowResize(edge);
      return;
    }
  }
  if (isMaximized.value) return;
  if (target?.closest("button,input,select,textarea,a,.soft-card,.soft-row,.soft-start,.theme-dot,.window-controls,.no-drag,.device-menu,[data-no-window-drag]")) return;
  const headerEl = target?.closest("header");
  const asideEl = target?.closest("aside");
  if (!headerEl && !asideEl) return;
  const scroller = target?.closest(".scrollable");
  if (scroller) {
    const rect = scroller.getBoundingClientRect();
    if (event.clientX >= rect.right - 24 || event.clientY >= rect.bottom - 24) return;
  }
  beginWindowMove(event);
}

function beginWindowMove(event: PointerEvent) {
  windowMovePointerId = event.pointerId;
  windowMoveLastScreenX = event.screenX;
  windowMoveLastScreenY = event.screenY;
  document.documentElement.classList.add("window-moving");
  window.addEventListener("pointermove", moveWindowWithPointer);
  window.addEventListener("pointerup", endWindowMove);
  window.addEventListener("pointercancel", endWindowMove);
  event.preventDefault();
}

function moveWindowWithPointer(event: PointerEvent) {
  if (event.pointerId !== windowMovePointerId) return;
  const dx = Math.round(event.screenX - windowMoveLastScreenX);
  const dy = Math.round(event.screenY - windowMoveLastScreenY);
  windowMoveLastScreenX = event.screenX;
  windowMoveLastScreenY = event.screenY;
  if (dx === 0 && dy === 0) return;
  pendingWindowMoveX += dx;
  pendingWindowMoveY += dy;
  if (pendingWindowMoveFrame) return;
  pendingWindowMoveFrame = window.requestAnimationFrame(flushWindowMove);
}

function flushWindowMove() {
  pendingWindowMoveFrame = 0;
  const dx = pendingWindowMoveX;
  const dy = pendingWindowMoveY;
  pendingWindowMoveX = 0;
  pendingWindowMoveY = 0;
  if (dx !== 0 || dy !== 0) {
    void moveWindowBy(dx, dy);
  }
}

function endWindowMove(event: PointerEvent) {
  if (event.pointerId !== windowMovePointerId) return;
  windowMovePointerId = -1;
  window.removeEventListener("pointermove", moveWindowWithPointer);
  window.removeEventListener("pointerup", endWindowMove);
  window.removeEventListener("pointercancel", endWindowMove);
  document.documentElement.classList.remove("window-moving");
  if (pendingWindowMoveFrame) {
    window.cancelAnimationFrame(pendingWindowMoveFrame);
    flushWindowMove();
  }
}

function resizeEdgeFromPointer(event: PointerEvent) {
  // 高 DPI 屏幕上扩大边缘热区，避免缩放后窗口边缘难以拖拽。
  const margin = Math.max(12, Math.round(12 * window.devicePixelRatio));
  const width = window.innerWidth;
  const height = window.innerHeight;
  const left = event.clientX <= margin;
  const right = event.clientX >= width - margin;
  const top = event.clientY <= margin;
  const bottom = event.clientY >= height - margin;
  if (top && left) return "top-left";
  if (top && right) return "top-right";
  if (bottom && left) return "bottom-left";
  if (bottom && right) return "bottom-right";
  if (left) return "left";
  if (right) return "right";
  if (top) return "top";
  if (bottom) return "bottom";
  return "";
}

const resizeCursorMap: Record<string, string> = {
  left: "ew-resize",
  right: "ew-resize",
  top: "ns-resize",
  bottom: "ns-resize",
  "top-left": "nwse-resize",
  "bottom-right": "nwse-resize",
  "top-right": "nesw-resize",
  "bottom-left": "nesw-resize",
};

function handleWindowPointerMove(event: PointerEvent) {
  if (isMaximized.value || event.buttons !== 0) {
    document.body.style.cursor = "";
    return;
  }
  const edge = resizeEdgeFromPointer(event);
  if (!edge) {
    document.body.style.cursor = "";
    return;
  }
  const target = event.target instanceof Element ? event.target : null;
  if (target?.closest("button,input,select,textarea,a,nav,.soft-card,.soft-row,.soft-start,.theme-dot,.window-controls,.no-drag,.device-menu,[data-no-window-drag]")) {
    document.body.style.cursor = "";
    return;
  }
  document.body.style.cursor = resizeCursorMap[edge] ?? "";
}

async function chooseComponent(component: ComponentDefinition, skipGuard = false) {
  if (!skipGuard && componentRoute.value === "editor" && workspace.selectedComponentId !== component.id) {
    await runWithEditorLeaveGuard(() => chooseComponent(component, true));
    return;
  }
  if (loadingComponentId.value === component.id) return;
  const loadSequence = ++componentLoadSequence;
  loadingComponentId.value = component.id;
  workspace.selectedComponentId = component.id;
  componentEditorMode.value = String(component.editMode).toLowerCase() === "code" ? "code" : "visual";
  componentVisualSection.value = "base";
  try {
    await loadComponentFiles(component, loadSequence);
    if (loadSequence !== componentLoadSequence || workspace.selectedComponentId !== component.id) return;
    componentEditorMode.value = String(component.editMode).toLowerCase() === "code" ? "code" : "visual";
    componentRoute.value = "editor";
    rememberEditorSavedSnapshot();
  } finally {
    if (loadingComponentId.value === component.id) loadingComponentId.value = "";
  }
}

function choosePage(page: PageDefinition, skipGuard = false) {
  if (!skipGuard && pageRoute.value === "editor" && workspace.selectedPageId !== page.id) {
    void runWithEditorLeaveGuard(() => choosePage(page, true));
    return;
  }
  ensurePageGridCells(page);
  workspace.selectedPageId = page.id;
  selectedCellId.value = page.cells[0]?.id ?? "";
  pageRoute.value = "editor";
  rememberEditorSavedSnapshot();
}

function chooseScheme(scheme: SchemeDefinition, skipGuard = false) {
  if (!skipGuard && schemeRoute.value === "editor" && workspace.selectedSchemeId !== scheme.id) {
    void runWithEditorLeaveGuard(() => chooseScheme(scheme, true));
    return;
  }
  workspace.selectedSchemeId = scheme.id;
  schemeRoute.value = "editor";
  rememberEditorSavedSnapshot();
}

async function performDelete() {
  const target = pendingDelete.value;
  if (!target) return;
  pendingDelete.value = null;
  if (target.kind === "component") {
    await deleteComponentNow(target.id);
  } else if (target.kind === "page") {
    await deletePageNow(target.id);
  } else if (target.kind === "scheme") {
    await deleteSchemeNow(target.id);
  } else if (target.kind === "action") {
    await removeComponentActionNow(target.id);
  } else {
    const response = await sendShell("plugin.delete", { id: target.id });
  workspace.toast = response.ok ? "插件已删除" : response.message ?? "插件删除失败";
    await loadWorkspace();
  }
}

function requestDelete(kind: "component" | "page" | "scheme" | "plugin" | "action", id: string, name: string) {
  pendingDelete.value = { kind, id, name };
}

async function deleteComponentNow(id: string) {
  const response = await sendShell("workspace.deleteComponent", { id });
  workspace.toast = response.ok ? "组件已删除" : response.message ?? "组件删除失败";
  await loadWorkspace();
}

async function deleteComponent(component: ComponentDefinition) {
  requestDelete("component", component.id, component.name);
}

async function deletePageNow(id: string) {
  const response = await sendShell("workspace.deletePage", { id });
  workspace.toast = response.ok ? "页面已删除" : response.message ?? "页面删除失败";
  await loadWorkspace();
}

async function deletePage(page: PageDefinition) {
  requestDelete("page", page.id, page.name);
}

async function deleteSchemeNow(id: string) {
  const response = await sendShell("workspace.deleteScheme", { id });
  workspace.toast = response.ok ? "方案已删除" : response.message ?? "方案删除失败";
  await loadWorkspace();
}

async function deleteScheme(scheme: SchemeDefinition) {
  requestDelete("scheme", scheme.id, scheme.name);
}

function requestCodeMode() {
  if (componentEditorMode.value === "code") return;
  showCodeSwitchDialog.value = true;
}

function requestVisualMode() {
  if (componentHasEnteredCodeMode.value) {
    announceToast("代码编辑模式不可回退到可视化编辑");
    return;
  }
  componentEditorMode.value = "visual";
}

async function confirmCodeMode() {
  if (!selectedComponent.value) return;
  syncVisualCodeDraft();
  selectedComponent.value.editMode = "code";
  selectedComponent.value.visualConfigFile = null;
  componentEditorMode.value = "code";
  showCodeSwitchDialog.value = false;
  codeFileDrafts.value[selectedCodeFile.value] = componentCodeDraft.value;
  await saveComponent();
}

async function createComponent() {
  const id = `component-${crypto.randomUUID()}`;
  const name = ensureUniqueDraftName("\u65b0\u7ec4\u4ef6");
  const componentDraft: ComponentDefinition = {
    id,
    name,
    version: "1.0.0",
    editMode: "visual",
    entryFile: "src/Component.vue",
    visualConfigFile: "onedesk.visual.json",
    actionIds: [],
    requestedPermissions: [],
    pluginDependencies: [],
  };
  announceToast("\u6b63\u5728\u521b\u5efa\u7ec4\u4ef6...");
  try {
    const response = await sendShell<ComponentDefinition>("workspace.saveComponent", componentDraft);
    if (!response.ok || !response.payload) {
      announceToast(response.message ?? "\u7ec4\u4ef6\u521b\u5efa\u5931\u8d25");
      return;
    }

    const component = response.payload;
    workspace.components = [component, ...workspace.components.filter((item) => item.id !== component.id)];
    workspace.selectedComponentId = component.id;
    activeView.value = "component";
    componentRoute.value = "editor";
    componentEditorMode.value = "visual";
    componentVisualSection.value = "base";
    visualConfig.value = parseVisualConfig(undefined, component);
    componentCodeDraft.value = generatedComponentCode(component, visualConfig.value);
    hydrateCodeFiles(component);

    const fileResponse = await sendShell<Record<string, string>>("workspace.saveComponentFiles", { id: component.id, files: codeFileDrafts.value });
    if (!fileResponse.ok) {
      announceToast(fileResponse.message ?? "\u7ec4\u4ef6\u521b\u5efa\u540e\u4ee3\u7801\u6587\u4ef6\u521d\u59cb\u5316\u5931\u8d25");
      return;
    }

    await loadWorkspace({ preserveSelection: true, selectedComponentId: component.id });
    workspace.selectedComponentId = component.id;
    componentEditorMode.value = "visual";
    componentRoute.value = "editor";
    componentVisualSection.value = "base";
    visualConfig.value = parseVisualConfig(codeFileDrafts.value["onedesk.visual.json"], component);
    componentCodeDraft.value = codeFileDrafts.value[selectedCodeFile.value] ?? generatedComponentCode(component, visualConfig.value);
    announceToast("\u7ec4\u4ef6\u5df2\u521b\u5efa");
  } catch (error) {
    console.error("createComponent failed", error);
    announceToast("\u7ec4\u4ef6\u521b\u5efa\u5931\u8d25");
  }
}

async function createPage() {
  const id = `page-${crypto.randomUUID()}`;
  const name = ensureUniqueDraftName("\u65b0\u9875\u9762");
  const page: PageDefinition = {
    id,
    name,
    rows: 4,
    columns: 3,
    gridHorizontalAlign: "center",
    gridVerticalAlign: "center",
    spacing: { padding: 16, rowGap: 10, columnGap: 10 },
    backgroundKind: "solid",
    backgroundValue: "#0ea5e9",
    backgroundSecondaryValue: "#22d3ee",
    backgroundResourceId: null,
    backgroundMediaSource: null,
    cells: [],
  };
  ensurePageGridCells(page);
  const response = await sendShell<PageDefinition>("workspace.savePage", page);
  announceToast(response.ok ? "\u9875\u9762\u5df2\u521b\u5efa" : response.message ?? "\u9875\u9762\u521b\u5efa\u5931\u8d25");
  await loadWorkspace({ preserveSelection: true, selectedPageId: id });
  workspace.selectedPageId = id;
  selectedCellId.value = page.cells[0]?.id ?? "";
  pageRoute.value = "editor";
  rememberEditorSavedSnapshot();
}

async function createScheme() {
  const id = `scheme-${crypto.randomUUID()}`;
  const name = ensureUniqueDraftName("\u65b0\u65b9\u6848");
  const firstPageId = workspace.pages[0]?.id ?? "";
  const trigger = { id: "three-finger-swipe-up", category: "touch.standard", displayName: "\u4e09\u6307\u4e0a\u6ed1", fingerCount: 3 };
  const scheme: SchemeDefinition = {
    id,
    name,
    version: "1.0.0",
    pageIds: firstPageId ? [firstPageId] : [],
    globalPrevious: { trigger: { ...trigger, id: "three-finger-swipe-down", displayName: "\u4e09\u6307\u4e0b\u6ed1" }, animation: "fade" },
    globalNext: { trigger, animation: "fade" },
    edges: [],
    pluginDependencies: [],
  };
  const response = await sendShell<SchemeDefinition>("workspace.saveScheme", scheme);
  announceToast(response.ok ? "\u65b9\u6848\u5df2\u521b\u5efa" : response.message ?? "\u65b9\u6848\u521b\u5efa\u5931\u8d25");
  await loadWorkspace({ preserveSelection: true, selectedSchemeId: id });
  workspace.selectedSchemeId = id;
  schemeRoute.value = "editor";
  rememberEditorSavedSnapshot();
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
  if (!ensureEditorNameAvailable()) return;
  if (componentEditorMode.value === "code") {
    codeFileDrafts.value[selectedCodeFile.value] = componentCodeDraft.value;
    selectedComponent.value.editMode = "code";
    selectedComponent.value.visualConfigFile = null;
    codeFileDrafts.value["onedesk.component.json"] = JSON.stringify(selectedComponent.value, null, 2);
  } else {
    codeFileDrafts.value["onedesk.visual.json"] = JSON.stringify(visualConfig.value, null, 2);
    codeFileDrafts.value["onedesk.component.json"] = JSON.stringify(selectedComponent.value, null, 2);
    selectedComponent.value.editMode = "visual";
    selectedComponent.value.visualConfigFile = "onedesk.visual.json";
  }
  const response = await sendShell<ComponentDefinition>("workspace.saveComponent", selectedComponent.value);
  const fileResponse = await sendShell<Record<string, string>>("workspace.saveComponentFiles", { id: selectedComponent.value.id, files: codeFileDrafts.value });
  announceToast(response.ok && fileResponse.ok ? "组件与代码文件已保存" : response.message ?? fileResponse.message ?? "组件保存失败");
  await loadWorkspace({ preserveSelection: true, selectedComponentId: selectedComponent.value.id });
  rememberEditorSavedSnapshot();
}

function addComponentAction() {
  openActionDesigner();
}

function openActionDesigner(action?: ActionDefinition) {
  if (!selectedComponent.value) return;
  const used = new Set(selectedComponentActions.value.filter((item) => item.id !== action?.id).map((item) => item.trigger.id));
  const fallbackTrigger = triggerOptions.value.find((trigger) => !used.has(trigger.id)) ?? triggerOptions.value[0];
  const draft: ActionDefinition = action ? {
    id: action.id,
    name: action.name,
    trigger: buildTriggerDefinition(action.trigger),
    invocations: action.invocations.length ? action.invocations.map((item) => ({
      targetDeviceId: item.targetDeviceId,
      capability: item.capability,
      parameters: { ...item.parameters },
    })) : [defaultActionInvocation()],
  } : {
    id: `action-${crypto.randomUUID()}`,
    name: "\u65b0\u52a8\u4f5c",
    trigger: buildTriggerDefinition(fallbackTrigger),
    invocations: [defaultActionInvocation()],
  };
  actionDraft.value = draft;
  actionDraftParametersText.value = Object.fromEntries(draft.invocations.map((item, index) => [index, JSON.stringify(item.parameters ?? {}, null, 2)]));
  showActionDesignerDialog.value = true;
}

function defaultActionInvocation() {
  return {
    targetDeviceId: "desktop",
    capability: "notification.native",
    parameters: { title: "OneDesk", message: "\u52a8\u4f5c\u5df2\u89e6\u53d1" },
  };
}

function changeActionDraftTrigger(triggerId: string) {
  if (!actionDraft.value) return;
  actionDraft.value.trigger = buildTriggerDefinition(findTrigger(triggerId));
}

function addActionDraftInvocation() {
  if (!actionDraft.value) return;
  actionDraft.value.invocations = [...actionDraft.value.invocations, defaultActionInvocation()];
  const index = actionDraft.value.invocations.length - 1;
  actionDraftParametersText.value = {
    ...actionDraftParametersText.value,
    [index]: JSON.stringify(actionDraft.value.invocations[index].parameters ?? {}, null, 2),
  };
}

function removeActionDraftInvocation(index: number) {
  if (!actionDraft.value || actionDraft.value.invocations.length <= 1) return;
  actionDraft.value.invocations = actionDraft.value.invocations.filter((_, itemIndex) => itemIndex !== index);
  actionDraftParametersText.value = Object.fromEntries(actionDraft.value.invocations.map((item, itemIndex) => [itemIndex, JSON.stringify(item.parameters ?? {}, null, 2)]));
}

async function saveActionDesigner() {
  if (!selectedComponent.value || !actionDraft.value || !actionDraftInvocation.value) return;
  const duplicated = selectedComponentActions.value.some((item) => item.id !== actionDraft.value?.id && item.trigger.id === actionDraft.value?.trigger.id);
  if (duplicated) {
    announceToast("\u540c\u4e00\u7ec4\u4ef6\u5185\u89e6\u53d1\u5fc5\u987b\u552f\u4e00");
    return;
  }
  try {
    actionDraft.value.invocations = actionDraft.value.invocations.map((invocation, index) => ({
      ...invocation,
      parameters: JSON.parse(actionDraftParametersText.value[index] || "{}") as Record<string, unknown>,
    }));
  } catch {
    announceToast("JSAPI \u53c2\u6570\u5fc5\u987b\u662f\u6709\u6548 JSON");
    return;
  }
  const actionResponse = await sendShell<ActionDefinition>("workspace.saveAction", actionDraft.value);
  if (!actionResponse.ok) {
    announceToast(actionResponse.message ?? "\u52a8\u4f5c\u4fdd\u5b58\u5931\u8d25");
    return;
  }
  if (!selectedComponent.value.actionIds.includes(actionDraft.value.id)) {
    selectedComponent.value.actionIds = [...selectedComponent.value.actionIds, actionDraft.value.id];
  }
  showActionDesignerDialog.value = false;
  await saveComponent();
}

function changeActionTrigger(action: ActionDefinition, triggerId: string) {
  const trigger = findTrigger(triggerId);
  action.trigger = buildTriggerDefinition(trigger);
  void sendShell("workspace.saveAction", action);
}

function changeSchemeGlobalTrigger(target: "previous" | "next", triggerId: string) {
  if (!selectedScheme.value) return;
  const trigger = findTrigger(triggerId);
  const next = buildTriggerDefinition(trigger);
  if (target === "previous") selectedScheme.value.globalPrevious.trigger = next;
  else selectedScheme.value.globalNext.trigger = next;
}

async function removeComponentAction(actionId: string) {
  const action = workspace.actions.find((item) => item.id === actionId);
  requestDelete("action", actionId, action?.name ?? "动作");
}

async function removeComponentActionNow(actionId: string) {
  if (!selectedComponent.value) return;
  selectedComponent.value.actionIds = selectedComponent.value.actionIds.filter((id) => id !== actionId);
  await sendShell("workspace.deleteAction", { id: actionId });
  await saveComponent();
}

// 添加一个新的文字层。文字层属于组件内部内容，不再和组件名称强绑定。
function addVisualText() {
  const count = visualConfig.value.texts.length;
  visualConfig.value.texts.push({
    id: `text-${Date.now()}`,
    content: `文字 ${count + 1}`,
    fontSize: 14,
    color: "#ffffff",
    position: "center",
    x: 50,
    y: 50,
    width: 58,
    height: 18,
  });
}

// 删除指定文字层；至少保留一层，避免预览内容完全不可见。
function removeVisualText(id: string) {
  if (visualConfig.value.texts.length <= 1) return;
  visualConfig.value.texts = visualConfig.value.texts.filter((t) => t.id !== id);
}

function beginDragTextLayer(event: PointerEvent, textId: string) {
  if (componentEditorMode.value !== "visual" || resizingTextLayerId.value) return;
  const target = event.currentTarget;
  if (!(target instanceof HTMLElement)) return;
  draggingTextLayerId.value = textId;
  target.setPointerCapture(event.pointerId);
  updateTextLayerFromPointer(event, textId);
}

function dragTextLayer(event: PointerEvent, textId: string) {
  if (componentEditorMode.value !== "visual") return;
  if (draggingTextLayerId.value !== textId) return;
  updateTextLayerFromPointer(event, textId);
}

function endDragTextLayer(event: PointerEvent) {
  const target = event.currentTarget;
  if (target instanceof HTMLElement && target.hasPointerCapture(event.pointerId)) {
    target.releasePointerCapture(event.pointerId);
  }
  draggingTextLayerId.value = null;
}

function updateTextLayerFromPointer(event: PointerEvent, textId: string) {
  if (componentEditorMode.value !== "visual") return;
  const frame = componentPreviewEl.value;
  const text = visualConfig.value.texts.find((item) => item.id === textId);
  if (!frame || !text) return;
  const rect = frame.getBoundingClientRect();
  if (rect.width <= 0 || rect.height <= 0) return;
  text.x = Math.max(0, Math.min(100, ((event.clientX - rect.left) / rect.width) * 100));
  text.y = Math.max(0, Math.min(100, ((event.clientY - rect.top) / rect.height) * 100));
  text.position = "custom";
}

function beginResizeTextLayer(event: PointerEvent, textId: string) {
  if (componentEditorMode.value !== "visual") return;
  const target = event.currentTarget;
  if (!(target instanceof HTMLElement)) return;
  resizingTextLayerId.value = textId;
  target.setPointerCapture(event.pointerId);
  updateTextLayerSizeFromPointer(event, textId);
}

function resizeTextLayer(event: PointerEvent, textId: string) {
  if (resizingTextLayerId.value !== textId) return;
  updateTextLayerSizeFromPointer(event, textId);
}

function endResizeTextLayer(event: PointerEvent) {
  const target = event.currentTarget;
  if (target instanceof HTMLElement && target.hasPointerCapture(event.pointerId)) {
    target.releasePointerCapture(event.pointerId);
  }
  resizingTextLayerId.value = null;
}

function updateTextLayerSizeFromPointer(event: PointerEvent, textId: string) {
  const frame = componentPreviewEl.value;
  const text = visualConfig.value.texts.find((item) => item.id === textId);
  if (!frame || !text) return;
  const rect = frame.getBoundingClientRect();
  if (rect.width <= 0 || rect.height <= 0) return;
  const pointerX = ((event.clientX - rect.left) / rect.width) * 100;
  const pointerY = ((event.clientY - rect.top) / rect.height) * 100;
  text.width = Math.max(8, Math.min(100, Math.abs(pointerX - text.x) * 2));
  text.height = Math.max(6, Math.min(100, Math.abs(pointerY - text.y) * 2));
}

function textLayerPreviewStyle(text: VisualTextLayer, index: number) {
  return {
    ...textPositionStyle(text.position, index, visualConfig.value.texts.length, text.x, text.y),
    width: `${text.width ?? 58}%`,
    minHeight: `${text.height ?? 18}%`,
  };
}

function hydrateCodeFiles(component?: ComponentDefinition) {
  selectedCodeFile.value = "src/Component.vue";
  const config = visualConfig.value;
  codeFileDrafts.value = {
    "src/Component.vue": generatedComponentCode(component, config),
    "src/onedesk.actions.json": JSON.stringify(workspace.actions.filter((action) => component?.actionIds.includes(action.id)), null, 2),
    "onedesk.component.json": JSON.stringify(component ?? {}, null, 2),
    "onedesk.visual.json": JSON.stringify(config, null, 2),
  };
  componentCodeDraft.value = codeFileDrafts.value[selectedCodeFile.value];
}

function syncVisualCodeDraft() {
  if (componentEditorMode.value !== "visual" || !selectedComponent.value) return;
  const generated = generatedComponentCode(selectedComponent.value, visualConfig.value);
  codeFileDrafts.value["src/Component.vue"] = generated;
  if (selectedCodeFile.value === "src/Component.vue") componentCodeDraft.value = generated;
  codeFileDrafts.value["onedesk.visual.json"] = JSON.stringify(visualConfig.value, null, 2);
  codeFileDrafts.value["onedesk.component.json"] = JSON.stringify(selectedComponent.value, null, 2);
}

async function loadComponentFiles(component?: ComponentDefinition, loadSequence = componentLoadSequence) {
  visualConfig.value = parseVisualConfig(undefined, component);
  hydrateCodeFiles(component);
  if (!component?.id) return;
  const response = await sendShell<Record<string, string>>("workspace.readComponentFiles", { id: component.id });
  if (loadSequence !== componentLoadSequence || workspace.selectedComponentId !== component.id) return;
  if (response.ok && response.payload && Object.keys(response.payload).length) {
    codeFileDrafts.value = {
      ...codeFileDrafts.value,
      ...response.payload,
    };
    selectedCodeFile.value = codeFileDrafts.value["src/Component.vue"] ? "src/Component.vue" : Object.keys(codeFileDrafts.value)[0] ?? "src/Component.vue";
    componentCodeDraft.value = codeFileDrafts.value[selectedCodeFile.value] ?? "";
    visualConfig.value = parseVisualConfig(codeFileDrafts.value["onedesk.visual.json"], component);
  }
}

function selectCodeFile(path: string) {
  codeFileDrafts.value[selectedCodeFile.value] = componentCodeDraft.value;
  selectedCodeFile.value = path;
  componentCodeDraft.value = codeFileDrafts.value[path] ?? "";
}

function clampGridCount(value: unknown) {
  const parsed = Number(value);
  if (!Number.isFinite(parsed)) return 1;
  return Math.min(12, Math.max(1, Math.round(parsed)));
}

function defaultPageCellStyle() {
  return { borderRadius: 16, outlineColor: "#e2e8f0", outlineWidth: 1, outlineStyle: "solid" };
}

function ensurePageGridCells(page?: PageDefinition | null) {
  if (!page) return;
  page.rows = clampGridCount(page.rows);
  page.columns = clampGridCount(page.columns);
  page.gridHorizontalAlign ??= "center";
  page.gridVerticalAlign ??= "center";

  const existingByPosition = new Map<string, (typeof page.cells)[number]>();
  for (const cell of page.cells ?? []) {
    const row = clampGridCount(cell.row);
    const column = clampGridCount(cell.column);
    if (row > page.rows || column > page.columns) continue;
    cell.row = row;
    cell.column = column;
    cell.rowSpan = Math.min(clampGridCount(cell.rowSpan), page.rows - row + 1);
    cell.columnSpan = Math.min(clampGridCount(cell.columnSpan), page.columns - column + 1);
    cell.style ??= defaultPageCellStyle();
    cell.style.borderRadius ??= 16;
    cell.style.outlineColor ||= "#e2e8f0";
    cell.style.outlineWidth ??= 1;
    cell.style.outlineStyle ||= "solid";
    existingByPosition.set(`${row}:${column}`, cell);
  }

  const cells: PageDefinition["cells"] = [];
  for (let row = 1; row <= page.rows; row += 1) {
    for (let column = 1; column <= page.columns; column += 1) {
      const key = `${row}:${column}`;
      cells.push(existingByPosition.get(key) ?? {
        id: `${page.id}-cell-${row}-${column}`,
        row,
        column,
        rowSpan: 1,
        columnSpan: 1,
        componentId: null,
        style: defaultPageCellStyle(),
      });
    }
  }

  page.cells = cells;
  if (!page.cells.some((cell) => cell.id === selectedCellId.value)) {
    selectedCellId.value = page.cells[0]?.id ?? "";
  }
}

function handlePageGridChanged() {
  ensurePageGridCells(selectedPage.value);
}

async function savePage() {
  if (!selectedPage.value) return;
  if (!ensureEditorNameAvailable()) return;
  ensurePageGridCells(selectedPage.value);
  const response = await sendShell<PageDefinition>("workspace.savePage", selectedPage.value);
  announceToast(response.ok ? "页面已保存" : response.message ?? "页面保存失败");
  await loadWorkspace({ preserveSelection: true, selectedPageId: selectedPage.value.id });
  rememberEditorSavedSnapshot();
}

async function saveScheme() {
  if (!selectedScheme.value) return;
  if (!ensureEditorNameAvailable()) return;
  const response = await sendShell<SchemeDefinition>("workspace.saveScheme", selectedScheme.value);
  announceToast(response.ok ? "方案已保存" : response.message ?? "方案保存失败");
  await loadWorkspace({ preserveSelection: true, selectedSchemeId: selectedScheme.value.id });
  rememberEditorSavedSnapshot();
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

function beginSchemePageDrag(index: number) {
  draggingSchemePageIndex.value = index;
}

function dropSchemePage(targetIndex: number) {
  if (!selectedScheme.value || draggingSchemePageIndex.value === null) return;
  const pages = [...selectedScheme.value.pageIds];
  const [pageId] = pages.splice(draggingSchemePageIndex.value, 1);
  pages.splice(targetIndex, 0, pageId);
  selectedScheme.value.pageIds = pages;
  draggingSchemePageIndex.value = null;
}

function addSchemeEdge() {
  if (!selectedScheme.value || selectedScheme.value.pageIds.length < 2) return;
  const fromPageId = selectedScheme.value.pageIds[0];
  const toPageId = selectedScheme.value.pageIds[1];
  selectedScheme.value.edges = [
    ...selectedScheme.value.edges,
    {
      fromPageId,
      toPageId,
      trigger: { id: `edge-swipe-${Date.now()}`, category: "touch.standard", displayName: "涓夋寚妯粦", fingerCount: 3 },
      animation: "fade",
    },
  ];
}

function removeSchemeEdge(index: number) {
  if (!selectedScheme.value) return;
  selectedScheme.value.edges = selectedScheme.value.edges.filter((_, edgeIndex) => edgeIndex !== index);
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
    const response = await sendShell<{ code: string; qrPayload: string; expiresInSeconds: number; host?: string; port?: number; localIps?: string[] }>("pairing.generate", { port: connectionPort.value });
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

function runQuickStart(item: { label: string }) {
  if (item.label.includes("连接码")) openDeviceDialog(true);
  else if (item.label.includes("插件")) openView("plugin");
  else if (item.label.includes("帮助")) pushToast("帮助文档位于安装目录 docs 文件夹，前端无法直接联网打开");
}

function leaveEditor() {
  if (activeView.value === "component") componentRoute.value = "manager";
  else if (activeView.value === "page") pageRoute.value = "manager";
  else if (activeView.value === "scheme") schemeRoute.value = "manager";
}

async function savePluginSettings(plugin?: PluginManifest | null) {
  if (!plugin) return;
  const response = await sendShell("plugin.submitSettings", { pluginId: plugin.id, settings: pluginDraft(plugin) });
  workspace.toast = response.ok ? "插件设置已提交" : response.message ?? "插件设置提交失败";
}
</script>

<template>
  <main class="relative h-screen w-screen overflow-hidden text-slate-950 dark:text-slate-100" :class="{ 'window-maximized': isMaximized }" @pointerdown="handleWindowDrag" @pointermove="handleWindowPointerMove">
    <section class="app-shell flex h-full min-h-[600px] min-w-[1020px]">
      <aside class="flex w-[96px] shrink-0 items-start justify-center py-9">
        <nav class="side-nav flex w-[54px] flex-col items-center gap-4 bg-white shadow-[0_16px_40px_rgba(15,23,42,0.08)] dark:bg-slate-950">
          <button v-for="item in navItems" :key="item.key" class="grid size-[40px] shrink-0 place-items-center rounded-full transition" :class="activeView === item.key ? 'bg-sky-500 text-white dark:shadow-[0_10px_24px_rgba(14,165,233,0.35)]' : 'text-slate-500 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-slate-800'" :title="item.label" @click="openView(item.key)">
            <Icon :icon="navIcon(item)" class="size-[20px]" />
          </button>
        </nav>
      </aside>

      <section class="flex min-w-0 flex-1 flex-col px-6 py-5">
        <header class="flex shrink-0 items-start justify-between gap-5">
            <div class="w-[420px] shrink-0 overflow-hidden pt-0.5">
            <div v-if="isEditorView" class="flex items-center gap-3">
              <button class="editor-back-button" @click="leaveEditor">
                <Icon icon="solar:alt-arrow-left-linear" class="size-4" />
              </button>
              <div class="editor-title-input">
                <input v-if="activeView === 'component' && componentRoute === 'editor' && selectedComponent" v-model="selectedComponent.name" class="w-full bg-transparent text-[18px] font-semibold leading-6 outline-none" />
                <input v-else-if="activeView === 'page' && pageRoute === 'editor' && selectedPage" v-model="selectedPage.name" class="w-full bg-transparent text-[18px] font-semibold leading-6 outline-none" />
                <input v-else-if="activeView === 'scheme' && schemeRoute === 'editor' && selectedScheme" v-model="selectedScheme.name" class="w-full bg-transparent text-[18px] font-semibold leading-6 outline-none" />
              </div>
            </div>
            <h1 v-else class="truncate text-[20px] font-semibold leading-6">{{ headerTitle }}</h1>
            <p class="mt-1 text-[12px] text-slate-500 dark:text-slate-400" :class="isEditorView ? 'pl-[48px]' : ''">{{ headerSubtitle }}</p>
            <p v-if="isEditorView && editorNameConflict" class="mt-1 text-[12px] font-medium text-rose-500">
              名称冲突：已存在同名{{ editorNameConflict.kind === 'component' ? '组件' : editorNameConflict.kind === 'page' ? '页面' : editorNameConflict.kind === 'scheme' ? '方案' : '插件' }}
            </p>
          </div>

          <div class="flex min-w-0 flex-1 items-center justify-end gap-3">
            <div v-if="isManagerView" class="flex items-center gap-2">
              <template v-if="activeView === 'component'">
                <button class="header-surface-button" @click="importWorkspace('Component')">导入组件</button>
                <button class="header-primary-button" @click="createComponent">新建组件</button>
              </template>
              <template v-else-if="activeView === 'page'">
                <button class="header-surface-button" @click="importWorkspace('Page')">导入页面</button>
                <button class="header-primary-button" @click="createPage">新建页面</button>
              </template>
              <template v-else-if="activeView === 'scheme'">
                <button class="header-surface-button" @click="importWorkspace('Scheme')">导入方案</button>
                <button class="header-primary-button" @click="createScheme">新建方案</button>
              </template>
            </div>
            <div v-if="isEditorView" class="flex items-center gap-2">
              <button v-if="activeView === 'component'" class="header-surface-button" @click="exportComponent(selectedComponent)">导出</button>
              <button v-if="activeView === 'page'" class="header-surface-button" @click="exportPage(selectedPage)">导出</button>
              <button v-if="activeView === 'scheme'" class="header-surface-button" @click="exportScheme(selectedScheme)">导出</button>
              <button class="header-primary-button" @click="activeView === 'component' ? saveComponent() : activeView === 'page' ? savePage() : saveScheme()">保存</button>
              <button v-if="activeView === 'scheme'" class="header-surface-button" @click="selectedScheme && applyScheme(selectedScheme.id, selectedDeviceId || undefined)">应用</button>
            </div>
            <div v-if="activeView === 'plugin' || activeView === 'permission'" class="flex items-center gap-2">
              <button v-if="activeView === 'plugin'" class="header-surface-button" @click="importPlugin">导入插件</button>
            </div>
            <div class="device-menu relative">
              <button class="header-device-button" title="设备" @click="showDeviceMenu = !showDeviceMenu">
                <Icon :icon="currentDeviceIcon" class="size-4 text-sky-500" />
                <span class="truncate">{{ currentDeviceName }}</span>
              </button>
              <div v-if="showDeviceMenu" class="absolute right-0 top-11 z-30 w-[260px] rounded-[22px] bg-white p-2 shadow-2xl shadow-slate-950/12 dark:bg-slate-950">
                <button class="menu-row" @click="openDeviceDialog(true)"><Icon icon="solar:devices-bold-duotone" class="size-5 text-sky-500" />设备管理</button>
                <div class="my-1 h-px bg-slate-100 dark:bg-slate-800"></div>
                <p class="px-3 py-2 text-[11px] leading-5 text-slate-500">桌面端显示本机局域网 IP 和验证码，由手机端主动连接。</p>
              </div>
            </div>

            <div class="group relative flex size-8 items-start justify-center overflow-visible rounded-full">
              <button class="grid size-8 shrink-0 place-items-center rounded-full text-slate-500 dark:text-slate-300" title="主题">
                <Icon :icon="theme === 'dark' ? 'solar:moon-bold-duotone' : theme === 'light' ? 'solar:sun-2-bold-duotone' : 'solar:monitor-bold-duotone'" class="size-[15px]" />
              </button>
              <div class="absolute left-1/2 top-0 z-20 flex h-8 w-8 -translate-x-1/2 flex-col items-center gap-1 overflow-hidden rounded-full bg-white p-1 opacity-0 shadow-lg shadow-slate-950/10 transition-all duration-200 group-hover:h-auto group-hover:min-h-[88px] group-hover:w-8 group-hover:rounded-full group-hover:py-1.5 group-hover:opacity-100 dark:bg-slate-950">
                <button class="theme-dot" :class="theme === 'light' ? 'theme-dot-active' : ''" title="浅色" @click="setTheme('light')"><Icon icon="solar:sun-2-bold-duotone" class="size-[15px]" /></button>
                <button class="theme-dot" :class="theme === 'dark' ? 'theme-dot-active' : ''" title="深色" @click="setTheme('dark')"><Icon icon="solar:moon-bold-duotone" class="size-[15px]" /></button>
                <button class="theme-dot" :class="theme === 'system' ? 'theme-dot-active' : ''" title="跟随系统" @click="setTheme('system')"><Icon icon="solar:monitor-bold-duotone" class="size-[15px]" /></button>
              </div>
            </div>

            <div class="window-controls ml-2 flex items-center gap-1 text-slate-500 dark:text-slate-300">
              <button class="window-control" title="最小化" @click="minimizeWindow"><span class="win-symbol">&#xE921;</span></button>
              <button class="window-control" :title="isMaximized ? '还原' : '最大化'" @click="toggleMaximize"><span class="win-symbol" v-html="isMaximized ? '&#xE923;' : '&#xE922;'"></span></button>
              <button class="window-control window-control-close" title="关闭" @click="requestCloseWindow"><span class="win-symbol">&#xE8BB;</span></button>
            </div>
          </div>
        </header>

        <div class="min-h-0 flex-1 pt-2 pb-6">
          <section v-if="activeView === 'home'" class="grid h-full grid-cols-[1.25fr_0.9fr] grid-rows-[240px_1fr] gap-5">
            <div class="soft-card p-5">
              <div class="mb-4 flex items-start justify-between">
                <div>
                  <h2 class="text-[16px] font-semibold">移动设备</h2>
                  <p class="mt-2 text-[12px] text-slate-500 dark:text-slate-400">已信任 {{ trustedDevices.length }} 个移动设备</p>
                </div>
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
              <div class="mt-4 flex justify-end"><button class="rounded-full border border-sky-500/60 px-3 py-1.5 text-[11px] font-medium text-sky-600 hover:bg-sky-50 dark:hover:bg-sky-950/40" @click="openDeviceDialog(false)">设备管理</button></div>
            </div>

            <div class="soft-card p-5">
              <h2 class="text-[16px] font-semibold">蹇嵎鎿嶄綔</h2>
              <div class="mt-4 grid gap-3">
                <button v-for="item in quickActions" :key="item.label" class="soft-row group" @click="item.label.includes('创建') ? createScheme() : item.label.includes('导入') ? importWorkspace('Scheme') : openDeviceDialog(true)">
                  <span class="grid size-8 place-items-center rounded-xl dark:bg-slate-800"><Icon :icon="item.icon" :class="['size-5', item.color]" /></span>
                  <span class="min-w-0 flex-1 truncate text-left text-[13px] font-medium">{{ item.label }}</span>
                  <Icon icon="solar:alt-arrow-right-linear" class="size-4 text-slate-400 transition group-hover:translate-x-0.5" />
                </button>
              </div>
            </div>

            <div class="soft-card col-span-2 p-5">
              <h2 class="text-[16px] font-semibold">快速开始</h2>
              <div class="mt-4 grid grid-cols-3 gap-4">
                <button v-for="item in quickStart" :key="item.label" class="soft-start" @click="runQuickStart(item)">
                  <span class="grid size-10 place-items-center rounded-2xl bg-white shadow-sm dark:bg-slate-800"><Icon :icon="item.icon" :class="['size-6', item.color]" /></span>
                  <span class="min-w-0"><span class="block truncate text-[13px] font-semibold">{{ item.label }}</span><span class="mt-1 block truncate text-[12px] text-slate-500 dark:text-slate-400">{{ item.desc }}</span></span>
                </button>
              </div>
            </div>
          </section>

          <section v-else-if="activeView === 'component' && componentRoute === 'manager'" class="scrollable h-full overflow-auto" data-no-window-drag>
            <div class="manager-grid">
              <article v-for="item in workspace.components" :key="item.id" class="manager-card" @click="chooseComponent(item)">
                <div :class="['manager-preview bg-gradient-to-br', previewGradient(item.id)]">
                  <Icon icon="solar:bolt-circle-bold-duotone" class="size-8 text-white" />
                  <span class="mt-2 text-[12px] font-semibold text-white">{{ item.name }}</span>
                </div>
                <div class="mt-3 flex items-start justify-between gap-3">
                  <div class="min-w-0">
                    <p class="truncate text-[14px] font-semibold">{{ item.name }}</p>
                    <p class="mt-1 text-[12px] text-slate-500">{{ componentModeLabel(item) }} · {{ item.actionIds.length }} 动作 · {{ item.version }}</p>
                  </div>
                  <span class="rounded-full bg-sky-50 px-2 py-1 text-[11px] text-sky-600 dark:bg-sky-950/50">{{ item.requestedPermissions.length }} 权限</span>
                </div>
                <div class="mt-4 grid grid-cols-2 gap-2">
                  <button class="card-action" @click.stop="chooseComponent(item)"><Icon icon="solar:pen-bold-duotone" class="size-4" />修改</button>
                  <button class="card-action danger" @click.stop="deleteComponent(item)"><Icon icon="solar:trash-bin-trash-bold-duotone" class="size-4" />删除</button>
                </div>
              </article>
              <button v-if="!workspace.components.length" class="empty-card" @click="createComponent">
                <span>
                  <Icon icon="solar:add-circle-bold-duotone" class="mx-auto size-10 text-sky-500" />
                  <span class="mt-3 block text-[14px] font-semibold">新建第一个组件</span>
                  <span class="mt-1 block text-[12px] text-slate-500">进入组件编辑页后可修改名称、样式、动作和代码。</span>
                </span>
              </button>
            </div>
          </section>

          <section v-else-if="activeView === 'component'" class="grid h-full min-h-0 grid-cols-[200px_1fr_240px] gap-3">
            <aside class="soft-card scrollable min-h-0 overflow-auto p-3" data-no-window-drag>
              <template v-if="componentEditorMode === 'visual'">
                <div class="grid gap-1">
                  <button
                    v-for="section in componentVisualSections"
                    :key="section.id"
                    :class="componentVisualSection === section.id ? 'editor-nav-active' : 'editor-nav'"
                    @click="scrollToVisualSection(section.id)"
                  >
                    {{ section.label }}
                  </button>
                </div>
              </template>
              <template v-else>
                <div class="grid gap-1.5 text-[12px]">
                  <button v-for="file in componentCodeFiles" :key="file.path" :class="selectedCodeFile === file.path ? 'file-row file-row-active' : 'file-row'" @click="selectCodeFile(file.path)">
                    <Icon :icon="file.icon" class="size-4" />
                    {{ file.path }}
                  </button>
                </div>
              </template>
            </aside>
            <section
              ref="componentVisualScrollHost"
              class="soft-card scrollable min-h-0 min-w-0 overflow-auto p-5 editor-section"
              data-no-window-drag
              @scroll="componentEditorMode === 'visual' ? syncVisualSectionFromScroll() : undefined"
            >
              <div class="mb-5 flex items-center gap-2">
                <div class="editor-toggle-group">
                  <button :class="componentEditorMode === 'visual' ? 'editor-toggle-active' : ''" :disabled="componentHasEnteredCodeMode" @click="requestVisualMode">可视化</button>
                  <button :class="componentEditorMode === 'code' ? 'editor-toggle-active' : ''" @click="requestCodeMode">代码</button>
                </div>
              </div>
              <div v-if="componentEditorMode === 'visual'" class="grid gap-5">
                <section data-visual-section="base">
                  <div class="editor-section-head"><h3>基础样式</h3><p>控制组件容器布局、圆角和边距。</p></div>
                  <div class="editor-form-row">
                    <label class="field-label editor-field-auto"><span>布局</span><select v-model="visualConfig.base.layout" class="field"><option value="center">居中</option><option value="left">靠左</option><option value="right">靠右</option><option value="bottom">靠下</option></select></label>
                    <label class="field-label editor-field-num"><span>圆角</span><input v-model.number="visualConfig.base.borderRadius" type="number" min="0" class="field" /></label>
                    <label class="field-label editor-field-num"><span>边距</span><input v-model.number="visualConfig.base.margin" type="number" min="0" class="field" /></label>
                  </div>
                </section>
                <section data-visual-section="background">
                  <div class="editor-section-head"><h3>背景与媒体</h3><p>按背景类型显示对应的颜色或文件选择器。</p></div>
                  <div class="editor-form-row">
                    <label class="field-label editor-field-grow"><span>背景类型</span><select v-model="visualConfig.background.kind" class="field"><option value="gradient">渐变背景</option><option value="solid">纯色背景</option><option value="image">图片背景</option><option value="video">视频背景</option></select></label>
                    <template v-if="visualConfig.background.kind === 'solid'">
                      <label class="field-label editor-field-auto"><span>纯色</span><input v-model="visualConfig.background.value" type="color" class="field editor-color-input" /></label>
                    </template>
                    <template v-else-if="visualConfig.background.kind === 'gradient'">
                      <label class="field-label editor-field-auto"><span>起始颜色</span><input v-model="visualConfig.background.value" type="color" class="field editor-color-input" /></label>
                      <label class="field-label editor-field-auto"><span>结束颜色</span><input v-model="visualConfig.background.secondaryValue" type="color" class="field editor-color-input" /></label>
                    </template>
                    <template v-else>
                      <label class="field-label editor-field-grow"><span>资源 ID</span><input v-model="visualConfig.background.value" class="field" placeholder="从资源管理器选择后写入" /></label>
                      <button class="editor-inline-button editor-field-align" type="button" @click="openResourcePicker('component-background')"><Icon :icon="visualConfig.background.kind === 'image' ? 'solar:gallery-add-bold-duotone' : 'solar:video-library-bold-duotone'" class="size-[14px]" />选择资源</button>
                    </template>
                  </div>
                  <div class="editor-form-row mt-2">
                    <label class="field-label editor-field-auto"><span>图片尺寸</span><select v-model="visualConfig.image.size" class="field"><option value="cover">填充覆盖</option><option value="contain">完整显示</option></select></label>
                    <label class="field-label editor-field-auto"><span>图片位置</span><select v-model="visualConfig.image.position" class="field"><option value="center">居中</option><option value="left">靠左</option><option value="right">靠右</option><option value="top">靠上</option><option value="bottom">靠下</option></select></label>
                    <label class="field-label editor-field-num"><span>图片边距</span><input v-model.number="visualConfig.image.margin" type="number" min="0" class="field" /></label>
                  </div>
                </section>
                <section data-visual-section="text">
                  <div class="editor-section-head editor-section-head-row"><div><h3>文字内容</h3><p>每个文字层可独立设置内容、字号、颜色和位置。</p></div><button class="editor-inline-button" @click="addVisualText"><Icon icon="solar:add-circle-bold-duotone" class="size-[14px]" />添加文字</button></div>
                  <div class="grid gap-3">
                    <div v-for="(text, index) in visualConfig.texts" :key="text.id" class="editor-text-card">
                      <div class="flex items-center gap-2">
                        <span class="text-[11px] font-bold text-slate-400">#{{ index + 1 }}</span>
                        <input v-model="text.content" class="field h-8 flex-1 min-w-0" placeholder="输入文字内容" />
                        <button v-if="visualConfig.texts.length > 1" class="editor-icon-button" @click="removeVisualText(text.id)"><Icon icon="solar:trash-bin-trash-bold-duotone" class="size-[16px]" /></button>
                      </div>
                      <div class="editor-form-row mt-2">
                        <label class="field-label editor-field-num"><span>字号</span><input v-model.number="text.fontSize" type="number" min="8" class="field" /></label>
                        <label class="field-label editor-field-auto"><span>颜色</span><input v-model="text.color" type="color" class="field editor-color-input" /></label>
                        <label class="field-label editor-field-grow"><span>位置</span><select v-model="text.position" class="field"><option value="center">居中</option><option value="left">靠左</option><option value="right">靠右</option><option value="top">靠上</option><option value="bottom">靠下</option></select></label>
                      </div>
                    </div>
                  </div>
                </section>
                <section data-visual-section="state">
                  <div class="editor-section-head"><h3>锁定与按下状态</h3><p>为移动端按钮配置状态反馈。</p></div>
                  <div class="editor-form-row">
                    <label class="field-label editor-field-grow"><span>按下效果</span><select v-model="visualConfig.states.pressed" class="field"><option value="scale-95">按下缩小</option><option value="brightness-110">按下高亮</option><option value="none">无</option></select></label>
                    <label class="field-label editor-field-grow"><span>锁定效果</span><select v-model="visualConfig.states.locked" class="field"><option value="opacity-60">降低透明度</option><option value="grayscale">增加灰度蒙层</option><option value="none">无</option></select></label>
                  </div>
                </section>
                <section data-visual-section="action">
                  <div class="editor-section-head editor-section-head-row"><div><h3>动作系统</h3><p>动作仍保持每个触发唯一。</p></div><button class="editor-inline-button" @click="addComponentAction"><Icon icon="solar:add-circle-bold-duotone" class="size-[14px]" />添加动作</button></div>
                  <div class="grid gap-3">
                    <div v-for="action in selectedComponentActions" :key="action.id" class="action-summary-card">
                      <div class="min-w-0 flex-1">
                        <div class="flex items-center justify-between gap-3">
                          <p class="truncate text-[13px] font-semibold">{{ action.name }}</p>
                          <span class="rounded-full bg-sky-500/10 px-2.5 py-1 text-[10px] font-semibold text-sky-500">{{ action.invocations.length }} JSAPI</span>
                        </div>
                        <div class="mt-3 grid gap-2">
                          <div class="action-flow-row"><span>1</span><div><p>触发</p><b>{{ triggerLabel(action.trigger) }}</b></div></div>
                          <div class="action-flow-row"><span>2</span><div><p>动作</p><b>调用 JSAPI · {{ action.invocations[0]?.capability || "未设置" }}</b></div></div>
                        </div>
                      </div>
                      <div class="grid shrink-0 gap-2">
                        <button class="editor-inline-button" @click="openActionDesigner(action)"><Icon icon="solar:pen-bold-duotone" class="size-[14px]" />编辑</button>
                        <button class="editor-inline-button editor-inline-danger" @click="removeComponentAction(action.id)"><Icon icon="solar:trash-bin-trash-bold-duotone" class="size-[14px]" />删除</button>
                      </div>
                    </div>
                    <p v-if="!selectedComponentActions.length" class="editor-empty-hint">暂无动作。点击添加动作后，在流程设计器里配置触发与 JSAPI 调用。</p>
                  </div>
                </section>
                <section data-visual-section="permission">
                  <div class="editor-section-head editor-section-head-row"><div><h3>权限声明</h3><p>导入和后续设置都会使用同一套授权信息。</p></div><button class="editor-inline-button editor-inline-primary" @click="showPermissionDialog = true"><Icon icon="solar:shield-keyhole-bold-duotone" class="size-[14px]" />打开授权</button></div>
                  <div class="grid gap-2">
                    <div v-for="permission in selectedComponent?.requestedPermissions ?? []" :key="permission.capability" class="editor-permission-row">
                      <span class="font-semibold text-[12px]">{{ permission.capability }}</span>
                      <span v-if="permission.highRisk" class="ml-2 rounded-full bg-rose-100 px-2 py-0.5 text-[10px] font-medium text-rose-600 dark:bg-rose-950 dark:text-rose-300">高危</span>
                      <p class="mt-0.5 text-[11px] text-slate-500">{{ permission.description }}</p>
                    </div>
                    <p v-if="!(selectedComponent?.requestedPermissions?.length)" class="editor-empty-hint">当前组件未声明额外权限。</p>
                  </div>
                </section>
              </div>
              <div v-else class="overflow-hidden rounded-[18px] bg-slate-950">
                <div class="flex h-9 items-center border-b border-slate-800 px-4 text-[12px] text-slate-400">{{ selectedCodeFile }}</div>
                <CodeMirrorEditor v-model="componentCodeDraft" :filename="selectedCodeFile" class="h-[390px]" />
              </div>
            </section>
            <aside class="soft-card scrollable min-h-0 min-w-0 overflow-auto p-3" data-no-window-drag>
              <div class="flex items-start justify-between gap-3">
                <h3 class="min-w-0 text-[13px] font-semibold">实时预览</h3>
                <label class="field-label ratio-field"><span>比例</span><input v-model="previewRatio" class="field h-8 px-2 text-center" placeholder="1:1" /></label>
              </div>
              <div ref="componentPreviewEl" class="mt-4 grid overflow-hidden rounded-[22px] shadow-lg shadow-sky-500/18" :style="[previewAspectStyle, componentPreviewStyle]">
                <div class="relative h-full w-full overflow-hidden text-center">
                  <video
                    v-if="isComponentVideoPreviewActive && componentVideoPreviewState !== 'idle'"
                    :key="componentVideoPreviewKey"
                    class="absolute inset-0 h-full w-full object-cover"
                    :src="componentPreviewVideoSource"
                    autoplay
                    muted
                    loop
                    playsinline
                    preload="auto"
                    @loadstart="componentVideoPreviewState = 'loading'"
                    @loadeddata="markComponentVideoReady"
                    @canplay="markComponentVideoReady"
                    @error="markComponentVideoError"
                  ></video>
                  <div v-if="componentPreviewVideoSource && componentVideoPreviewState !== 'ready'" class="absolute inset-0 z-[1] grid place-items-center bg-slate-950/80 px-4 text-center">
                    <div class="grid justify-items-center gap-2 text-white">
                      <Icon icon="solar:video-frame-play-horizontal-bold-duotone" class="size-8 text-sky-300" />
                      <p class="text-[12px] font-semibold">{{ componentVideoPreviewLabel }}</p>
                      <div v-if="componentVideoPreviewState === 'loading'" class="video-preview-progress"><span></span></div>
                      <p class="max-w-[180px] truncate text-[10px] text-slate-300">{{ componentPreviewVideoSource.split('/').pop() }}</p>
                    </div>
                  </div>
                  <div
                    v-for="(text, index) in visualConfig.texts"
                    :key="text.id"
                    class="component-text-layer absolute z-[1] select-none rounded-xl px-2 py-1 transition-shadow"
                    :class="componentEditorMode === 'visual' ? 'cursor-move hover:bg-slate-950/20 hover:shadow-lg' : 'cursor-default'"
                    :style="textLayerPreviewStyle(text, index)"
                    data-no-window-drag
                    @pointerdown.stop.prevent="beginDragTextLayer($event, text.id)"
                    @pointermove.stop.prevent="dragTextLayer($event, text.id)"
                    @pointerup.stop.prevent="endDragTextLayer"
                    @pointercancel.stop.prevent="endDragTextLayer"
                  >
                    <p class="grid h-full min-h-[28px] place-items-center overflow-hidden break-words text-center font-semibold leading-snug" :style="{ fontSize: `${text.fontSize}px`, color: text.color }">{{ text.content || '文字' }}</p>
                    <span
                      v-if="componentEditorMode === 'visual'"
                      class="component-text-resize-handle"
                      data-no-window-drag
                      @pointerdown.stop.prevent="beginResizeTextLayer($event, text.id)"
                      @pointermove.stop.prevent="resizeTextLayer($event, text.id)"
                      @pointerup.stop.prevent="endResizeTextLayer"
                      @pointercancel.stop.prevent="endResizeTextLayer"
                    ></span>
                  </div>
                </div>
              </div>
              <div class="mt-4 grid gap-2 text-[12px] text-slate-500">
                <p>预览比例：{{ previewRatio || '1:1' }}</p>
                <p>背景：{{ visualConfig.background.kind }} · {{ visualConfig.background.value || '默认' }}</p>
                <p>溢出策略：隐藏</p>
                <p>权限：{{ selectedComponent?.requestedPermissions.length }} 项</p>
              </div>
            </aside>
          </section>

<section v-else-if="activeView === 'page' && pageRoute === 'manager'" class="scrollable h-full overflow-auto" data-no-window-drag>
            <div class="manager-grid"><article v-for="page in workspace.pages" :key="page.id" class="manager-card" @click="choosePage(page)"><div class="manager-preview bg-slate-100 dark:bg-slate-800"><div class="grid h-full w-full gap-1 rounded-2xl p-2" :style="{ gridTemplateColumns: `repeat(${page.columns}, minmax(0, 1fr))`, gridTemplateRows: `repeat(${page.rows}, minmax(0, 1fr))` }"><span v-for="cell in page.cells.slice(0, 12)" :key="cell.id" class="rounded-md bg-white dark:bg-slate-950" :class="cell.componentId ? 'ring-1 ring-sky-400' : ''"></span></div></div><p class="mt-3 text-[14px] font-semibold">{{ page.name }}</p><p class="mt-1 text-[12px] text-slate-500">{{ page.rows }} x {{ page.columns }} · {{ pageComponentCount(page) }} 组件 · {{ page.backgroundKind }}</p><div class="mt-4 grid grid-cols-2 gap-2"><button class="card-action" @click.stop="choosePage(page)"><Icon icon="solar:pen-bold-duotone" class="size-4" />修改</button><button class="card-action danger" @click.stop="deletePage(page)"><Icon icon="solar:trash-bin-trash-bold-duotone" class="size-4" />删除</button></div></article></div>
          </section>

          <section v-else-if="activeView === 'page'" class="h-full">
            <div class="grid h-full min-h-0 grid-cols-[320px_1fr] gap-5">
              <aside class="soft-card page-settings-panel min-h-0 overflow-hidden" data-no-window-drag>
                <div class="scrollable h-full overflow-auto p-4" data-no-window-drag>
                <div class="editor-section-card">
                  <div class="editor-section-head">
                    <h3>格子矩阵</h3>
                    <p>设置行列、间距与矩阵在移动页面中的位置。</p>
                  </div>
                  <div class="editor-section-grid">
                    <label v-if="selectedPage" class="field-label"><span>行数</span><input v-model.number="selectedPage.rows" type="number" min="1" max="12" class="field" @input="handlePageGridChanged" @change="handlePageGridChanged" /></label>
                    <label v-if="selectedPage" class="field-label"><span>列数</span><input v-model.number="selectedPage.columns" type="number" min="1" max="12" class="field" @input="handlePageGridChanged" @change="handlePageGridChanged" /></label>
                    <label v-if="selectedPage" class="field-label"><span>水平对齐</span><select v-model="selectedPage.gridHorizontalAlign" class="field"><option value="left">靠左</option><option value="center">居中</option><option value="right">靠右</option></select></label>
                    <label v-if="selectedPage" class="field-label"><span>垂直对齐</span><select v-model="selectedPage.gridVerticalAlign" class="field"><option value="top">靠上</option><option value="center">居中</option><option value="bottom">靠下</option></select></label>
                    <label v-if="selectedPage" class="field-label"><span>页边距</span><input v-model.number="selectedPage.spacing.padding" type="number" min="0" class="field" /></label>
                    <label v-if="selectedPage" class="field-label"><span>行间距</span><input v-model.number="selectedPage.spacing.rowGap" type="number" min="0" class="field" /></label>
                    <label v-if="selectedPage" class="field-label"><span>列间距</span><input v-model.number="selectedPage.spacing.columnGap" type="number" min="0" class="field" /></label>
                  </div>
                </div>

                <div class="editor-section-card mt-4">
                  <div class="editor-section-head">
                    <h3>页面背景</h3>
                    <p>颜色直接保存；图片和视频必须从资源管理器选择。</p>
                  </div>
                  <div class="editor-section-grid">
                    <label v-if="selectedPage" class="field-label"><span>背景类型</span><select v-model="selectedPage.backgroundKind" class="field"><option value="solid">纯色背景</option><option value="gradient">渐变背景</option><option value="image">图片背景</option><option value="video">视频背景</option></select></label>
                    <template v-if="selectedPage?.backgroundKind === 'solid'">
                      <label class="field-label"><span>背景颜色</span><input v-model="selectedPage.backgroundValue" type="color" class="field h-10 p-1" /></label>
                    </template>
                    <template v-else-if="selectedPage?.backgroundKind === 'gradient'">
                      <label class="field-label"><span>起始颜色</span><input v-model="selectedPage.backgroundValue" type="color" class="field h-10 p-1" /></label>
                      <label class="field-label"><span>结束颜色</span><input v-model="selectedPage.backgroundSecondaryValue" type="color" class="field h-10 p-1" /></label>
                    </template>
                    <template v-else>
                      <label class="field-label"><span>{{ selectedPage?.backgroundKind === 'video' ? '视频资源 ID' : '图片资源 ID' }}</span><input v-if="selectedPage" v-model="selectedPage.backgroundValue" class="field" placeholder="从资源管理器选择后写入" /></label>
                      <button class="resource-pick-button" type="button" @click="openResourcePicker('page-background')"><Icon :icon="selectedPage?.backgroundKind === 'video' ? 'solar:video-library-bold-duotone' : 'solar:gallery-add-bold-duotone'" class="size-4" />选择资源</button>
                    </template>
                  </div>
                </div>

                <div v-if="selectedCell" class="editor-section-card mt-4">
                  <div class="editor-section-head">
                    <h3>当前格子</h3>
                    <p>绑定组件并设置格子的占位、圆角与轮廓。</p>
                  </div>
                  <div class="editor-section-grid">
                    <label class="field-label"><span>跨行</span><input v-model.number="selectedCell.rowSpan" type="number" min="1" :max="selectedPage?.rows ?? 12" class="field" @input="handlePageGridChanged" @change="handlePageGridChanged" /></label>
                    <label class="field-label"><span>跨列</span><input v-model.number="selectedCell.columnSpan" type="number" min="1" :max="selectedPage?.columns ?? 12" class="field" @input="handlePageGridChanged" @change="handlePageGridChanged" /></label>
                    <label class="field-label"><span>绑定组件</span><select v-model="selectedCell.componentId" class="field"><option :value="null">不绑定组件</option><option v-for="component in workspace.components" :key="component.id" :value="component.id">{{ component.name }}</option></select></label>
                    <label class="field-label"><span>圆角</span><input v-model.number="selectedCell.style.borderRadius" type="number" min="0" class="field" /></label>
                    <label class="field-label"><span>轮廓颜色</span><input v-model="selectedCell.style.outlineColor" type="color" class="field h-10 p-1" /></label>
                    <label class="field-label"><span>轮廓宽度</span><input v-model.number="selectedCell.style.outlineWidth" type="number" min="0" class="field" /></label>
                    <label class="field-label"><span>轮廓样式</span><select v-model="selectedCell.style.outlineStyle" class="field"><option value="solid">实线</option><option value="dashed">虚线</option><option value="dotted">点线</option></select></label>
                  </div>
                </div>
                <p class="mt-4 text-[11px] leading-5 text-slate-500">预览比例来自当前选择移动设备：{{ currentDeviceName }}</p>
                </div>
              </aside>
              <div class="soft-card flex min-h-0 flex-col p-5">
                <div class="mb-4 flex w-full items-center justify-between gap-3">
                  <div>
                    <h3 class="text-[14px] font-semibold">页面预览</h3>
                    <p class="mt-1 text-[12px] text-slate-500">开启真实预览后，格子会显示绑定组件保存时的内容。</p>
                  </div>
                  <div class="page-preview-controls">
                    <label class="field-label page-ratio-input"><span>宽</span><input v-model.number="pagePreviewRatioWidth" type="number" min="1" class="field" @input="measurePagePreviewFrame" /></label>
                    <button class="ratio-swap-button" title="对调宽高比例" @click="swapPagePreviewRatio">
                      <Icon icon="solar:transfer-horizontal-bold-duotone" class="size-4" />
                    </button>
                    <label class="field-label page-ratio-input"><span>高</span><input v-model.number="pagePreviewRatioHeight" type="number" min="1" class="field" @input="measurePagePreviewFrame" /></label>
                    <button class="header-surface-button" :class="pageLivePreview ? 'ring-2 ring-sky-400' : ''" @click="enablePageLivePreview">{{ pageLivePreview ? '关闭真实预览' : '开启真实预览' }}</button>
                  </div>
                </div>
                <div ref="pagePreviewStageEl" class="page-preview-stage">
                  <div class="page-preview-frame overflow-hidden rounded-[24px] bg-slate-100 dark:bg-slate-800" :style="pagePreviewFrameStyle">
                    <div class="page-grid-preview" :style="pageGridStyle">
                    <button
                      v-for="cell in selectedPage?.cells"
                      :key="cell.id"
                      class="page-grid-cell overflow-hidden bg-white text-[10px] text-slate-500 dark:bg-slate-900"
                      :class="selectedCellId === cell.id ? 'ring-2 ring-sky-400' : ''"
                      :style="{ gridColumnStart: cell.column, gridColumnEnd: `span ${cell.columnSpan}`, gridRowStart: cell.row, gridRowEnd: `span ${cell.rowSpan}`, borderRadius: `${cell.style.borderRadius}px`, border: `${cell.style.outlineWidth}px ${cell.style.outlineStyle} ${cell.style.outlineColor}` }"
                      @click="selectedCellId = cell.id"
                    >
                      <template v-if="pageLivePreview && cell.componentId">
                        <span class="component-live-tile" :style="componentTileStyle(componentPreviewForCell(cell.componentId).config)">
                          <template v-for="(text, ti) in componentPreviewTextsForCell(cell.componentId)" :key="text.id">
                            <span class="absolute" :style="textPositionStyle(text.position, ti, componentPreviewTextsForCell(cell.componentId).length || 1, text.x, text.y)"><span :style="{ fontSize: `${text.fontSize ?? 12}px`, color: text.color ?? '#ffffff' }">{{ text.content || componentPreviewForCell(cell.componentId).component?.name }}</span></span>
                          </template>
                          <span v-if="!componentPreviewTextsForCell(cell.componentId).length" :style="{ fontSize: '12px' }">{{ componentPreviewForCell(cell.componentId).component?.name }}</span>
                        </span>
                      </template>
                      <span v-else-if="cell.componentId" class="grid h-full place-items-center px-1 text-center">{{ workspace.components.find((item) => item.id === cell.componentId)?.name }}</span>
                    </button>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </section>

          <section v-else-if="activeView === 'scheme' && schemeRoute === 'manager'" class="scrollable h-full overflow-auto" data-no-window-drag>
            <div class="manager-grid">
              <article v-for="scheme in workspace.schemes" :key="scheme.id" class="manager-card" @click="chooseScheme(scheme)">
                <div :class="['manager-preview bg-gradient-to-br', previewGradient(scheme.id)]">
                  <Icon icon="solar:play-circle-bold-duotone" class="size-9 text-white" />
                  <span class="mt-2 text-[12px] font-semibold text-white">{{ scheme.name }}</span>
                </div>
                <p class="mt-3 text-[14px] font-semibold">{{ scheme.name }}</p>
                <p class="mt-1 text-[12px] text-slate-500">{{ scheme.pageIds.length }} 页面 · {{ scheme.pluginDependencies.length }} 插件依赖</p>
                <p class="mt-2 text-[12px]" :class="workspace.activeSchemeId === scheme.id ? 'text-sky-600' : 'text-slate-500'">{{ workspace.activeSchemeId === scheme.id ? '已应用' : '未应用' }}</p>
                <div class="mt-4 grid grid-cols-2 gap-2">
                  <button class="card-action" @click.stop="chooseScheme(scheme)"><Icon icon="solar:pen-bold-duotone" class="size-4" />修改</button>
                  <button class="card-action danger" @click.stop="deleteScheme(scheme)"><Icon icon="solar:trash-bin-trash-bold-duotone" class="size-4" />删除</button>
                </div>
              </article>
            </div>
          </section>

          <section v-else-if="activeView === 'scheme'" class="h-full">
            <div class="grid h-full min-h-0 grid-cols-[300px_1fr] gap-5">
              <aside class="soft-card scheme-settings-panel min-h-0 overflow-hidden" data-no-window-drag>
                <div class="scrollable h-full overflow-auto p-4" data-no-window-drag>
                <select class="field mt-1 text-[12px]" @change="handleAddSchemePage">
                  <option value="">添加页面到方案</option>
                  <option v-for="page in workspace.pages.filter((page) => !selectedScheme?.pageIds.includes(page.id))" :key="page.id" :value="page.id">{{ page.name }}</option>
                </select>

                <div class="mt-4 grid gap-2 text-[12px]">
                  <div v-for="pageId in selectedScheme?.pageIds" :key="pageId" class="rounded-xl bg-slate-50 px-3 py-2 dark:bg-slate-800">
                    <div class="font-semibold">{{ workspace.pages.find((page) => page.id === pageId)?.name ?? pageId }}</div>
                    <div class="mt-2 flex gap-1">
                      <button class="card-action h-7 flex-1" @click="moveSchemePage(pageId, -1)">上移</button>
                      <button class="card-action h-7 flex-1" @click="moveSchemePage(pageId, 1)">下移</button>
                      <button class="card-action danger h-7 flex-1" @click="removePageFromScheme(pageId)">删除</button>
                    </div>
                  </div>
                </div>

                <div class="mt-4 grid gap-2 text-[12px]">
                  <h3 class="text-[13px] font-semibold">全局切换</h3>
                  <label class="field-label">上一页触发
                    <select v-if="selectedScheme" class="field" :value="selectedScheme.globalPrevious.trigger.id" @change="changeSchemeGlobalTrigger('previous', ($event.target as HTMLSelectElement).value)">
                      <optgroup v-for="group in triggerCatalog" :key="group.category" :label="group.label">
                        <option v-for="trigger in group.triggers" :key="trigger.id" :value="trigger.id">{{ trigger.displayName }}</option>
                      </optgroup>
                    </select>
                  </label>
                  <label class="field-label">上一页动画
                    <select v-if="selectedScheme" v-model="selectedScheme.globalPrevious.animation" class="field">
                      <option value="fade">渐入渐退</option>
                      <option value="slide">滑动</option>
                      <option value="none">无动画</option>
                    </select>
                  </label>
                  <label class="field-label">下一页触发
                    <select v-if="selectedScheme" class="field" :value="selectedScheme.globalNext.trigger.id" @change="changeSchemeGlobalTrigger('next', ($event.target as HTMLSelectElement).value)">
                      <optgroup v-for="group in triggerCatalog" :key="group.category" :label="group.label">
                        <option v-for="trigger in group.triggers" :key="trigger.id" :value="trigger.id">{{ trigger.displayName }}</option>
                      </optgroup>
                    </select>
                  </label>
                  <label class="field-label">下一页动画
                    <select v-if="selectedScheme" v-model="selectedScheme.globalNext.animation" class="field">
                      <option value="fade">渐入渐退</option>
                      <option value="slide">滑动</option>
                      <option value="none">无动画</option>
                    </select>
                  </label>
                </div>

                <div class="mt-4 grid gap-2 text-[12px]">
                  <div class="flex items-center justify-between">
                    <h3 class="text-[13px] font-semibold">页面跳转边</h3>
                    <button class="text-sky-600" @click="addSchemeEdge">新增边</button>
                  </div>
                  <div v-for="(edge, index) in selectedScheme?.edges" :key="`${edge.fromPageId}-${edge.toPageId}-${index}`" class="grid gap-2 rounded-xl bg-slate-50 p-2 dark:bg-slate-800">
                    <select v-model="edge.fromPageId" class="field">
                      <option v-for="pageId in selectedScheme?.pageIds" :key="pageId" :value="pageId">{{ workspace.pages.find((page) => page.id === pageId)?.name ?? pageId }}</option>
                    </select>
                    <select v-model="edge.toPageId" class="field">
                      <option v-for="pageId in selectedScheme?.pageIds" :key="pageId" :value="pageId">{{ workspace.pages.find((page) => page.id === pageId)?.name ?? pageId }}</option>
                    </select>
                    <input v-model="edge.trigger.displayName" class="field" />
                    <select v-model="edge.animation" class="field">
                      <option value="fade">渐入渐退</option>
                      <option value="slide">滑动</option>
                      <option value="none">无动画</option>
                    </select>
                    <button class="card-action danger" @click="removeSchemeEdge(index)">删除边</button>
                  </div>
                </div>
                </div>
              </aside>

              <div class="soft-card min-h-0 p-5">
                <div class="mb-4 flex items-center justify-between">
                  <h3 class="text-[13px] font-semibold">页面流程图</h3>
                  <p class="text-[11px] text-slate-500">拖拽节点可调整页面顺序</p>
                </div>
                <div class="scheme-flow-canvas" data-no-window-drag>
                  <svg class="absolute inset-0 h-full w-full" viewBox="0 0 100 100" preserveAspectRatio="none">
                    <defs>
                      <marker id="flow-arrow" markerWidth="6" markerHeight="6" refX="5" refY="3" orient="auto"><path d="M0,0 L6,3 L0,6 Z" fill="#0ea5e9" /></marker>
                      <marker id="flow-arrow-edge" markerWidth="6" markerHeight="6" refX="5" refY="3" orient="auto"><path d="M0,0 L6,3 L0,6 Z" fill="#8b5cf6" /></marker>
                    </defs>
                    <line v-for="(node, index) in schemeFlowNodes.slice(0, -1)" :key="`${node.pageId}-line`" :x1="node.x + 12" :y1="node.y + 6" :x2="schemeFlowNodes[index + 1].x" :y2="schemeFlowNodes[index + 1].y + 6" stroke="#0ea5e9" stroke-width="0.7" marker-end="url(#flow-arrow)" />
                    <line v-for="item in schemeFlowEdges" :key="`edge-${item.index}`" :x1="item.from.x + 12" :y1="item.from.y + 6" :x2="item.to.x + 12" :y2="item.to.y + 6" stroke="#8b5cf6" stroke-width="0.9" stroke-dasharray="2 1.2" marker-end="url(#flow-arrow-edge)" />
                  </svg>
                  <button v-for="node in schemeFlowNodes" :key="node.pageId" draggable="true" class="scheme-flow-node rounded-2xl border border-sky-300 bg-white p-3 text-left shadow-lg shadow-sky-950/8 dark:border-sky-800 dark:bg-slate-900" :style="{ left: `${node.x}%`, top: `${node.y}%` }" @dragstart="beginSchemePageDrag(node.index)" @dragover.prevent @drop="dropSchemePage(node.index)">
                    <Icon icon="solar:smartphone-bold-duotone" class="size-6 text-sky-500" />
                    <p class="mt-2 truncate text-[13px] font-semibold">{{ node.page?.name ?? node.pageId }}</p>
                    <p class="mt-1 text-[11px] text-slate-500">节点 {{ node.index + 1 }}</p>
                  </button>
                  <div v-for="item in schemeFlowEdges" :key="`edge-label-${item.index}`" class="absolute rounded-full bg-violet-500/90 px-2 py-0.5 text-[10px] font-medium text-white shadow" :style="{ left: `${(item.from.x + item.to.x) / 2 + 6}%`, top: `${(item.from.y + item.to.y) / 2 + 4}%`, transform: 'translate(-50%, -50%)' }">{{ item.edge.trigger.displayName }} · {{ item.edge.animation }}</div>
                  <div v-if="!schemeFlowNodes.length" class="grid h-full place-items-center text-[13px] text-slate-500">请先向方案添加页面</div>
                </div>
                <div class="mt-4 grid gap-2 text-[12px] text-slate-500">
                  <p>全局上一页：{{ selectedScheme?.globalPrevious.trigger.displayName }} / {{ selectedScheme?.globalPrevious.animation }}</p>
                  <p>全局下一页：{{ selectedScheme?.globalNext.trigger.displayName }} / {{ selectedScheme?.globalNext.animation }}</p>
                  <p v-for="edge in selectedScheme?.edges" :key="`${edge.fromPageId}-${edge.toPageId}`">{{ edge.fromPageId }} -> {{ edge.toPageId }}：{{ edge.trigger.displayName }} / {{ edge.animation }}</p>
                </div>
              </div>
            </div>
          </section>

<section v-else class="scrollable h-full overflow-auto" data-no-window-drag>
            <div v-if="activeView === 'plugin'" class="plugin-layout">
              <div class="soft-card scrollable grid content-start gap-3 overflow-auto p-4" data-no-window-drag>
                <button v-for="plugin in workspace.plugins" :key="plugin.id" class="rounded-2xl bg-white px-4 py-3 text-left text-[13px] shadow-sm dark:bg-slate-900" :class="selectedPlugin?.id === plugin.id ? 'ring-2 ring-sky-400' : ''" @click="selectedPluginId = plugin.id">
                  <div class="flex items-start justify-between gap-3"><div class="min-w-0"><p class="truncate font-semibold">{{ plugin.name }}</p><p class="mt-1 text-[12px] text-slate-500">{{ plugin.id }} · {{ plugin.version }}</p></div><span class="rounded-full bg-sky-50 px-2 py-1 text-[11px] text-sky-600 dark:bg-sky-950">已注册</span></div>
                  <p class="mt-2 text-[12px] text-slate-500">{{ plugin.persistent ? '允许常驻后台' : '按需调用' }} · {{ plugin.permissions.length }} 权限</p>
                  <span class="mt-3 inline-flex rounded-full bg-rose-50 px-3 py-1.5 text-[12px] font-medium text-rose-500 dark:bg-rose-950/40" @click.stop="requestDelete('plugin', plugin.id, plugin.name)">删除插件</span>
                </button>
                <div v-if="!workspace.plugins.length" class="rounded-2xl bg-white px-4 py-8 text-center text-[13px] text-slate-500 shadow-sm dark:bg-slate-900">暂无插件。导入插件包后，OneDesk 会读取插件清单、显示权限并注册后端进程。</div>
              </div>
              <div class="soft-card p-4">
                <template v-if="selectedPlugin">
                  <h3 class="text-[15px] font-semibold">{{ selectedPlugin.name }}</h3>
                  <p class="mt-1 text-[12px] text-slate-500">{{ selectedPlugin.id }} · {{ selectedPlugin.version }}</p>
                  <div class="mt-4 grid gap-2">
                    <div class="flex items-center justify-between"><h4 class="text-[13px] font-semibold">设置表单</h4><button class="rounded-full bg-sky-500 px-3 py-1.5 text-[12px] font-medium text-white" @click="savePluginSettings(selectedPlugin)">保存设置</button></div>
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
            <div v-else class="settings-layout">
              <aside class="soft-card flex flex-col gap-1 p-2 text-[13px]">
                <button class="menu-row" :class="settingsSection === 'general' ? 'bg-sky-50 text-sky-600 dark:bg-sky-950/40' : ''" @click="settingsSection = 'general'"><Icon icon="solar:tuning-2-bold-duotone" class="size-5" />通用</button>
                <button class="menu-row" :class="settingsSection === 'connection' ? 'bg-sky-50 text-sky-600 dark:bg-sky-950/40' : ''" @click="settingsSection = 'connection'"><Icon icon="solar:link-bold-duotone" class="size-5" />连接</button>
                <button class="menu-row" :class="settingsSection === 'permission' ? 'bg-sky-50 text-sky-600 dark:bg-sky-950/40' : ''" @click="settingsSection = 'permission'"><Icon icon="solar:shield-keyhole-bold-duotone" class="size-5" />权限管理</button>
                <button class="menu-row" :class="settingsSection === 'resources' ? 'bg-sky-50 text-sky-600 dark:bg-sky-950/40' : ''" @click="settingsSection = 'resources'"><Icon icon="solar:gallery-wide-bold-duotone" class="size-5" />资源管理器</button>
                <button class="menu-row" :class="settingsSection === 'plugins' ? 'bg-sky-50 text-sky-600 dark:bg-sky-950/40' : ''" @click="settingsSection = 'plugins'"><Icon icon="solar:plug-circle-bold-duotone" class="size-5" />插件</button>
                <button class="menu-row" :class="settingsSection === 'logs' ? 'bg-sky-50 text-sky-600 dark:bg-sky-950/40' : ''" @click="settingsSection = 'logs'"><Icon icon="solar:document-text-bold-duotone" class="size-5" />日志</button>
              </aside>
              <section class="soft-card scrollable overflow-auto p-4" data-no-window-drag>
                <div v-if="settingsSection === 'general'" class="grid max-w-[560px] gap-3 text-[13px]">
                  <label class="flex items-center justify-between rounded-2xl bg-slate-50 px-4 py-3 dark:bg-slate-950">
                    <span>
                      <span class="block font-semibold">开机启动</span>
                      <span class="mt-1 block text-[12px] text-slate-500">系统登录后自动启动 OneDesk 桌面端</span>
                    </span>
                    <input v-model="enableStartup" type="checkbox" class="size-4 accent-sky-500" />
                  </label>
                  <label class="field-label">界面语言<input class="field" value="简体中文" disabled /></label>
                  <label class="field-label">主题模式<input class="field" :value="theme === 'system' ? '跟随系统' : theme === 'dark' ? '深色' : '浅色'" disabled /></label>
                </div>
                <div v-else-if="settingsSection === 'connection'" class="grid max-w-[560px] gap-3 text-[13px]">
                  <label class="field-label">桌面监听端口<input v-model.number="connectionPort" type="number" min="1024" max="65535" class="field" /></label>
                  <button class="w-fit rounded-full bg-sky-500 px-4 py-2 text-[12px] font-medium text-white" @click="openDeviceDialog(true)">打开设备管理</button>
                  <p class="text-[12px] text-slate-500">端口修改会影响移动端连接信息；当前网关状态仍以壳子返回值为准。</p>
                </div>
                <div v-else-if="settingsSection === 'permission'" class="grid gap-2">
                  <div class="rounded-2xl bg-slate-50 px-4 py-3 text-[13px] dark:bg-slate-950">
                    <label class="field-label max-w-[420px]">授权对象
                      <select class="field" :value="`${permissionSourceKind}:${permissionSourceId}`" @change="(() => { const value = ($event.target as HTMLSelectElement).value; const splitAt = value.indexOf(':'); const kind = value.slice(0, splitAt); const id = value.slice(splitAt + 1); permissionSourceKind = kind as 'component' | 'plugin'; permissionSourceId = id; })">
                        <option v-for="option in permissionSourceOptions" :key="`${option.kind}:${option.id}`" :value="`${option.kind}:${option.id}`">{{ option.label }}</option>
                      </select>
                    </label>
                    <p class="mt-2 text-[12px] text-slate-500">当前：{{ permissionSourceLabel }} · {{ permissionSourceKey }} · 大类授权会覆盖全部小类，小类授权只开放单项能力。</p>
                  </div>
                  <div v-for="item in permissionRows" :key="item.id" class="rounded-2xl bg-slate-50 px-4 py-3 text-[13px] dark:bg-slate-950">
                    <div class="flex items-center justify-between gap-4">
                      <div class="min-w-0">
                        <p class="truncate font-semibold">{{ item.name }}</p>
                        <p class="mt-1 truncate text-[12px] text-slate-500">{{ item.categoryName }} · {{ item.id }}</p>
                      </div>
                      <div class="flex items-center gap-2">
                        <span :class="item.highRisk ? 'text-rose-500' : 'text-sky-600'">{{ item.highRisk ? '高危' : '普通' }}</span>
                        <button class="rounded-full px-3 py-1.5 text-[12px] font-medium" :class="selectedGrants.includes(item.id) ? 'bg-sky-500 text-white' : 'bg-slate-100 text-slate-600 dark:bg-slate-800 dark:text-slate-300'" @click="togglePermission(item.id)">{{ selectedGrants.includes(item.id) ? '已授权' : '授权' }}</button>
                      </div>
                    </div>
                  </div>
                </div>
                <div v-else-if="settingsSection === 'resources'" class="grid gap-3 text-[13px]">
                  <div class="flex items-center justify-between gap-3 rounded-2xl bg-slate-50 px-4 py-3 dark:bg-slate-950">
                    <div>
                      <p class="font-semibold">媒体资源</p>
                      <p class="mt-1 text-[12px] text-slate-500">图片和视频先进入资源管理器，再复制到组件或页面目录中使用。</p>
                    </div>
                    <button class="header-primary-button" @click="addMediaResource">添加资源</button>
                  </div>
                  <div class="resource-grid">
                    <article v-for="resource in workspace.resources" :key="resource.id" class="resource-card">
                      <div class="resource-thumb">
                        <img v-if="resource.kind === 'image'" :src="resource.fileUri" alt="" class="h-full w-full object-cover" />
                        <Icon v-else icon="solar:video-frame-play-horizontal-bold-duotone" class="size-8 text-sky-500" />
                      </div>
                      <div class="min-w-0 flex-1">
                        <p class="truncate font-semibold">{{ resource.name }}</p>
                        <p class="mt-1 truncate text-[11px] text-slate-500">{{ resource.kind }} · {{ resource.id }}</p>
                        <p class="mt-1 text-[11px] text-slate-500">{{ Math.max(1, Math.round(resource.sizeBytes / 1024)) }} KB</p>
                      </div>
                       <button class="card-action danger w-[72px]" @click="deleteMediaResource(resource)">删除</button>
                    </article>
                    <div v-if="!workspace.resources.length" class="rounded-2xl bg-slate-50 px-4 py-8 text-center text-[13px] text-slate-500 dark:bg-slate-950">暂无资源。点击添加资源导入图片或视频。</div>
                  </div>
                </div>
                <div v-else-if="settingsSection === 'plugins'" class="grid gap-3 text-[13px]"><p class="rounded-2xl bg-slate-50 px-4 py-3 dark:bg-slate-950">已安装插件：{{ workspace.plugins.length }} 个。插件导入、设置和权限仍在插件页面管理。</p><button class="w-fit rounded-full bg-sky-500 px-4 py-2 text-[12px] font-medium text-white" @click="activeView = 'plugin'">前往插件</button></div>
                <div v-else class="grid gap-2 text-[13px]"><div v-for="(log, index) in workspace.logs.slice(0, 20)" :key="index" class="rounded-2xl bg-slate-50 px-4 py-3 text-[12px] dark:bg-slate-950">{{ JSON.stringify(log) }}</div><p v-if="!workspace.logs.length" class="rounded-2xl bg-slate-50 px-4 py-3 text-slate-500 dark:bg-slate-950">暂无日志。</p></div>
              </section>
            </div>
          </section>
        </div>

        <footer class="h-3 shrink-0"><div v-if="exporting" class="mt-1 h-1.5 overflow-hidden rounded-full bg-white dark:bg-slate-900"><div class="h-full rounded-full bg-sky-500 transition-all" :style="{ width: `${exportProgress}%` }"></div></div></footer>
      </section>
    </section>

    <div v-if="showResourcePicker" class="fixed inset-0 z-40 grid place-items-center bg-slate-950/30 p-6 backdrop-blur-sm">
      <div class="modal-panel w-full max-w-[620px] rounded-[28px] bg-white p-5 shadow-2xl dark:bg-slate-950">
        <div class="flex items-center justify-between gap-3">
          <div>
            <h3 class="text-[16px] font-semibold">{{ resourcePickerTitle }}</h3>
            <p class="mt-1 text-[12px] text-slate-500">当前只显示{{ resourcePickerKind === 'video' ? '视频' : '图片' }}资源；选择后会复制到当前{{ resourcePickerTarget === 'component-background' ? '组件' : '页面' }}目录。</p>
          </div>
          <button class="grid size-8 place-items-center rounded-full bg-slate-100 dark:bg-slate-900" @click="showResourcePicker = false"><Icon icon="solar:close-circle-bold-duotone" class="size-5" /></button>
        </div>
        <div class="mt-4 flex justify-end">
          <button class="header-primary-button" @click="addMediaResource">添加资源</button>
        </div>
        <div class="scrollable mt-4 grid max-h-[420px] gap-3 overflow-auto pr-2" data-no-window-drag>
          <button v-for="resource in resourcePickerItems" :key="resource.id" class="resource-card text-left" @click="chooseMediaResource(resource)">
            <div class="resource-thumb">
              <img v-if="resource.kind === 'image'" :src="resource.fileUri" alt="" class="h-full w-full object-cover" />
              <Icon v-else icon="solar:video-frame-play-horizontal-bold-duotone" class="size-8 text-sky-500" />
            </div>
            <div class="min-w-0 flex-1">
              <p class="truncate font-semibold">{{ resource.name }}</p>
              <p class="mt-1 truncate text-[11px] text-slate-500">{{ resource.id }}</p>
              <p class="mt-1 text-[11px] text-slate-500">{{ resource.extension }} · {{ Math.max(1, Math.round(resource.sizeBytes / 1024)) }} KB</p>
            </div>
            <span class="rounded-full bg-sky-50 px-3 py-1.5 text-[12px] font-medium text-sky-600 dark:bg-sky-950/50">选择</span>
          </button>
          <div v-if="!resourcePickerItems.length" class="rounded-2xl bg-slate-50 px-4 py-8 text-center text-[13px] text-slate-500 dark:bg-slate-900">暂无可用资源，请先添加{{ resourcePickerKind === 'video' ? '视频' : '图片' }}资源。</div>
        </div>
      </div>
    </div>

    <div v-if="showDeviceDialog" class="fixed inset-0 z-40 grid place-items-center bg-slate-950/28 p-6 backdrop-blur-sm">
      <div class="max-h-[calc(100vh-48px)] w-full max-w-[760px] overflow-auto rounded-3xl bg-white p-5 shadow-2xl dark:bg-slate-950" data-no-window-drag>
        <div class="flex items-start justify-between gap-4">
          <div>
            <h3 class="text-[16px] font-semibold">设备管理</h3>
            <p class="mt-1 text-[12px] text-slate-500">手机端输入本机局域网 IP、端口和验证码后连接桌面端。</p>
          </div>
          <button class="grid size-8 place-items-center rounded-full bg-slate-100 dark:bg-slate-900" @click="showDeviceDialog = false">
            <Icon icon="solar:close-circle-bold-duotone" class="size-5" />
          </button>
        </div>

        <div class="mt-5 grid gap-4 md:grid-cols-[1.2fr_0.9fr]">
          <section class="rounded-[22px] bg-slate-50 p-4 dark:bg-slate-900">
            <h4 class="text-[13px] font-semibold">移动设备</h4>
            <div class="mt-3 grid gap-2">
              <div v-for="device in trustedDevices" :key="device.deviceId" class="rounded-2xl bg-white p-3 text-[12px] dark:bg-slate-950">
                <div class="flex items-center justify-between">
                  <span class="font-semibold">{{ device.remark || device.displayName }}</span>
                  <span :class="currentDevice?.deviceId === device.deviceId ? 'text-sky-600' : 'text-green-600'">{{ currentDevice?.deviceId === device.deviceId ? '当前预览' : '已信任' }}</span>
                </div>
                <p class="mt-1 truncate text-slate-500">{{ device.deviceId }} · {{ new Date(device.createdAt).toLocaleString() }}</p>
                <div class="mt-3 flex gap-2">
                  <input v-model="deviceRemarkDraft[device.deviceId]" class="field min-w-0 flex-1" :placeholder="device.remark || '设备备注'" />
                  <button class="rounded-xl bg-slate-100 px-3 text-sky-600 dark:bg-slate-800" @click="selectedDeviceId = device.deviceId">设为当前</button>
                  <button class="rounded-xl bg-sky-500 px-3 text-white" @click="renameDevice(device)">保存</button>
                </div>
              </div>
              <div v-if="!trustedDevices.length" class="rounded-2xl bg-white p-4 text-[12px] leading-6 text-slate-500 dark:bg-slate-950">暂无移动设备。请在手机端打开 OneDesk，输入右侧本机 IP、端口和验证码建立首次信任。</div>
            </div>

            <h4 class="mt-4 text-[13px] font-semibold">在线连接</h4>
            <div class="mt-3 grid gap-2">
              <div v-for="peer in workspace.gatewayStatus?.peers" :key="peer.deviceId" class="rounded-2xl bg-white p-3 text-[12px] dark:bg-slate-950">
                <div class="flex items-center justify-between">
                  <span class="font-semibold">{{ peer.deviceId }}</span>
                  <span :class="peer.online ? 'text-green-600' : 'text-slate-400'">{{ peer.online ? '在线' : '离线' }}</span>
                </div>
                <p class="mt-1 text-slate-500">{{ peer.endpoint }}</p>
              </div>
              <div v-if="!workspace.gatewayStatus?.peers.length" class="rounded-2xl bg-white p-3 text-[12px] text-slate-500 dark:bg-slate-950">暂无在线移动端</div>
            </div>
          </section>

          <section class="rounded-[22px] bg-slate-50 p-4 dark:bg-slate-900">
            <h4 class="text-[13px] font-semibold">本机连接信息</h4>
            <div class="mt-3 grid gap-2 text-[12px]">
              <div class="rounded-2xl bg-white p-3 dark:bg-slate-950">
                <p class="text-slate-500">局域网 IP</p>
                <p class="mt-1 text-[18px] font-semibold text-sky-500">{{ localPairingHost }}</p>
                <p class="mt-2 text-[11px] text-slate-500">可用 IP：{{ pairing?.localIps?.join(' / ') || workspace.deviceStatus?.localIps?.join(' / ') || '未检测到' }}</p>
              </div>
              <div class="rounded-2xl bg-white p-3 dark:bg-slate-950">
                <p class="text-slate-500">端口</p>
                <p class="mt-1 text-[18px] font-semibold">{{ pairing?.port ?? workspace.gatewayStatus?.port ?? 48320 }}</p>
              </div>
              <div class="rounded-2xl bg-white p-4 text-center dark:bg-slate-950">
                <p class="text-[28px] font-semibold tracking-[0.3em] text-sky-500">{{ pairing?.code ?? "------" }}</p>
                <p class="mt-1 text-[11px] text-slate-500">验证码 5 分钟内有效，只用于首次换取长期信任凭据。</p>
              </div>
              <div class="rounded-2xl bg-white p-3 text-center dark:bg-slate-950">
                <p class="mb-2 text-left text-[12px] font-semibold">扫码连接</p>
                <img v-if="pairingQrDataUrl" :src="pairingQrDataUrl" alt="OneDesk 配对二维码" class="mx-auto size-[168px] rounded-2xl bg-white p-2" />
                <p v-else class="rounded-2xl bg-slate-50 p-6 text-[12px] text-slate-500 dark:bg-slate-900">点击生成验证码后显示二维码</p>
                <p class="mt-2 break-all text-left text-[11px] leading-5 text-slate-500">{{ pairing?.qrPayload ?? "" }}</p>
              </div>
              <button class="rounded-2xl bg-sky-500 py-2.5 text-[13px] font-medium text-white" @click="openDeviceDialog(true)">生成验证码</button>
            </div>
          </section>
        </div>
      </div>
    </div>
    <div v-if="showActionDesignerDialog && actionDraft" class="fixed inset-0 z-40 grid place-items-center bg-slate-950/28 p-6 backdrop-blur-sm">
      <div class="grid h-[min(720px,calc(100vh-64px))] w-full max-w-[880px] grid-cols-[260px_1fr] overflow-hidden rounded-3xl bg-white shadow-2xl dark:bg-slate-950" data-no-window-drag>
        <aside class="min-h-0 border-r border-slate-200 bg-slate-50 p-5 dark:border-slate-800 dark:bg-slate-900">
          <div class="flex items-center justify-between gap-3">
            <div>
              <h3 class="text-[16px] font-semibold">动作流程</h3>
              <p class="mt-1 text-[12px] text-slate-500">按顺序设计触发与执行内容。</p>
            </div>
          </div>
          <div class="mt-5 grid gap-3">
            <div class="action-designer-step">
              <span>1</span>
              <div>
                <p>触发</p>
                <b>{{ actionDraftTriggerLabel }}</b>
              </div>
            </div>
            <div v-for="(invocation, index) in actionDraft.invocations" :key="index" class="action-designer-step">
              <span>{{ index + 2 }}</span>
              <div>
                <p>动作</p>
                <b>调用 JSAPI · {{ invocation.capability || "未设置" }}</b>
              </div>
            </div>
            <button class="action-add-step-button" type="button" @click="addActionDraftInvocation">
              <Icon icon="solar:add-circle-bold-duotone" class="size-4" />增加下一步动作
            </button>
          </div>
        </aside>
        <section class="scrollable h-full min-h-0 overflow-auto p-5">
          <div class="flex items-start justify-between gap-3">
            <div>
              <h3 class="text-[16px] font-semibold">动作配置</h3>
              <p class="mt-1 text-[12px] text-slate-500">同一组件内触发必须唯一，保存后动作会绑定到当前组件。</p>
            </div>
            <div class="flex shrink-0 items-center gap-2">
              <span class="text-[16px] font-semibold">保存</span>
              <button class="grid size-8 place-items-center rounded-full bg-sky-500 text-white shadow-md shadow-sky-500/16" @click="saveActionDesigner">
                <Icon icon="solar:diskette-bold-duotone" class="size-5" />
              </button>
              <button class="grid size-8 place-items-center rounded-full bg-slate-100 text-slate-500 dark:bg-slate-800" @click="showActionDesignerDialog = false">
                <Icon icon="solar:close-circle-bold-duotone" class="size-5" />
              </button>
            </div>
          </div>
          <div class="mt-5 grid gap-5">
            <section class="editor-section-card">
              <div class="editor-section-head"><h3>基础信息</h3><p>用于在组件动作列表中识别这条动作。</p></div>
              <label class="field-label"><span>动作名称</span><input v-model="actionDraft.name" class="field" placeholder="动作名称" /></label>
            </section>
            <section class="editor-section-card">
              <div class="editor-section-head"><h3>触发</h3><p>一个动作只能拥有一个触发。</p></div>
              <label class="field-label">
                <span>触发方式</span>
                <select class="field" :value="actionDraft.trigger.id" @change="changeActionDraftTrigger(($event.target as HTMLSelectElement).value)">
                  <optgroup v-for="group in triggerCatalog" :key="group.category" :label="group.label">
                    <option v-for="trigger in group.triggers" :key="trigger.id" :value="trigger.id">{{ trigger.displayName }}</option>
                  </optgroup>
                </select>
              </label>
            </section>
            <section v-for="(invocation, index) in actionDraft.invocations" :key="index" class="editor-section-card">
              <div class="editor-section-head editor-section-head-row"><div><h3>动作 {{ index + 1 }}</h3><p>执行内容为调用 JSAPI，可继续追加下一步动作。</p></div><button v-if="actionDraft.invocations.length > 1" class="editor-icon-button" type="button" @click="removeActionDraftInvocation(index)"><Icon icon="solar:trash-bin-trash-bold-duotone" class="size-[16px]" /></button></div>
              <div class="editor-form-row">
                <label class="field-label editor-field-grow"><span>目标设备 ID</span><input v-model="invocation.targetDeviceId" class="field" placeholder="desktop 或设备 ID" /></label>
                <label class="field-label editor-field-grow"><span>JSAPI 能力</span><input v-model="invocation.capability" class="field" list="action-capability-list" placeholder="notification.native" /></label>
              </div>
              <label class="field-label mt-3"><span>参数 JSON</span><textarea v-model="actionDraftParametersText[index]" class="field min-h-[132px] resize-none font-mono text-[12px]" spellcheck="false"></textarea></label>
            </section>
            <button class="action-add-step-button action-add-step-button-wide" type="button" @click="addActionDraftInvocation">
              <Icon icon="solar:add-circle-bold-duotone" class="size-4" />增加下一步动作
            </button>
            <datalist id="action-capability-list">
              <option v-for="capability in permissionRows" :key="capability.id" :value="capability.id">{{ capability.name }} · {{ capability.description }}</option>
            </datalist>
          </div>
        </section>
      </div>
    </div>
    <div v-if="pendingEditorLeave" class="fixed inset-0 z-50 grid place-items-center bg-slate-950/30 p-6 backdrop-blur-sm">
      <div class="w-full max-w-[420px] rounded-3xl bg-white p-5 shadow-2xl dark:bg-slate-950" data-no-window-drag>
        <Icon icon="solar:diskette-bold-duotone" class="size-9 text-sky-500" />
        <h3 class="mt-3 text-[16px] font-semibold">{{ pendingEditorLeave.title }}</h3>
        <p class="mt-2 text-[13px] leading-6 text-slate-500">检测到当前编辑内容与上一次保存状态不同。离开前可以保存，也可以放弃这次修改。</p>
        <div class="mt-4 grid grid-cols-3 gap-2">
          <button class="rounded-2xl bg-sky-500 py-2.5 text-[13px] font-medium text-white" @click="confirmEditorLeave(true)">保存</button>
          <button class="rounded-2xl bg-slate-100 py-2.5 text-[13px] font-medium text-rose-500 dark:bg-slate-900" @click="confirmEditorLeave(false)">不保存</button>
          <button class="rounded-2xl bg-slate-100 py-2.5 text-[13px] font-medium dark:bg-slate-900" @click="cancelEditorLeave">取消</button>
        </div>
      </div>
    </div>
    <div v-if="showPermissionDialog" class="fixed inset-0 z-40 grid place-items-center bg-slate-950/28 p-6 backdrop-blur-sm">
      <div class="w-full max-w-[520px] rounded-3xl bg-white p-5 shadow-2xl dark:bg-slate-950">
        <div class="flex items-center justify-between">
          <div>
            <h3 class="text-[16px] font-semibold">确认授权</h3>
            <p v-if="pendingInspection" class="mt-1 truncate text-[12px] text-slate-500">{{ pendingInspection.name }} · {{ pendingInspection.kind }}</p>
          </div>
          <button class="grid size-8 place-items-center rounded-full bg-slate-100 dark:bg-slate-900" @click="pendingImportKind = null; pendingPluginImport = false; pendingInspection = null; showPermissionDialog = false">
            <Icon icon="solar:close-circle-bold-duotone" class="size-5" />
          </button>
        </div>
        <p class="mt-3 text-[12px] leading-5 text-slate-500">导入或安装前会按照能力目录授权，默认同意；高危权限会明确标记，后续可在设置里修改。</p>
        <div v-if="pendingInspection?.pluginDependencies.length" class="mt-3 rounded-2xl bg-amber-50 px-3 py-2 text-[12px] text-amber-700 dark:bg-amber-950/40 dark:text-amber-200">依赖插件：{{ pendingInspection.pluginDependencies.map((item) => `${item.id}@${item.version}`).join(' / ') }}</div>
        <div class="mt-4 grid max-h-[300px] gap-2 overflow-auto pr-1" data-no-window-drag>
          <label v-for="permission in importPermissionRows" :key="permission.capability" class="flex items-center gap-3 rounded-2xl bg-slate-50 px-3 py-2.5 text-[13px] dark:bg-slate-900">
            <input type="checkbox" :checked="grantedImportCapabilities.includes(permission.capability)" class="size-4 accent-sky-500" @change="toggleImportCapability(permission.capability)" />
            <span class="min-w-0 flex-1">
              <span class="block font-medium">{{ permission.capability }}</span>
              <span class="mt-0.5 block text-[12px] text-slate-500">{{ permission.description }}</span>
            </span>
            <span v-if="permission.highRisk" class="rounded-full bg-rose-100 px-2 py-1 text-[11px] font-medium text-rose-600 dark:bg-rose-950 dark:text-rose-300">高危</span>
          </label>
          <div v-if="!importPermissionRows.length" class="rounded-2xl bg-slate-50 px-3 py-2.5 text-[13px] text-slate-500 dark:bg-slate-900">当前对象没有声明额外权限。</div>
        </div>
        <button class="mt-4 w-full rounded-2xl bg-sky-500 py-2.5 text-[13px] font-medium text-white" @click="confirmPermissionDialog">{{ pendingImportKind || pendingPluginImport ? '授权并导入' : '确认授权' }}</button>
      </div>
    </div>
    <div v-if="showCodeSwitchDialog" class="fixed inset-0 z-40 grid place-items-center bg-slate-950/28 p-6 backdrop-blur-sm">
      <div class="w-full max-w-[420px] rounded-3xl bg-white p-5 shadow-2xl dark:bg-slate-950"><Icon icon="solar:danger-triangle-bold-duotone" class="size-9 text-amber-500" /><h3 class="mt-3 text-[16px] font-semibold">切换到代码编辑？</h3><p class="mt-2 text-[13px] leading-6 text-slate-500">切换后无法回到可视化编辑，因为任意 Vue 代码无法完整还原为可视化配置。</p><div class="mt-4 flex gap-2"><button class="flex-1 rounded-2xl bg-slate-100 py-2.5 text-[13px] font-medium dark:bg-slate-900" @click="showCodeSwitchDialog = false">取消</button><button class="flex-1 rounded-2xl bg-sky-500 py-2.5 text-[13px] font-medium text-white" @click="confirmCodeMode">继续</button></div></div>
    </div>

    <div v-if="pendingDelete" class="fixed inset-0 z-50 grid place-items-center bg-slate-950/30 p-6 backdrop-blur-sm">
      <div class="modal-panel w-full max-w-[420px] rounded-3xl bg-white p-5 shadow-2xl dark:bg-slate-950">
        <Icon icon="solar:trash-bin-trash-bold-duotone" class="size-9 text-rose-500" />
        <h3 class="mt-3 text-[16px] font-semibold">纭鍒犻櫎</h3>
        <p class="mt-2 text-[13px] leading-6 text-slate-500">即将删除「{{ pendingDelete.name }}」。删除后相关引用可能失效，请确认后继续。</p>
        <div class="mt-4 flex gap-2">
          <button class="flex-1 rounded-2xl bg-slate-100 py-2.5 text-[13px] font-medium dark:bg-slate-900" @click="pendingDelete = null">取消</button>
          <button class="flex-1 rounded-2xl bg-rose-500 py-2.5 text-[13px] font-medium text-white" @click="performDelete">纭鍒犻櫎</button>
        </div>
      </div>
    </div>

    <div v-if="toasts.length" class="toast-stack">
      <div v-for="toast in toasts" :key="toast.id" class="toast-panel flex items-center gap-2 rounded-2xl bg-white px-4 py-3 text-[12px] font-medium text-slate-800 shadow-2xl shadow-slate-950/20 dark:bg-slate-950 dark:text-white">
        <Icon icon="solar:check-circle-bold-duotone" class="size-4 text-sky-400" />
        <span>{{ toast.message }}</span>
      </div>
    </div>
  </main>
</template>
