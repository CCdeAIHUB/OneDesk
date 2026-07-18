import { readFileSync, readdirSync, statSync, writeFileSync } from "node:fs";
import { join, relative, resolve } from "node:path";

const [sourceArgument = "src", outputArgument = "src/generatedIconCollections.ts", ...prefixes] = process.argv.slice(2);
if (!prefixes.length) throw new Error("至少需要一个 Iconify 图标集前缀");

const packageRoot = process.cwd();
const sourceRoot = resolve(packageRoot, sourceArgument);
const outputPath = resolve(packageRoot, outputArgument);
const sourceFiles = enumerate(sourceRoot).filter((path) => path !== outputPath && /\.(vue|ts|tsx|js|jsx)$/.test(path));

const collections = prefixes.map((prefix) => {
  const catalogPath = resolve(packageRoot, "node_modules", "@iconify-json", prefix, "icons.json");
  const catalog = JSON.parse(readFileSync(catalogPath, "utf8"));
  const names = new Set();
  const pattern = new RegExp(`\\b${prefix}:([a-z0-9-]+)`, "g");
  for (const path of sourceFiles) {
    const source = readFileSync(path, "utf8");
    for (const match of source.matchAll(pattern)) {
      names.add(match[1]);
      // 导航选中态会把 bold-duotone 动态替换为 bold，静态扫描必须显式补入。
      if (match[1].endsWith("-bold-duotone")) names.add(match[1].replace(/-bold-duotone$/, "-bold"));
    }
  }

  const icons = {};
  const aliases = {};
  const include = (name) => {
    if (catalog.icons[name]) {
      icons[name] = catalog.icons[name];
      return;
    }
    const alias = catalog.aliases?.[name];
    if (!alias) throw new Error(`图标不存在：${prefix}:${name}`);
    aliases[name] = alias;
    include(alias.parent);
  };
  [...names].sort().forEach(include);
  return {
    prefix,
    icons,
    ...(Object.keys(aliases).length ? { aliases } : {}),
    ...(catalog.width ? { width: catalog.width } : {}),
    ...(catalog.height ? { height: catalog.height } : {}),
  };
});

const banner = `// 此文件由 scripts/generate-icon-subset.mjs 生成，请勿手工编辑。\nimport type { IconifyJSON } from "@iconify/types";\n`;
writeFileSync(outputPath, `${banner}export const iconCollections: IconifyJSON[] = ${JSON.stringify(collections)};\n`, "utf8");
const count = collections.reduce((total, collection) => total + Object.keys(collection.icons).length + Object.keys(collection.aliases ?? {}).length, 0);
console.log(`已生成 ${relative(packageRoot, outputPath)}，包含 ${count} 个图标`);

function enumerate(directory) {
  return readdirSync(directory).flatMap((name) => {
    const path = join(directory, name);
    return statSync(path).isDirectory() ? enumerate(path) : [path];
  });
}
