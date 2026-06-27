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

export default defineConfig({
  base: "./",
  plugins: [vue(), tailwindcss(), fileShellHtmlPlugin()],
  build: {
    outDir: "dist",
    emptyOutDir: true,
  },
});
