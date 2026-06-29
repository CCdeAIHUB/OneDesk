import type { ComponentDefinition, PageDefinition } from "./domain";

export interface VisualTextLayer {
  id: string;
  content: string;
  fontSize: number;
  color: string;
  position: string;
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

export function generatedComponentCode(component?: ComponentDefinition) {
  const name = component?.name ?? "新组件";
  return `<script setup lang="ts">\nconst title = '${name}'\n<\/script>\n\n<template>\n  <button class="onedesk-control-tile">{{ title }}</button>\n</template>\n\n<style scoped>\n.onedesk-control-tile {\n  width: 100%;\n  height: 100%;\n  border-radius: 16px;\n  overflow: hidden;\n  background: linear-gradient(135deg, #0ea5e9, #22d3ee);\n  color: white;\n  font-size: 14px;\n  font-weight: 700;\n}\n</style>`;
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

export function textPositionStyle(position: string, index: number, total: number): Record<string, string> {
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

function normalizeTextLayers(parsed: Record<string, unknown>, fallback: VisualConfig) {
  const textLayers = parsed?.texts;
  if (Array.isArray(textLayers)) {
    return textLayers.map((item, index) => {
      const layer = item as Partial<VisualTextLayer>;
      return {
        id: String(layer.id ?? `text-${index + 1}`),
        content: String(layer.content ?? ""),
        fontSize: Number(layer.fontSize ?? 14),
        color: String(layer.color ?? "#ffffff"),
        position: String(layer.position ?? "center"),
      };
    });
  }

  const legacyText = parsed?.text as Partial<VisualTextLayer> | undefined;
  if (legacyText) {
    return [
      {
        id: "text-1",
        content: String(legacyText.content ?? ""),
        fontSize: Number(legacyText.fontSize ?? 14),
        color: String(legacyText.color ?? "#ffffff"),
        position: String(legacyText.position ?? "center"),
      },
    ];
  }

  return fallback.texts;
}

function calculateSquareCellSize(metrics: PageGridMetrics, rows: number, columns: number, rowGap: number, columnGap: number, padding: number) {
  if (metrics.width <= 0 || metrics.height <= 0) return 0;

  // 页面编辑器的格子必须保持 1:1。这里按宽高两个方向分别计算最大可用边长，再取较小值。
  const availableWidth = metrics.width - padding * 2 - columnGap * Math.max(0, columns - 1);
  const availableHeight = metrics.height - padding * 2 - rowGap * Math.max(0, rows - 1);
  return Math.max(0, Math.floor(Math.min(availableWidth / columns, availableHeight / rows)));
}

function clampGridCount(value: number) {
  return Math.max(1, Math.min(12, Number.isFinite(value) ? Math.floor(value) : 1));
}
