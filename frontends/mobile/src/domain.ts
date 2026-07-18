export interface KnownDesktop {
  desktopId: string;
  name: string;
  host: string;
  port: number;
  trusted: boolean;
  schemeVersion: string;
  schemeHash: string;
}

export interface TriggerDefinition {
  id: string;
  category: string;
  displayName: string;
  fingerCount: number;
}

export interface InvocationDefinition {
  targetDeviceId: string;
  capability: string;
  parameters: Record<string, unknown>;
}

export interface ActionDefinition {
  id: string;
  name: string;
  trigger: TriggerDefinition;
  invocations: InvocationDefinition[];
}

export interface VisualTextLayer {
  id: string;
  content: string;
  fontSize: number;
  color: string;
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface VisualConfig {
  base: { borderRadius: number; margin: number; layout: string };
  background: { kind: string; value: string; secondaryValue: string; mediaSource: string };
  texts: VisualTextLayer[];
  image: { source: string; size: string; position: string; margin: number };
  states: { pressed: string; locked: string };
}

export interface ComponentDefinition {
  id: string;
  name: string;
  editMode: string | number;
  actionIds: string[];
}

export interface ComponentBundle {
  definition: ComponentDefinition;
  visualConfig: VisualConfig | null;
  codeRuntime?: { code: string; style: string; sha256: string } | null;
  codeRuntimeError?: string | null;
}

export interface CodeJsApiRequest {
  targetDeviceId: string;
  capability: string;
  payload: Record<string, unknown>;
  respond: (response: string) => void;
}

export interface GridCellDefinition {
  id: string;
  row: number;
  column: number;
  rowSpan: number;
  columnSpan: number;
  componentId?: string | null;
  style: {
    borderRadius: number;
    outlineColor: string;
    outlineWidth: number;
    outlineStyle: string;
  };
}

export interface PageDefinition {
  id: string;
  name: string;
  rows: number;
  columns: number;
  previewRatioWidth?: number;
  previewRatioHeight?: number;
  gridHorizontalAlign?: "left" | "center" | "right";
  gridVerticalAlign?: "top" | "center" | "bottom";
  spacing: { padding: number; rowGap: number; columnGap: number };
  backgroundKind: string;
  backgroundValue: string;
  backgroundSecondaryValue?: string | null;
  backgroundMediaSource?: string | null;
  cells: GridCellDefinition[];
}

export interface SchemeDefinition {
  id: string;
  name: string;
  pageIds: string[];
  globalPrevious: { trigger: TriggerDefinition; animation: string };
  globalNext: { trigger: TriggerDefinition; animation: string };
  edges: Array<{
    fromPageId: string;
    toPageId: string;
    trigger: TriggerDefinition;
    animation: string;
  }>;
}

export interface CachedScheme {
  desktopId: string;
  version: string;
  hash: string;
  activeSchemeId: string | null;
  scheme: SchemeDefinition | null;
  pages: PageDefinition[];
  components: ComponentBundle[];
  actions: ActionDefinition[];
}

export interface NativeResponse<T> {
  ok: boolean;
  payload?: T;
  message?: string;
  errorCode?: string;
}
