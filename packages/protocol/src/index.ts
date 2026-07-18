import { z } from "zod";

export * from "./generated/protocol.js";

export const permissionRiskSchema = z.enum(["normal", "high"]);

export const permissionSchema = z.object({
  category: z.string().min(1),
  capability: z.string().min(1),
  risk: permissionRiskSchema,
  description: z.string().min(1),
});

export type PermissionDeclaration = z.infer<typeof permissionSchema>;

export const platformArtifactSchema = z.object({
  platform: z.enum(["windows", "macos", "linux"]),
  architecture: z.enum(["x64", "arm64"]),
  path: z.string().min(1),
  command: z.array(z.string()).default([]),
});

export const pluginManifestSchema = z.object({
  manifestVersion: z.literal(1),
  id: z.string().min(1),
  name: z.string().min(1),
  version: z.string().min(1),
  author: z.string().optional(),
  frontend: z
    .object({
      entry: z.string().min(1),
    })
    .optional(),
  backend: z
    .object({
      protocol: z.literal("json-rpc"),
      persistent: z.boolean().default(false),
      artifacts: z.array(platformArtifactSchema).default([]),
    })
    .optional(),
  permissions: z.array(permissionSchema).default([]),
  settingsSchema: z.record(z.string(), z.unknown()).optional(),
  selfContained: z.boolean().default(true),
});

export type PluginManifest = z.infer<typeof pluginManifestSchema>;

export const componentManifestSchema = z.object({
  manifestVersion: z.literal(1),
  id: z.string().min(1),
  name: z.string().min(1),
  version: z.string().min(1),
  mode: z.enum(["visual", "code"]),
  entry: z.string().min(1),
  visualConfig: z.string().optional(),
  permissions: z.array(permissionSchema).default([]),
  actionDependencies: z.array(z.string()).default([]),
  pluginDependencies: z
    .array(
      z.object({
        id: z.string().min(1),
        version: z.string().min(1),
      }),
    )
    .default([]),
});

export type ComponentManifest = z.infer<typeof componentManifestSchema>;

export const jsApiErrorCodeSchema = z.enum([
  "CapabilityNotSupported",
  "CapabilityNotFound",
  "CapabilityPlatformHandlerMissing",
  "CapabilityRequiresUserPath",
  "PermissionDenied",
  "TargetOffline",
  "TargetNotFound",
  "GatewayOffline",
  "TransportNotAttached",
  "InvalidPath",
  "InvalidRequest",
  "InvalidPayload",
  "ExecutionFailed",
  "PluginNotInstalled",
  "PluginBackendMissing",
  "PluginNoResponse",
]);

export type JsApiErrorCode = z.infer<typeof jsApiErrorCodeSchema>;

export const highRiskPermissionIds = [
  "file.writeExternal",
  "file.deleteExternal",
  "process.control",
  "memory.read",
  "memory.write",
  "input.keyboardMouseSimulation",
  "network.access",
  "clipboard.read",
  "clipboard.write",
  "camera.access",
  "microphone.access",
  "screen.capture",
  "screen.record",
  "background.persistent",
  "credential.access",
  "shell.execute",
  "crossDevice.sensitiveJsApi",
] as const;

// 桌面端、Android 和 iOS 必须共享同一份方案缓存结构，避免各端自行猜测字段。
export const triggerSchema = z.object({
  id: z.string().min(1),
  category: z.string().min(1),
  displayName: z.string().min(1),
  fingerCount: z.number().int().min(0).max(5),
  platformLimited: z.boolean().optional(),
});

export const jsApiInvocationSchema = z.object({
  targetDeviceId: z.string().min(1),
  capability: z.string().min(1),
  parameters: z.record(z.string(), z.unknown()).default({}),
});

export const actionDefinitionSchema = z.object({
  id: z.string().min(1),
  name: z.string().min(1),
  trigger: triggerSchema,
  invocations: z.array(jsApiInvocationSchema).min(1),
  updatedAt: z.string().optional(),
});

export const visualTextLayerSchema = z.object({
  id: z.string().min(1),
  content: z.string(),
  fontSize: z.number().positive(),
  color: z.string().min(1),
  position: z.string().optional(),
  x: z.number().min(0).max(100),
  y: z.number().min(0).max(100),
  width: z.number().positive().max(100),
  height: z.number().positive().max(100),
});

export const visualConfigSchema = z.object({
  base: z.object({
    borderRadius: z.number().nonnegative(),
    margin: z.number().nonnegative(),
    layout: z.string().min(1),
  }),
  background: z.object({
    kind: z.enum(["solid", "gradient", "image", "video"]),
    value: z.string(),
    secondaryValue: z.string().default(""),
    mediaSource: z.string().default(""),
  }),
  texts: z.array(visualTextLayerSchema),
  image: z.object({
    source: z.string().default(""),
    size: z.enum(["cover", "contain"]).or(z.string().min(1)),
    position: z.string().min(1),
    margin: z.number().nonnegative(),
  }),
  states: z.object({
    pressed: z.string(),
    locked: z.string(),
  }),
});

export const gridCellSchema = z.object({
  id: z.string().min(1),
  row: z.number().int().min(1),
  column: z.number().int().min(1),
  rowSpan: z.number().int().min(1),
  columnSpan: z.number().int().min(1),
  componentId: z.string().nullable().optional(),
  style: z.object({
    borderRadius: z.number().nonnegative(),
    outlineColor: z.string(),
    outlineWidth: z.number().nonnegative(),
    outlineStyle: z.string(),
  }),
});

export const pageDefinitionSchema = z.object({
  id: z.string().min(1),
  name: z.string().min(1),
  rows: z.number().int().min(1).max(12),
  columns: z.number().int().min(1).max(12),
  previewRatioWidth: z.number().positive().default(21),
  previewRatioHeight: z.number().positive().default(9),
  gridHorizontalAlign: z.enum(["left", "center", "right"]).optional(),
  gridVerticalAlign: z.enum(["top", "center", "bottom"]).optional(),
  spacing: z.object({
    padding: z.number().nonnegative(),
    rowGap: z.number().nonnegative(),
    columnGap: z.number().nonnegative(),
  }),
  backgroundKind: z.enum(["solid", "gradient", "image", "video"]),
  backgroundValue: z.string(),
  backgroundSecondaryValue: z.string().nullable().optional(),
  backgroundResourceId: z.string().nullable().optional(),
  backgroundMediaSource: z.string().nullable().optional(),
  cells: z.array(gridCellSchema),
  updatedAt: z.string().optional(),
});

export const componentDefinitionSchema = z.object({
  id: z.string().min(1),
  name: z.string().min(1),
  version: z.string().min(1),
  editMode: z.union([z.enum(["visual", "code", "Visual", "Code"]), z.number().int()]),
  entryFile: z.string().min(1),
  visualConfigFile: z.string().nullable().optional(),
  actionIds: z.array(z.string()),
  requestedPermissions: z.array(z.unknown()).default([]),
  pluginDependencies: z.array(z.unknown()).default([]),
  updatedAt: z.string().optional(),
});

export const pageSwitchSchema = z.object({
  trigger: triggerSchema,
  animation: z.string().min(1),
});

export const schemeDefinitionSchema = z.object({
  id: z.string().min(1),
  name: z.string().min(1),
  version: z.string().min(1),
  pageIds: z.array(z.string()),
  globalPrevious: pageSwitchSchema,
  globalNext: pageSwitchSchema,
  edges: z.array(z.object({
    fromPageId: z.string().min(1),
    toPageId: z.string().min(1),
    trigger: triggerSchema,
    animation: z.string().min(1),
  })),
  pluginDependencies: z.array(z.unknown()).default([]),
  updatedAt: z.string().optional(),
});

export const schemeSnapshotSchema = z.object({
  activeSchemeId: z.string().nullable(),
  appliedAt: z.string().nullable().optional(),
  scheme: schemeDefinitionSchema.nullable(),
  pages: z.array(pageDefinitionSchema),
  components: z.array(z.object({
    definition: componentDefinitionSchema,
    visualConfig: visualConfigSchema.nullable(),
  })),
  actions: z.array(actionDefinitionSchema),
});

export const schemeDescriptorSchema = z.object({
  version: z.string(),
  hash: z.string().regex(/^[a-f0-9]{64}$/i),
  totalBytes: z.number().int().nonnegative(),
  hasScheme: z.boolean(),
});

export const pairingQrSchema = z.object({
  host: z.string().min(1),
  port: z.number().int().min(1).max(65535),
  code: z.string().regex(/^\d{6}$/),
});

export type Trigger = z.infer<typeof triggerSchema>;
export type ActionDefinition = z.infer<typeof actionDefinitionSchema>;
export type VisualConfig = z.infer<typeof visualConfigSchema>;
export type PageDefinition = z.infer<typeof pageDefinitionSchema>;
export type ComponentDefinition = z.infer<typeof componentDefinitionSchema>;
export type SchemeDefinition = z.infer<typeof schemeDefinitionSchema>;
export type SchemeSnapshot = z.infer<typeof schemeSnapshotSchema>;
export type SchemeDescriptor = z.infer<typeof schemeDescriptorSchema>;
export type PairingQr = z.infer<typeof pairingQrSchema>;
