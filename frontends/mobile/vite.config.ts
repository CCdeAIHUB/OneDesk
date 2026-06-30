import tailwindcss from "@tailwindcss/vite";
import vue from "@vitejs/plugin-vue";
import { defineConfig } from "vite";

function fileShellHtmlPlugin() {
  return {
    name: "onedesk-file-shell-html",
    enforce: "post" as const,
    transformIndexHtml(html: string) {
      return html.replaceAll(' type="module"', "").replaceAll(" crossorigin", "");
    },
  };
}

function inlineFileShellAssetsPlugin() {
  return {
    name: "onedesk-inline-file-shell-assets",
    enforce: "post" as const,
    generateBundle(_: unknown, bundle: Record<string, any>) {
      const htmlAsset = bundle["index.html"];
      if (!htmlAsset || typeof htmlAsset.source !== "string") {
        return;
      }

      let html = htmlAsset.source as string;
      for (const [fileName, item] of Object.entries(bundle)) {
        if (item.type === "chunk" && fileName.endsWith(".js")) {
          const safeCode = item.code.replace(/<\/script/gi, "<\\/script");
          const scriptBlock = `<script>\n${safeCode}\n</script>`;
          html = html.replace(
            new RegExp(`<script src=["']\\./${escapeRegExp(fileName)}["']><\\/script>`),
            "",
          );
          html = html.replace("</body>", () => `${scriptBlock}\n  </body>`);
          delete bundle[fileName];
        }

        if (item.type === "asset" && fileName.endsWith(".css") && typeof item.source === "string") {
          const safeCss = item.source.replace(/<\/style/gi, "<\\/style");
          html = html.replace(
            new RegExp(`<link rel=["']stylesheet["'] href=["']\\./${escapeRegExp(fileName)}["']>`),
            () => `<style>\n${safeCss}\n</style>`,
          );
          delete bundle[fileName];
        }
      }

      htmlAsset.source = html;
    },
  };
}

function escapeRegExp(value: string) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

export default defineConfig({
  base: "./",
  plugins: [vue(), tailwindcss(), fileShellHtmlPlugin(), inlineFileShellAssetsPlugin()],
  build: {
    outDir: "dist",
    emptyOutDir: true,
    target: "es2015",
    cssTarget: "chrome61",
  },
});
