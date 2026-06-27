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

declare global {
  interface Window {
    OneDeskNative?: {
      callJsApi?: (targetDeviceId: string, capability: string, payloadJson: string) => string | Promise<string>;
      getDeviceId?: () => string | Promise<string>;
      minimizeWindow?: () => string | Promise<string>;
      maximizeWindow?: () => string | Promise<string>;
      closeWindow?: () => string | Promise<string>;
    };
  }
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

export async function getLocalDeviceId(): Promise<string> {
  if (!window.OneDeskNative?.getDeviceId) {
    return "desktop-preview";
  }

  return await window.OneDeskNative.getDeviceId();
}

export async function minimizeWindow(): Promise<void> {
  await window.OneDeskNative?.minimizeWindow?.();
}

export async function maximizeWindow(): Promise<void> {
  await window.OneDeskNative?.maximizeWindow?.();
}

export async function closeWindow(): Promise<void> {
  await window.OneDeskNative?.closeWindow?.();
}
