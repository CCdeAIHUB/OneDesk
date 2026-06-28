export interface NativeBridgeRequest {
  targetDeviceId: string;
  capability: string;
  payload: unknown;
}

export interface NativeBridgeResponse<T = unknown> {
  ok: boolean;
  payload?: T;
  errorCode?: string;
  message?: string;
}

type ThemeForShell = "light" | "dark";

declare global {
  interface Window {
    OneDeskNative?: {
      callJsApi?: (targetDeviceId: string, capability: string, payloadJson: string) => string | Promise<string>;
      callComponentJsApi?: (componentId: string, targetDeviceId: string, capability: string, payloadJson: string) => string | Promise<string>;
      callPluginJsApi?: (pluginId: string, targetDeviceId: string, capability: string, payloadJson: string) => string | Promise<string>;
      getDeviceId?: () => string | Promise<string>;
      minimizeWindow?: () => string | Promise<string>;
      maximizeWindow?: () => string | Promise<string>;
      startWindowDrag?: () => string | Promise<string>;
      closeWindow?: () => string | Promise<string>;
      setShellTheme?: (theme: ThemeForShell) => string | Promise<string>;
      send?: (type: string, payloadJson?: string) => string | Promise<string>;
    };
  }
}

export async function callComponentNative<T = unknown>(componentId: string, request: NativeBridgeRequest): Promise<NativeBridgeResponse<T>> {
  if (!window.OneDeskNative?.callComponentJsApi) {
    return {
      ok: false,
      errorCode: "CapabilityNotSupported",
      message: "当前预览环境未连接 OneDesk 壳子",
    };
  }

  const raw = await window.OneDeskNative.callComponentJsApi(
    componentId,
    request.targetDeviceId,
    request.capability,
    JSON.stringify(request.payload),
  );
  return JSON.parse(raw) as NativeBridgeResponse<T>;
}

export async function callPluginNative<T = unknown>(pluginId: string, request: NativeBridgeRequest): Promise<NativeBridgeResponse<T>> {
  if (!window.OneDeskNative?.callPluginJsApi) {
    return {
      ok: false,
      errorCode: "CapabilityNotSupported",
      message: "当前预览环境未连接 OneDesk 壳子",
    };
  }

  const raw = await window.OneDeskNative.callPluginJsApi(
    pluginId,
    request.targetDeviceId,
    request.capability,
    JSON.stringify(request.payload),
  );
  return JSON.parse(raw) as NativeBridgeResponse<T>;
}

export async function callNative<T = unknown>(request: NativeBridgeRequest): Promise<NativeBridgeResponse<T>> {
  if (!window.OneDeskNative?.callJsApi) {
    return {
      ok: false,
      errorCode: "CapabilityNotSupported",
      message: "当前预览环境未连接 OneDesk 壳子",
    };
  }

  const raw = await window.OneDeskNative.callJsApi(
    request.targetDeviceId,
    request.capability,
    JSON.stringify(request.payload),
  );
  return JSON.parse(raw) as NativeBridgeResponse<T>;
}

export async function sendShell<T = unknown>(type: string, payload?: unknown): Promise<NativeBridgeResponse<T>> {
  if (!window.OneDeskNative?.send) {
    return {
      ok: false,
      errorCode: "ShellNotConnected",
      message: "当前环境无法调用桌面壳子",
    };
  }

  const raw = await window.OneDeskNative.send(type, payload === undefined ? undefined : JSON.stringify(payload));
  return JSON.parse(raw) as NativeBridgeResponse<T>;
}

export async function getLocalDeviceId(): Promise<string> {
  if (!window.OneDeskNative?.getDeviceId) {
    return "desktop-preview";
  }

  return await window.OneDeskNative.getDeviceId();
}

export async function minimizeWindow(): Promise<void> {
  await window.OneDeskNative?.minimizeWindow?.();
}

export async function maximizeWindow(): Promise<boolean> {
  const raw = await window.OneDeskNative?.maximizeWindow?.();
  return raw ? Boolean((JSON.parse(raw) as NativeBridgeResponse<boolean>).payload) : false;
}

export async function startWindowDrag(): Promise<void> {
  await window.OneDeskNative?.startWindowDrag?.();
}

export async function closeWindow(): Promise<void> {
  await window.OneDeskNative?.closeWindow?.();
}

export async function setShellTheme(theme: ThemeForShell): Promise<void> {
  await window.OneDeskNative?.setShellTheme?.(theme);
}
