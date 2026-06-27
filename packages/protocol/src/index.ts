import { z } from "zod";

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
  "PermissionDenied",
  "TargetOffline",
  "TargetNotFound",
  "InvalidRequest",
  "ExecutionFailed",
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
