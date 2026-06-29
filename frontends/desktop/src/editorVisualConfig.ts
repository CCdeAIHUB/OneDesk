import type { ComponentDefinition, PageDefinition } from "./domain";

export interface VisualTextLayer {
  id: string;
  content: string;
  fontSize: number;
  color: string;
  position: string;
  x: number;
  y: number;
}

export interface VisualConfig {
  base: { borderRadius: number; margin: number; layout: string };
  background: { kind: string; value: string; secondaryValue: string; mediaSource: string };
  texts: VisualTextLayer[];
  image: { source: string; size: string; position: string; margin: number };
  states: { pressed: string; locked: string };
}

export interface PageGridMetrics {
  width: number;
  height: number;
}

export const gradientPresets: Record<string, string> = {
  "sky-cyan": "linear-gradient(135deg, #0ea5e9, #22d3ee)",
  "violet-sky": "linear-gradient(135deg, #8b5cf6, #38bdf8)",
  "emerald-sky": "linear-gradient(135deg, #34d399, #38bdf8)",
  "amber-rose": "linear-gradient(135deg, #fbbf24, #fb7185)",
};

export function defaultVisualConfig(component?: ComponentDefinition): VisualConfig {
  return {
    base: { borderRadius: 16, margin: 8, layout: "center" },
    background: { kind: "gradient", value: "#0ea5e9", secondaryValue: "#22d3ee", mediaSource: "" },
    texts: [
      {
        id: "text-1",
        content: component?.name ? `按钮 · ${component.name}` : "按钮",
        fontSize: 14,
        color: "#ffffff",
        position: "center",
        x: 50,
        y: 50,
      },
    ],
    image: { source: "", size: "cover", position: "center", margin: 0 },
    states: { pressed: "scale-95", locked: "opacity-60" },
  };
}

export function parseVisualConfig(json: string | undefined, component?: ComponentDefinition): VisualConfig {
  const fallback = defaultVisualConfig(component);
  if (!json) return fallback;

  try {
    const parsed = JSON.parse(json);
    return {
      base: {
        borderRadius: Number(parsed?.base?.borderRadius ?? fallback.base.borderRadius),
        margin: Number(parsed?.base?.margin ?? fallback.base.margin),
        layout: String(parsed?.base?.layout ?? fallback.base.layout),
      },
      background: {
        kind: String(parsed?.background?.kind ?? fallback.background.kind),
        value: String(parsed?.background?.value ?? fallback.background.value),
        secondaryValue: String(parsed?.background?.secondaryValue ?? fallback.background.secondaryValue),
        mediaSource: String(parsed?.background?.mediaSource ?? fallback.background.mediaSource),
      },
      texts: normalizeTextLayers(parsed, fallback),
      image: {
        source: String(parsed?.image?.source ?? fallback.image.source),
        size: String(parsed?.image?.size ?? fallback.image.size),
        position: String(parsed?.image?.position ?? fallback.image.position),
        margin: Number(parsed?.image?.margin ?? fallback.image.margin),
      },
      states: {
        pressed: String(parsed?.states?.pressed ?? fallback.states.pressed),
        locked: String(parsed?.states?.locked ?? fallback.states.locked),
      },
    };
  } catch {
    return fallback;
  }
}

export function generatedComponentCode(component?: ComponentDefinition, config?: VisualConfig) {
  const visual = config ?? defaultVisualConfig(component);
  const title = escapeSingleQuote(component?.name ?? "新组件");
  const background = visualBackgroundCode(visual);
  const textNodes = visual.texts.map((text) => {
    const content = escapeHtml(text.content || "文字");
    return `    <span class="onedesk-text-layer" style="left: ${normalizePercent(text.x, 50)}%; top: ${normalizePercent(text.y, 50)}%; font-size: ${Number(text.fontSize) || 14}px; color: ${escapeHtml(text.color || "#ffffff")};">${content}</span>`;
  }).join("\n");

  return `<script setup lang="ts">
const title = '${title}'
<\/script>

<template>
  <button class="onedesk-control-tile" type="button" aria-label="${escapeHtml(component?.name ?? "新组件")}">
${background.template}
${textNodes || "    <span class=\"onedesk-text-layer\">{{ title }}</span>"}
  </button>
</template>

<style scoped>
.onedesk-control-tile {
  position: relative;
  width: 100%;
  height: 100%;
  margin: ${Number(visual.base.margin) || 0}px;
  border: 0;
  border-radius: ${Number(visual.base.borderRadius) || 0}px;
  overflow: hidden;
  background: ${background.css};
  color: white;
  font-weight: 700;
}

.onedesk-video-background {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.onedesk-text-layer {
  position: absolute;
  z-index: 1;
  transform: translate(-50%, -50%);
  white-space: nowrap;
  pointer-events: none;
}
</style>`;
}

export function componentPreviewStyle(config: VisualConfig): Record<string, string> {
  const bg = config.background;
  const base = config.base;
  return {
    background: visualBackgroundToCss(bg, config.image.size),
    borderRadius: `${base.borderRadius}px`,
    margin: `${base.margin}px`,
  };
}

export function componentTileStyle(config?: VisualConfig | null): Record<string, string> {
  if (!config) return {};
  return {
    background: visualBackgroundToCss(config.background, config.image.size),
    borderRadius: `${config.base.borderRadius}px`,
  };
}

export function pageBackgroundStyle(page?: PageDefinition): Record<string, string> {
  if (!page) return {};
  if (page.backgroundKind === "solid") return { background: page.backgroundValue };
  if (page.backgroundKind === "gradient") {
    return {
      background:
        gradientPresets[page.backgroundValue] ??
        `linear-gradient(135deg, ${page.backgroundValue || "#0ea5e9"}, ${page.backgroundSecondaryValue || "#22d3ee"})`,
    };
  }
  if (page.backgroundKind === "image" && page.backgroundMediaSource) {
    return {
      backgroundImage: `url(${page.backgroundMediaSource})`,
      backgroundSize: "cover",
      backgroundPosition: "center",
    };
  }
  if (page.backgroundKind === "video" && page.backgroundMediaSource) return { background: "#0f172a" };
  return { background: "#0ea5e9" };
}

export function pageGridStyle(page: PageDefinition | undefined, metrics: PageGridMetrics): Record<string, string> {
  const rows = clampGridCount(page?.rows ?? 3);
  const columns = clampGridCount(page?.columns ?? 3);
  const rowGap = Math.max(0, Number(page?.spacing.rowGap ?? 8));
  const columnGap = Math.max(0, Number(page?.spacing.columnGap ?? 8));
  const padding = Math.max(0, Number(page?.spacing.padding ?? 12));
  const cellSize = calculateSquareCellSize(metrics, rows, columns, rowGap, columnGap, padding);
  const horizontal = page?.gridHorizontalAlign ?? "center";
  const vertical = page?.gridVerticalAlign ?? "center";

  return {
    "--page-grid-columns": String(columns),
    "--page-grid-rows": String(rows),
    "--page-grid-row-gap": `${rowGap}px`,
    "--page-grid-column-gap": `${columnGap}px`,
    "--page-grid-padding": `${padding}px`,
    "--page-grid-cell-size": `${cellSize}px`,
    "--page-grid-justify": horizontal === "left" ? "start" : horizontal === "right" ? "end" : "center",
    "--page-grid-align": vertical === "top" ? "start" : vertical === "bottom" ? "end" : "center",
    gridTemplateColumns: cellSize > 0 ? `repeat(${columns}, ${cellSize}px)` : `repeat(${columns}, minmax(0, 1fr))`,
    gridTemplateRows: cellSize > 0 ? `repeat(${rows}, ${cellSize}px)` : `repeat(${rows}, minmax(0, 1fr))`,
  };
}

export function textPositionStyle(position: string, index: number, total: number, x?: number, y?: number): Record<string, string> {
  if (Number.isFinite(x) && Number.isFinite(y)) {
    return {
      left: `${normalizePercent(x, 50)}%`,
      top: `${normalizePercent(y, 50)}%`,
      transform: "translate(-50%, -50%)",
    };
  }

  const base: Record<string, string> = {};
  if (position === "left") {
    base.left = "8px";
    base.top = "50%";
    base.transform = "translateY(-50%)";
  } else if (position === "right") {
    base.right = "8px";
    base.top = "50%";
    base.transform = "translateY(-50%)";
  } else if (position === "top") {
    base.top = "8px";
    base.left = "50%";
    base.transform = "translateX(-50%)";
  } else if (position === "bottom") {
    base.bottom = "8px";
    base.left = "50%";
    base.transform = "translateX(-50%)";
  } else {
    base.top = "50%";
    base.left = "50%";
    base.transform = "translate(-50%, -50%)";
  }

  if (total > 1) {
    const offset = (index - (total - 1) / 2) * 24;
    if (position === "top" || position === "bottom") {
      base.transform = `translateX(calc(-50% + ${offset}px))`;
    } else if (position === "left" || position === "right") {
      base.transform = `translateY(calc(-50% + ${offset}px))`;
    } else {
      base.transform = `translate(-50%, calc(-50% + ${offset}px))`;
    }
  }

  return base;
}

export function visualVideoSource(config?: VisualConfig | null) {
  if (!config || config.background.kind !== "video") return "";
  return config.background.mediaSource || "";
}

export function applyDpiScaling(root: HTMLElement, dpr = window.devicePixelRatio || 1) {
  const remBase = Math.round(Math.min(Math.max(16, 16 + (dpr - 1) * 4), 19));
  root.style.setProperty("--onedesk-rem-base", String(remBase));
}

function visualBackgroundToCss(background: VisualConfig["background"], imageSize: string) {
  if (background.kind === "solid") return background.value;
  if (background.kind === "gradient") return `linear-gradient(135deg, ${background.value}, ${background.secondaryValue})`;
  if (background.kind === "image" && background.mediaSource) return `url(${background.mediaSource}) center / ${imageSize} no-repeat`;
  if (background.kind === "video" && background.mediaSource) return "#0f172a";
  return gradientPresets["sky-cyan"];
}

function visualBackgroundCode(config: VisualConfig) {
  const background = config.background;
  if (background.kind === "solid") return { css: background.value || "#0ea5e9", template: "" };
  if (background.kind === "gradient") return { css: `linear-gradient(135deg, ${background.value || "#0ea5e9"}, ${background.secondaryValue || "#22d3ee"})`, template: "" };
  if (background.kind === "image" && background.mediaSource) return { css: `url('${escapeSingleQuote(background.mediaSource)}') center / ${config.image.size || "cover"} no-repeat`, template: "" };
  if (background.kind === "video" && background.mediaSource) {
    return {
      css: "#0f172a",
      template: `    <video class="onedesk-video-background" src="${escapeHtml(background.mediaSource)}" autoplay muted loop playsinline></video>`,
    };
  }
  return { css: gradientPresets["sky-cyan"], template: "" };
}

function normalizeTextLayers(parsed: Record<string, unknown>, fallback: VisualConfig) {
  const textLayers = parsed?.texts;
  if (Array.isArray(textLayers)) {
    return textLayers.map((item, index) => {
      const layer = item as Partial<VisualTextLayer>;
      const fallbackPosition = fallbackTextPosition(layer.position ?? "center", index, textLayers.length);
      return {
        id: String(layer.id ?? `text-${index + 1}`),
        content: String(layer.content ?? ""),
        fontSize: Number(layer.fontSize ?? 14),
        color: String(layer.color ?? "#ffffff"),
        position: String(layer.position ?? "center"),
        x: normalizePercent(layer.x, fallbackPosition.x),
        y: normalizePercent(layer.y, fallbackPosition.y),
      };
    });
  }

  const legacyText = parsed?.text as Partial<VisualTextLayer> | undefined;
  if (legacyText) {
    const fallbackPosition = fallbackTextPosition(legacyText.position ?? "center");
    return [
      {
        id: "text-1",
        content: String(legacyText.content ?? ""),
        fontSize: Number(legacyText.fontSize ?? 14),
        color: String(legacyText.color ?? "#ffffff"),
        position: String(legacyText.position ?? "center"),
        x: normalizePercent(legacyText.x, fallbackPosition.x),
        y: normalizePercent(legacyText.y, fallbackPosition.y),
      },
    ];
  }

  return fallback.texts;
}

function fallbackTextPosition(position: string, index = 0, total = 1) {
  const offset = total > 1 ? (index - (total - 1) / 2) * 8 : 0;
  if (position === "left") return { x: 12, y: clampPercent(50 + offset) };
  if (position === "right") return { x: 88, y: clampPercent(50 + offset) };
  if (position === "top") return { x: clampPercent(50 + offset), y: 12 };
  if (position === "bottom") return { x: clampPercent(50 + offset), y: 88 };
  return { x: 50, y: clampPercent(50 + offset) };
}

function calculateSquareCellSize(metrics: PageGridMetrics, rows: number, columns: number, rowGap: number, columnGap: number, padding: number) {
  if (metrics.width <= 0 || metrics.height <= 0) return 0;

  // 页面编辑器的格子必须保持 1:1。这里按宽高分别计算最大可用边长，再取较小值。
  const availableWidth = metrics.width - padding * 2 - columnGap * Math.max(0, columns - 1);
  const availableHeight = metrics.height - padding * 2 - rowGap * Math.max(0, rows - 1);
  return Math.max(0, Math.floor(Math.min(availableWidth / columns, availableHeight / rows)));
}

function clampGridCount(value: number) {
  return Math.max(1, Math.min(12, Number.isFinite(value) ? Math.floor(value) : 1));
}

function normalizePercent(value: unknown, fallback: number) {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? clampPercent(parsed) : fallback;
}

function clampPercent(value: number) {
  return Math.max(0, Math.min(100, value));
}

function escapeSingleQuote(value: string) {
  return value.replace(/\\/g, "\\\\").replace(/'/g, "\\'");
}

function escapeHtml(value: string) {
  return value
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}
