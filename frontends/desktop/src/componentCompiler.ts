import { compileScript, compileStyle, compileTemplate, parse } from "@vue/compiler-sfc";
import * as esbuild from "esbuild-wasm";
import esbuildWasmUrl from "esbuild-wasm/esbuild.wasm?url";

export interface CodeComponentRuntimeArtifact {
  schemaVersion: 1;
  entryFile: string;
  codeFile: string;
  styleFile: string;
  sha256: string;
}

export interface CodeComponentBuildResult {
  code: string;
  style: string;
  manifest: CodeComponentRuntimeArtifact;
}

let initialization: Promise<void> | null = null;

export async function compileCodeComponent(
  files: Record<string, string>,
  entryFile: string,
): Promise<CodeComponentBuildResult> {
  await initializeCompiler();
  const normalizedFiles = normalizeFiles(files);
  const normalizedEntry = normalizePath(entryFile);
  if (!normalizedFiles[normalizedEntry]) throw new Error(`入口文件不存在：${normalizedEntry}`);

  const entryModule = "__onedesk_runtime_entry__.ts";
  normalizedFiles[entryModule] = `
import { createApp } from "vue";
import Component from "/${normalizedEntry}";
const root = document.getElementById("app");
if (!root) throw new Error("OneDeskCodeComponentRootMissing");
globalThis.__oneDeskCodeComponentApp = createApp(Component);
globalThis.__oneDeskCodeComponentApp.mount(root);
`;

  const result = await esbuild.build({
    entryPoints: [`/${entryModule}`],
    bundle: true,
    write: false,
    format: "iife",
    platform: "browser",
    target: ["chrome109", "safari16"],
    minify: true,
    legalComments: "none",
    sourcemap: false,
    plugins: [virtualComponentPlugin(normalizedFiles)],
  });
  const code = result.outputFiles.find((file) => file.path.endsWith(".js"))?.text;
  if (!code) throw new Error("组件构建没有生成 JavaScript 产物");
  const style = result.outputFiles.find((file) => file.path.endsWith(".css"))?.text ?? "";
  const sha256 = await digest(`${code}\n/* onedesk-style */\n${style}`);
  return {
    code,
    style,
    manifest: {
      schemaVersion: 1,
      entryFile: normalizedEntry,
      codeFile: "dist/onedesk-component.js",
      styleFile: "dist/onedesk-component.css",
      sha256,
    },
  };
}

async function initializeCompiler() {
  initialization ??= esbuild.initialize({ wasmURL: esbuildWasmUrl, worker: true });
  await initialization;
}

function virtualComponentPlugin(files: Record<string, string>): esbuild.Plugin {
  return {
    name: "onedesk-virtual-component",
    setup(build) {
      build.onResolve({ filter: /^vue$/ }, () => ({ path: "vue", namespace: "onedesk-vue-runtime" }));
      build.onLoad({ filter: /.*/, namespace: "onedesk-vue-runtime" }, () => ({
        contents: "module.exports = globalThis.Vue;",
        loader: "js",
      }));

      build.onResolve({ filter: /.*/ }, (args) => {
        if (!args.path.startsWith(".") && !args.path.startsWith("/")) {
          return { errors: [{ text: `代码组件禁止直接依赖未打包模块：${args.path}` }] };
        }
        const base = args.path.startsWith("/") ? args.path.slice(1) : joinPath(args.resolveDir, args.path);
        const resolved = resolveProjectFile(files, base);
        return resolved
          ? { path: resolved, namespace: "onedesk-project", pluginData: { importer: args.importer } }
          : { errors: [{ text: `无法解析组件文件：${args.path}` }] };
      });

      build.onLoad({ filter: /.*/, namespace: "onedesk-project" }, async (args) => {
        const source = files[args.path];
        const resolveDir = parentPath(args.path);
        if (args.path.endsWith(".vue")) return compileVueFile(args.path, source, resolveDir);
        if (args.path.endsWith(".ts") || args.path.endsWith(".tsx")) return { contents: source, loader: "ts", resolveDir };
        if (args.path.endsWith(".json")) return { contents: source, loader: "json", resolveDir };
        if (args.path.endsWith(".css")) return { contents: source, loader: "css", resolveDir };
        if (args.path.endsWith(".svg")) return { contents: source, loader: "dataurl", resolveDir };
        return { contents: source, loader: "js", resolveDir };
      });
    },
  };
}

function compileVueFile(filename: string, source: string, resolveDir: string): esbuild.OnLoadResult {
  const id = `data-v-${stableId(filename)}`;
  const parsed = parse(source, { filename });
  if (parsed.errors.length) {
    return { errors: parsed.errors.map((error) => ({ text: String(error) })) };
  }
  const descriptor = parsed.descriptor;
  const scoped = descriptor.styles.some((style) => style.scoped);
  const script = descriptor.script || descriptor.scriptSetup
    ? compileScript(descriptor, { id, genDefaultAs: "__sfc__" }).content
    : "const __sfc__ = {};";
  let templateCode = "function render(){ return null; }";
  if (descriptor.template) {
    const template = compileTemplate({
      id,
      filename,
      source: descriptor.template.content,
      scoped,
      slotted: descriptor.slotted,
      compilerOptions: { bindingMetadata: descriptor.scriptSetup ? compileScript(descriptor, { id }).bindings : undefined },
    });
    if (template.errors.length) return { errors: template.errors.map((error) => ({ text: String(error) })) };
    templateCode = template.code.replace("export function render", "function render");
  }
  const styleImports = descriptor.styles.map((style, index) => {
    const virtualName = `${filename}.onedesk-style-${index}.css`;
    const compiled = compileStyle({ id, filename, source: style.content, scoped: style.scoped });
    if (compiled.errors.length) throw new Error(compiled.errors.map(String).join("\n"));
    return { virtualName, css: compiled.code };
  });
  // 样式以内联注入方式进入组件产物，iframe 卸载时会随文档一起释放。
  const injectStyles = styleImports.map(({ css }) =>
    `const __style=document.createElement("style");__style.textContent=${JSON.stringify(css)};document.head.appendChild(__style);`,
  ).join("\n");
  return {
    contents: `${script}\n${templateCode}\n__sfc__.render = render;\n${scoped ? `__sfc__.__scopeId = ${JSON.stringify(id)};` : ""}\n${injectStyles}\nexport default __sfc__;`,
    loader: descriptor.script?.lang === "ts" || descriptor.scriptSetup?.lang === "ts" ? "ts" : "js",
    resolveDir,
  };
}

function normalizeFiles(files: Record<string, string>) {
  return Object.fromEntries(Object.entries(files).map(([path, content]) => [normalizePath(path), content]));
}

function normalizePath(path: string) {
  const segments: string[] = [];
  for (const segment of path.replaceAll("\\", "/").split("/")) {
    if (!segment || segment === ".") continue;
    if (segment === "..") {
      if (!segments.length) throw new Error("组件路径不能越过项目根目录");
      segments.pop();
    } else segments.push(segment);
  }
  return segments.join("/");
}

function joinPath(directory: string, relative: string) {
  return normalizePath(`${directory}/${relative}`);
}

function parentPath(path: string) {
  const index = path.lastIndexOf("/");
  return index < 0 ? "" : path.slice(0, index);
}

function resolveProjectFile(files: Record<string, string>, requested: string) {
  const normalized = normalizePath(requested);
  const candidates = [normalized, `${normalized}.ts`, `${normalized}.js`, `${normalized}.vue`, `${normalized}.json`, `${normalized}/index.ts`, `${normalized}/index.js`, `${normalized}/index.vue`];
  return candidates.find((candidate) => Object.hasOwn(files, candidate));
}

function stableId(value: string) {
  let hash = 2166136261;
  for (let index = 0; index < value.length; index += 1) {
    hash ^= value.charCodeAt(index);
    hash = Math.imul(hash, 16777619);
  }
  return (hash >>> 0).toString(16).padStart(8, "0");
}

async function digest(value: string) {
  const bytes = new TextEncoder().encode(value);
  const hash = await crypto.subtle.digest("SHA-256", bytes);
  return Array.from(new Uint8Array(hash), (byte) => byte.toString(16).padStart(2, "0")).join("");
}
