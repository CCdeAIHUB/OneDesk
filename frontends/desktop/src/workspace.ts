import { reactive } from "vue";
import { sendShell } from "./nativeBridge";
import type {
  ActionDefinition,
  CapabilityCategory,
  ComponentDefinition,
  DeviceIdentity,
  DeviceStatusSnapshot,
  GatewayStatus,
  MediaResourceDefinition,
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
  resources: [] as MediaResourceDefinition[],
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
  { label: "连接设备", icon: "solar:devices-bold-duotone", color: "text-violet-500" },
];

export const quickStart: QuickStartItem[] = [
  { label: "显示连接码", desc: "让手机端连接本机桌面端", icon: "solar:qr-code-bold-duotone", color: "text-sky-500" },
  { label: "浏览插件", desc: "扩展你的 OneDesk 能力", icon: "solar:plug-circle-bold-duotone", color: "text-green-500" },
  { label: "使用帮助", desc: "查看使用文档和教程", icon: "solar:question-circle-bold-duotone", color: "text-violet-500" },
];

export async function loadWorkspace(options?: {
  preserveSelection?: boolean;
  selectedComponentId?: string;
  selectedPageId?: string;
  selectedSchemeId?: string;
}): Promise<void> {
  workspace.loading = true;
  try {
    const [workspaceResponse, capabilityResponse, logResponse, permissionResponse, deviceResponse, gatewayResponse, cacheResponse, pluginResponse, resourceResponse] = await Promise.all([
      sendShell<WorkspaceSnapshot>("workspace.list"),
      sendShell<CapabilityCategory[]>("capability.list"),
      sendShell<unknown[]>("log.list"),
      sendShell<PermissionListSnapshot>("permission.list"),
      sendShell<DeviceStatusSnapshot>("device.status"),
      sendShell<GatewayStatus>("gateway.status"),
      sendShell<SchemeCacheManifest | null>("scheme.cacheManifest"),
      sendShell<PluginManifest[]>("plugin.list"),
      sendShell<MediaResourceDefinition[]>("resource.list"),
    ]);

    if (workspaceResponse.ok && workspaceResponse.payload) {
      workspace.components = workspaceResponse.payload.components;
      workspace.actions = workspaceResponse.payload.actions;
      workspace.pages = workspaceResponse.payload.pages;
      workspace.schemes = workspaceResponse.payload.schemes;
      workspace.devices = workspaceResponse.payload.devices.filter((device) => String(device.kind).toLowerCase() !== "desktop" && !device.deviceId.startsWith("desktop-"));
      workspace.activeSchemeId = workspaceResponse.payload.activeScheme?.schemeId ?? "";
      const nextComponentId = options?.selectedComponentId ?? workspace.selectedComponentId;
      const nextPageId = options?.selectedPageId ?? workspace.selectedPageId;
      const nextSchemeId = options?.selectedSchemeId ?? workspace.selectedSchemeId;
      workspace.selectedComponentId = options?.preserveSelection && workspace.components.some((item) => item.id === nextComponentId)
        ? nextComponentId
        : workspace.components[0]?.id ?? "";
      workspace.selectedPageId = options?.preserveSelection && workspace.pages.some((item) => item.id === nextPageId)
        ? nextPageId
        : workspace.pages[0]?.id ?? "";
      workspace.selectedSchemeId = options?.preserveSelection && workspace.schemes.some((item) => item.id === nextSchemeId)
        ? nextSchemeId
        : workspace.schemes[0]?.id ?? "";
      workspace.selectedDevice = workspace.devices[0]?.displayName ?? "未选择移动设备";
    } else {
      workspace.components = [];
      workspace.actions = [];
      workspace.pages = [];
      workspace.schemes = [];
      workspace.devices = [];
      workspace.selectedComponentId = "";
      workspace.selectedPageId = "";
      workspace.selectedSchemeId = "";
      workspace.activeSchemeId = "";
      workspace.toast = workspaceResponse.message ?? "工作区读取失败，请检查桌面壳子日志";
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

    if (resourceResponse.ok && resourceResponse.payload) {
      workspace.resources = resourceResponse.payload;
    }
  } finally {
    workspace.loading = false;
  }
}

export async function refreshDeviceConnectivity(): Promise<void> {
  const [deviceResponse, gatewayResponse] = await Promise.all([
    sendShell<DeviceStatusSnapshot>("device.status"),
    sendShell<GatewayStatus>("gateway.status"),
  ]);

  if (deviceResponse.ok && deviceResponse.payload) {
    workspace.deviceStatus = deviceResponse.payload;
    workspace.devices = deviceResponse.payload.devices.filter((device) => String(device.kind).toLowerCase() !== "desktop" && !device.deviceId.startsWith("desktop-"));
    workspace.selectedDevice = workspace.devices[0]?.displayName ?? "未选择移动设备";
  }

  if (gatewayResponse.ok && gatewayResponse.payload) {
    workspace.gatewayStatus = gatewayResponse.payload;
  }
}

export interface SchemeApplyResult {
  schemeId: string;
  deviceId?: string | null;
  delivery: "desktop" | "acknowledged" | "unconfirmed" | "pending";
  message: string;
}

export async function applyScheme(schemeId: string, deviceId?: string): Promise<SchemeApplyResult | null> {
  const response = await sendShell<SchemeApplyResult>("workspace.applyScheme", { id: schemeId, deviceId });
  if (response.ok) {
    workspace.activeSchemeId = response.payload?.schemeId ?? schemeId;
    workspace.toast = response.payload?.message ?? "方案已应用到设备";
    return response.payload ?? null;
  } else {
    workspace.toast = response.message ?? "方案应用失败";
    return null;
  }
}
