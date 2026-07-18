import tailwindcss from "@tailwindcss/vite";
import vue from "@vitejs/plugin-vue";
import { defineConfig } from "vite";

function fileShellHtmlPlugin() {
  return {
    name: "onedesk-file-shell-html",
    enforce: "post" as const,
    transformIndexHtml(html: string) {
      // file:// 前端仍必须保持 ES module 语义；两个 Chromium 壳只开放本地文件模块访问，
      // 网络与 wwwroot 外文件仍由原生请求拦截器拒绝。
      return html.replaceAll(" crossorigin", "");
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
