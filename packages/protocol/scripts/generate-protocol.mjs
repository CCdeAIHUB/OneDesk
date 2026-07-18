import { createHash } from "node:crypto";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const packageRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const workspaceRoot = resolve(packageRoot, "../..");
const schemaPath = resolve(packageRoot, "schema/onedesk.protocol.json");
const schemaText = await readFile(schemaPath, "utf8");
const schema = JSON.parse(schemaText);
const schemaHash = createHash("sha256").update(schemaText).digest("hex");
const capabilityPath = resolve(packageRoot, "capabilities.json");
const capabilityText = await readFile(capabilityPath, "utf8");
const capabilityCatalog = JSON.parse(capabilityText);
const capabilityHash = createHash("sha256").update(capabilityText).digest("hex");
const checkOnly = process.argv.includes("--check");

const outputs = new Map([
  [resolve(packageRoot, "src/generated/protocol.ts"), generateTypeScript()],
  [resolve(workspaceRoot, "apps/desktop/Protocol/GeneratedProtocolContracts.cs"), generateCSharp()],
  [resolve(workspaceRoot, "apps/mobile/android/app/src/main/java/cc/onedesk/mobile/GeneratedProtocolContracts.kt"), generateKotlin()],
  [resolve(workspaceRoot, "apps/mobile/ios/GeneratedProtocolContracts.swift"), generateSwift()],
  [resolve(workspaceRoot, "apps/mobile/ios/GeneratedCapabilityCatalog.swift"), generateSwiftCapabilities()],
]);

let changed = false;
for (const [path, content] of outputs) {
  const current = await readFile(path, "utf8").catch(() => "");
  if (current === content) continue;
  changed = true;
  if (!checkOnly) {
    await mkdir(dirname(path), { recursive: true });
    await writeFile(path, content, "utf8");
  }
}

if (checkOnly && changed) {
  throw new Error("协议生成文件已过期，请执行 pnpm --filter @onedesk/protocol generate");
}

function pascal(value) {
  return value.charAt(0).toUpperCase() + value.slice(1);
}

function generatedHeader(prefix) {
  return `${prefix} 此文件由 packages/protocol/schema/onedesk.protocol.json 生成，请勿手工修改。\n${prefix} schema-sha256: ${schemaHash}\n`;
}

function mapType(type, language) {
  const primitives = {
    ts: { string: "string", int: "number", long: "number", bool: "boolean", json: "unknown", bytes: "Uint8Array" },
    cs: { string: "string", int: "int", long: "long", bool: "bool", json: "JsonElement", bytes: "byte[]" },
    kt: { string: "String", int: "Int", long: "Long", bool: "Boolean", json: "JSONObject", bytes: "ByteArray" },
    swift: { string: "String", int: "Int", long: "Int64", bool: "Bool", json: "JSONValue", bytes: "Data" },
  };
  return primitives[language][type] ?? type;
}

function fieldType(field, language) {
  let type = mapType(field.type, language);
  if (field.repeated) {
    type = language === "cs"
      ? `IReadOnlyList<${type}>`
      : language === "ts"
        ? `ReadonlyArray<${type}>`
        : language === "kt"
          ? `List<${type}>`
          : `[${type}]`;
  }
  if (field.optional && language !== "ts") type += "?";
  return type;
}

function generateTypeScript() {
  const lines = [generatedHeader("//").trimEnd(), `export const protocolVersion = ${schema.protocolVersion} as const;`, ""];
  for (const [name, values] of Object.entries(schema.enums)) {
    lines.push(`export type ${name} = ${values.map((value) => JSON.stringify(value)).join(" | ")};`, "");
  }
  for (const [name, fields] of Object.entries(schema.records)) {
    lines.push(`export interface ${name} {`);
    for (const field of fields) lines.push(`  ${field.name}${field.optional ? "?" : ""}: ${fieldType(field, "ts")};`);
    lines.push("}", "");
  }
  return `${lines.join("\n").trimEnd()}\n`;
}

function generateCSharp() {
  const lines = [
    generatedHeader("//").trimEnd(),
    "using System.Text.Json;",
    "",
    "namespace OneDesk.Desktop.Transport;",
    "",
    "public static class OneDeskProtocol",
    "{",
    `    public const int Version = ${schema.protocolVersion};`,
    `    public const string SchemaSha256 = \"${schemaHash}\";`,
    "}",
    "",
  ];
  for (const [name, values] of Object.entries(schema.enums)) {
    lines.push(`public enum ${name}`, "{");
    values.forEach((value, index) => lines.push(`    ${pascal(value)}${index < values.length - 1 ? "," : ""}`));
    lines.push("}", "");
  }
  for (const [name, fields] of Object.entries(schema.records)) {
    lines.push(`public sealed record ${name}(`);
    fields.forEach((field, index) => lines.push(`    ${fieldType(field, "cs")} ${pascal(field.name)}${index < fields.length - 1 ? "," : ""}`));
    lines.push(");", "");
  }
  return `${lines.join("\n").trimEnd()}\n`;
}

function generateKotlin() {
  const lines = [
    generatedHeader("//").trimEnd(),
    "package cc.onedesk.mobile",
    "",
    "import org.json.JSONObject",
    "",
    "object OneDeskProtocol {",
    `    const val PROTOCOL_VERSION: Int = ${schema.protocolVersion}`,
    `    const val SCHEMA_SHA256: String = \"${schemaHash}\"`,
    "}",
    "",
  ];
  for (const [name, values] of Object.entries(schema.enums)) {
    lines.push(`enum class ${name}(val wireValue: String) {`);
    values.forEach((value, index) => lines.push(`    ${value.replace(/[^a-zA-Z0-9]/g, "_").toUpperCase()}(\"${value}\")${index < values.length - 1 ? "," : ";"}`));
    lines.push("}", "");
  }
  for (const [name, fields] of Object.entries(schema.records)) {
    lines.push(`data class ${name}(`);
    fields.forEach((field, index) => lines.push(`    val ${field.name}: ${fieldType(field, "kt")}${index < fields.length - 1 ? "," : ""}`));
    lines.push(")", "");
  }
  return `${lines.join("\n").trimEnd()}\n`;
}

function generateSwift() {
  const lines = [
    generatedHeader("//").trimEnd(),
    "import Foundation",
    "",
    "enum OneDeskProtocol {",
    `    static let version = ${schema.protocolVersion}`,
    `    static let schemaSha256 = \"${schemaHash}\"`,
    "}",
    "",
    "enum JSONValue: Codable {",
    "    case string(String), number(Double), bool(Bool), object([String: JSONValue]), array([JSONValue]), null",
    "}",
    "",
  ];
  for (const [name, values] of Object.entries(schema.enums)) {
    lines.push(`enum ${name}: String, Codable {`);
    values.forEach((value) => lines.push(`    case ${value.replace(/[^a-zA-Z0-9]/g, "_")} = \"${value}\"`));
    lines.push("}", "");
  }
  for (const [name, fields] of Object.entries(schema.records)) {
    lines.push(`struct ${name}: Codable {`);
    fields.forEach((field) => lines.push(`    let ${field.name}: ${fieldType(field, "swift")}`));
    lines.push("}", "");
  }
  return `${lines.join("\n").trimEnd()}\n`;
}

function generateSwiftCapabilities() {
  const lines = [
    "// 此文件由 packages/protocol/capabilities.json 生成，请勿手工修改。",
    `// catalog-sha256: ${capabilityHash}`,
    "import Foundation",
    "",
    "struct GeneratedCapabilityDefinition {",
    "    let id: String",
    "    let category: String",
    "    let highRisk: Bool",
    "}",
    "",
    "enum GeneratedCapabilityCatalog {",
    "    static let entries: [String: GeneratedCapabilityDefinition] = [",
  ];
  for (const capability of capabilityCatalog.capabilities) {
    lines.push(
      `        "${capability.id}": .init(id: "${capability.id}", category: "${capability.category}", highRisk: ${capability.risk === "high"}),`,
    );
  }
  lines.push(
    "    ]",
    "    static let ids = Set(entries.keys)",
    "    static let highRiskIds = Set(entries.values.filter(\\.highRisk).map(\\.id))",
    "}",
    "",
  );
  return lines.join("\n");
}
