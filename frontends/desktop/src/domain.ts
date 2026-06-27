export type ViewKey = "home" | "component" | "page" | "scheme" | "plugin" | "permission";
export type ThemeMode = "light" | "dark" | "system";
export type SectionRoute = "manager" | "editor";
export type ComponentEditMode = "visual" | "code";

export interface NavigationItem {
  key: ViewKey;
  label: string;
  icon: string;
}

export interface PermissionGrant {
  category: string;
  capability: string;
  highRisk: boolean;
  description: string;
}

export interface TriggerDefinition {
  id: string;
  category: string;
  displayName: string;
  fingerCount?: number;
  platformLimited?: boolean;
}

export interface JsApiInvocationDefinition {
  targetDeviceId: string;
  capability: string;
  parameters: Record<string, unknown>;
}

export interface ActionDefinition {
  id: string;
  name: string;
  trigger: TriggerDefinition;
  invocations: JsApiInvocationDefinition[];
}

export interface DependencyDefinition {
  id: string;
  version: string;
  kind: string;
}

export interface ComponentDefinition {
  id: string;
  name: string;
  version: string;
  editMode: "visual" | "code" | "Visual" | "Code";
  entryFile: string;
  visualConfigFile?: string | null;
  actionIds: string[];
  requestedPermissions: PermissionGrant[];
  pluginDependencies: DependencyDefinition[];
  updatedAt?: string;
}

export interface GridSpacing {
  padding: number;
  rowGap: number;
  columnGap: number;
}

export interface CellStyleDefinition {
  borderRadius: number;
  outlineColor: string;
  outlineWidth: number;
  outlineStyle: string;
}

export interface GridCellDefinition {
  id: string;
  row: number;
  column: number;
  rowSpan: number;
  columnSpan: number;
  componentId?: string | null;
  style: CellStyleDefinition;
}

export interface PageDefinition {
  id: string;
  name: string;
  rows: number;
  columns: number;
  spacing: GridSpacing;
  backgroundKind: string;
  backgroundValue: string;
  cells: GridCellDefinition[];
  updatedAt?: string;
}

export interface PageSwitchDefinition {
  trigger: TriggerDefinition;
  animation: string;
}

export interface PageSwitchEdge {
  fromPageId: string;
  toPageId: string;
  trigger: TriggerDefinition;
  animation: string;
}

export interface SchemeDefinition {
  id: string;
  name: string;
  version: string;
  pageIds: string[];
  globalPrevious: PageSwitchDefinition;
  globalNext: PageSwitchDefinition;
  edges: PageSwitchEdge[];
  pluginDependencies: DependencyDefinition[];
  updatedAt?: string;
}

export interface ActiveSchemeState {
  schemeId: string;
  appliedAt: string;
}

export interface DeviceIdentity {
  deviceId: string;
  displayName: string;
  kind: "Desktop" | "Mobile" | number;
  platform: string;
  architecture: string;
}

export interface WorkspaceSnapshot {
  components: ComponentDefinition[];
  actions: ActionDefinition[];
  pages: PageDefinition[];
  schemes: SchemeDefinition[];
  activeScheme?: ActiveSchemeState;
  devices: DeviceIdentity[];
}

export interface QuicPeerState {
  deviceId: string;
  endpoint: string;
  online: boolean;
  lastSeenAt: string;
  trustCredentialHash: string;
}

export interface GatewayStatus {
  running: boolean;
  port: number;
  peers: QuicPeerState[];
}

export interface TrustedPairingCredential {
  deviceId: string;
  displayName: string;
  token: string;
  createdAt: string;
}

export interface DeviceStatusSnapshot {
  desktop?: DeviceIdentity;
  devices: DeviceIdentity[];
  trusted: TrustedPairingCredential[];
  gateway: GatewayStatus;
  logs: unknown[];
}

export interface PermissionGrantSnapshot {
  sourceKey: string;
  capabilities: string[];
}

export interface PermissionListSnapshot {
  grants: PermissionGrantSnapshot[];
  categories: CapabilityCategory[];
}

export interface PackageExportResult {
  ready: boolean;
  kind: string;
  packagePath: string;
  sha256: string;
  sizeBytes: number;
}

export interface SchemeCacheManifest {
  activeSchemeId: string;
  appliedAt: string;
  pageCount: number;
  componentCount: number;
  hash: string;
}

export interface CapabilitySupport {
  supported: boolean;
  note: string;
}

export interface CapabilityDefinition {
  id: string;
  category: string;
  categoryName: string;
  name: string;
  description: string;
  highRisk: boolean;
  desktop: CapabilitySupport;
  android: CapabilitySupport;
  ios: CapabilitySupport;
}

export interface CapabilityCategory {
  id: string;
  name: string;
  highRisk: boolean;
  capabilities: CapabilityDefinition[];
}

export interface QuickAction {
  label: string;
  icon: string;
  color: string;
}

export interface QuickStartItem extends QuickAction {
  desc: string;
}
