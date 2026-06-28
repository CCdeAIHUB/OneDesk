import { reactive } from "vue";
import { sendShell } from "./nativeBridge";
import type {
  ActionDefinition,
  CapabilityCategory,
  ComponentDefinition,
  DeviceIdentity,
  DeviceStatusSnapshot,
  GatewayStatus,
  NavigationItem,
  PageDefinition,
  PermissionListSnapshot,
  PluginManifest,
  QuickAction,
  QuickStartItem,
  SchemeDefinition,
  SchemeCacheManifest,
  WorkspaceSnapshot,
} from "./domain";

export const navItems: NavigationItem[] = [
  { key: "home", label: "首页", icon: "solar:widget-2-bold-duotone" },
  { key: "component", label: "组件", icon: "solar:card-bold-duotone" },
  { key: "page", label: "页面", icon: "solar:layers-bold-duotone" },
  { key: "scheme", label: "方案", icon: "solar:play-circle-bold-duotone" },
  { key: "plugin", label: "插件", icon: "solar:plug-circle-bold-duotone" },
  { key: "permission", label: "设置", icon: "solar:settings-bold-duotone" },
];

export const workspace = reactive({
  selectedDevice: "未选择移动设备",
  toast: "工作区已就绪",
  selectedComponentId: "component-scene-switch",
  selectedPageId: "page-capture",
  selectedSchemeId: "scheme-live-console",
  components: [] as ComponentDefinition[],
  actions: [] as ActionDefinition[],
  pages: [] as PageDefinition[],
  schemes: [] as SchemeDefinition[],
  devices: [] as DeviceIdentity[],
  capabilities: [] as CapabilityCategory[],
  logs: [] as unknown[],
  plugins: [] as PluginManifest[],
  permissionGrants: [] as PermissionListSnapshot["grants"],
  deviceStatus: null as DeviceStatusSnapshot | null,
  gatewayStatus: null as GatewayStatus | null,
  cacheManifest: null as SchemeCacheManifest | null,
  activeSchemeId: "",
  loading: false,
});

export const quickActions: QuickAction[] = [
  { label: "创建新方案", icon: "solar:add-circle-bold-duotone", color: "text-sky-500" },
  { label: "导入方案", icon: "solar:download-minimalistic-bold-duotone", color: "text-green-500" },
  { label: "打开动作编辑器", icon: "solar:bolt-bold-duotone", color: "text-violet-500" },
];

export const quickStart: QuickStartItem[] = [
  { label: "显示连接码", desc: "让手机端连接本机桌面端", icon: "solar:qr-code-bold-duotone", color: "text-sky-500" },
  { label: "浏览插件", desc: "扩展你的 OneDesk 能力", icon: "solar:plug-circle-bold-duotone", color: "text-green-500" },
  { label: "使用帮助", desc: "查看使用文档和教程", icon: "solar:question-circle-bold-duotone", color: "text-violet-500" },
];

export async function loadWorkspace(): Promise<void> {
  workspace.loading = true;
  try {
    const [workspaceResponse, capabilityResponse, logResponse, permissionResponse, deviceResponse, gatewayResponse, cacheResponse, pluginResponse] = await Promise.all([
      sendShell<WorkspaceSnapshot>("workspace.list"),
      sendShell<CapabilityCategory[]>("capability.list"),
      sendShell<unknown[]>("log.list"),
      sendShell<PermissionListSnapshot>("permission.list"),
      sendShell<DeviceStatusSnapshot>("device.status"),
      sendShell<GatewayStatus>("gateway.status"),
      sendShell<SchemeCacheManifest | null>("scheme.cacheManifest"),
      sendShell<PluginManifest[]>("plugin.list"),
    ]);

    if (workspaceResponse.ok && workspaceResponse.payload) {
      workspace.components = workspaceResponse.payload.components;
      workspace.actions = workspaceResponse.payload.actions;
      workspace.pages = workspaceResponse.payload.pages;
      workspace.schemes = workspaceResponse.payload.schemes;
      workspace.devices = workspaceResponse.payload.devices.filter((device) => String(device.kind).toLowerCase() !== "desktop" && !device.deviceId.startsWith("desktop-"));
      workspace.activeSchemeId = workspaceResponse.payload.activeScheme?.schemeId ?? "";
      workspace.selectedComponentId = workspace.components[0]?.id ?? "";
      workspace.selectedPageId = workspace.pages[0]?.id ?? "";
      workspace.selectedSchemeId = workspace.schemes[0]?.id ?? "";
      workspace.selectedDevice = workspace.devices[0]?.displayName ?? "未选择移动设备";
    } else {
      applyPreviewData();
    }

    if (capabilityResponse.ok && capabilityResponse.payload) {
      workspace.capabilities = capabilityResponse.payload;
    }

    if (logResponse.ok && logResponse.payload) {
      workspace.logs = logResponse.payload;
    }

    if (permissionResponse.ok && permissionResponse.payload) {
      workspace.permissionGrants = permissionResponse.payload.grants;
    }

    if (deviceResponse.ok && deviceResponse.payload) {
      workspace.deviceStatus = deviceResponse.payload;
    }

    if (gatewayResponse.ok && gatewayResponse.payload) {
      workspace.gatewayStatus = gatewayResponse.payload;
    }

    if (cacheResponse.ok) {
      workspace.cacheManifest = cacheResponse.payload ?? null;
    }

    if (pluginResponse.ok && pluginResponse.payload) {
      workspace.plugins = pluginResponse.payload;
    }
  } finally {
    workspace.loading = false;
  }
}

export async function applyScheme(schemeId: string, deviceId?: string): Promise<void> {
  const response = await sendShell<{ schemeId: string; deviceId?: string | null }>("workspace.applyScheme", { id: schemeId, deviceId });
  if (response.ok) {
    workspace.activeSchemeId = response.payload?.schemeId ?? schemeId;
    workspace.toast = "方案已应用到设备";
  } else {
    workspace.toast = response.message ?? "方案应用失败";
  }
}

function applyPreviewData(): void {
  workspace.components = [
    {
      id: "component-scene-switch",
      name: "场景切换",
      version: "1.0.0",
      editMode: "visual",
      entryFile: "src/SceneSwitch.vue",
      visualConfigFile: "onedesk.visual.json",
      actionIds: ["action-switch-scene"],
      requestedPermissions: [{ category: "plugin", capability: "plugin.invoke", highRisk: false, description: "调用桌面端插件方法" }],
      pluginDependencies: [{ id: "cc.onedesk.example.obs", version: "1.0.0", kind: "plugin" }],
    },
    {
      id: "component-volume-strip",
      name: "音量推子",
      version: "1.0.0",
      editMode: "code",
      entryFile: "src/VolumeStrip.vue",
      visualConfigFile: null,
      actionIds: [],
      requestedPermissions: [{ category: "input", capability: "input.keyboardMouseSimulation", highRisk: true, description: "模拟键盘快捷键调整音量" }],
      pluginDependencies: [],
    },
  ];
  workspace.actions = [
    {
      id: "action-switch-scene",
      name: "切换直播场景",
      trigger: { id: "three-finger-swipe-up", category: "touch.standard", displayName: "三指上滑", fingerCount: 3 },
      invocations: [{ targetDeviceId: "desktop", capability: "plugin.invoke", parameters: { pluginId: "cc.onedesk.example.obs" } }],
    },
  ];
  workspace.pages = [
    {
      id: "page-capture",
      name: "采集",
      rows: 4,
      columns: 3,
      spacing: { padding: 16, rowGap: 10, columnGap: 10 },
      backgroundKind: "gradient",
      backgroundValue: "sky",
      cells: [],
    },
  ];
  workspace.schemes = [
    {
      id: "scheme-live-console",
      name: "直播控制台",
      version: "1.0.0",
      pageIds: ["page-capture"],
      globalPrevious: { trigger: { id: "three-finger-swipe-down", category: "touch.standard", displayName: "三指下滑", fingerCount: 3 }, animation: "fade" },
      globalNext: { trigger: { id: "three-finger-swipe-up", category: "touch.standard", displayName: "三指上滑", fingerCount: 3 }, animation: "fade" },
      edges: [],
      pluginDependencies: [],
    },
  ];
}
