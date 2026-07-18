// 此文件由 packages/protocol/schema/onedesk.protocol.json 生成，请勿手工修改。
// schema-sha256: 04aedd08890925036ff61bc67b943c1af80a039c82a6f3a09af3e373137022ee
export const protocolVersion = 1 as const;

export type GatewayMessageType = "request" | "response" | "event";

export type ProtocolDeviceKind = "desktop" | "mobile";

export type ProtocolSourceKind = "component" | "plugin" | "system";

export interface ProtocolDeviceIdentity {
  deviceId: string;
  displayName: string;
  kind: ProtocolDeviceKind;
  platform: string;
  architecture: string;
}

export interface ProtocolTrustedSource {
  schemeId?: string;
  pageId?: string;
  componentId?: string;
  pluginId?: string;
  kind: ProtocolSourceKind;
}

export interface PairingRequestContract {
  verificationCode: string;
  clientNonce: string;
  mobileIdentity: ProtocolDeviceIdentity;
}

export interface PairingResponseContract {
  desktopIdentity: ProtocolDeviceIdentity;
  assignedMobileIdentity: ProtocolDeviceIdentity;
  trustCredential: string;
  credentialExpiresAtUnixMs: number;
}

export interface TrustedConnectRequestContract {
  trustCredential: string;
  clientNonce: string;
  mobileIdentity: ProtocolDeviceIdentity;
}

export interface JsApiRequestContract {
  requestId: string;
  targetDeviceId: string;
  source: ProtocolTrustedSource;
  capability: string;
  payload: unknown;
}

export interface JsApiErrorContract {
  code: string;
  message: string;
  highRisk: boolean;
}

export interface JsApiResponseContract {
  requestId: string;
  ok: boolean;
  error?: JsApiErrorContract;
  payload?: unknown;
}

export interface SchemeDescriptorContract {
  version: string;
  hash: string;
  totalBytes: number;
  hasScheme: boolean;
}

export interface LogRecordContract {
  logId: string;
  createdAtUnixMs: number;
  sourceDeviceId: string;
  level: string;
  category: string;
  message: string;
  context: unknown;
}

export interface MobileGatewayEnvelope {
  protocolVersion: number;
  messageType: string;
  messageId: string;
  correlationId?: string;
  payload: unknown;
}
