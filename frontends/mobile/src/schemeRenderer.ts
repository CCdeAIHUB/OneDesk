import type { ComponentBundle, GridCellDefinition, PageDefinition, VisualConfig, VisualTextLayer } from "./domain";

export function buildGridStyle(page: PageDefinition, viewportWidth: number, viewportHeight: number): Record<string, string> {
  const rows = clamp(page.rows, 1, 12);
  const columns = clamp(page.columns, 1, 12);
  const padding = Math.max(0, Number(page.spacing?.padding ?? 0));
  const rowGap = Math.max(0, Number(page.spacing?.rowGap ?? 0));
  const columnGap = Math.max(0, Number(page.spacing?.columnGap ?? 0));
  const availableWidth = viewportWidth - padding * 2 - columnGap * Math.max(0, columns - 1);
  const availableHeight = viewportHeight - padding * 2 - rowGap * Math.max(0, rows - 1);
  const cellSize = Math.max(1, Math.floor(Math.min(availableWidth / columns, availableHeight / rows)));
  return {
    gridTemplateColumns: `repeat(${columns}, ${cellSize}px)`,
    gridTemplateRows: `repeat(${rows}, ${cellSize}px)`,
    columnGap: `${columnGap}px`,
    rowGap: `${rowGap}px`,
    padding: `${padding}px`,
    justifyContent: page.gridHorizontalAlign === "left" ? "start" : page.gridHorizontalAlign === "right" ? "end" : "center",
    alignContent: page.gridVerticalAlign === "top" ? "start" : page.gridVerticalAlign === "bottom" ? "end" : "center",
  };
}

export function pageBackgroundStyle(page: PageDefinition): Record<string, string> {
  if (page.backgroundKind === "solid") return { background: page.backgroundValue || "#ffffff" };
  if (page.backgroundKind === "gradient") {
    return { background: `linear-gradient(135deg, ${page.backgroundValue || "#0ea5e9"}, ${page.backgroundSecondaryValue || "#22d3ee"})` };
  }
  if (page.backgroundKind === "image" && page.backgroundMediaSource) {
    return { background: `url('${cssUrl(page.backgroundMediaSource)}') center / cover no-repeat` };
  }
  return { background: page.backgroundKind === "video" ? "#020617" : "#ffffff" };
}

export function cellStyle(cell: GridCellDefinition): Record<string, string> {
  return {
    gridColumn: `${cell.column} / span ${Math.max(1, cell.columnSpan)}`,
    gridRow: `${cell.row} / span ${Math.max(1, cell.rowSpan)}`,
    borderRadius: `${Math.max(0, cell.style?.borderRadius ?? 0)}px`,
    border: `${Math.max(0, cell.style?.outlineWidth ?? 0)}px ${cell.style?.outlineStyle || "solid"} ${cell.style?.outlineColor || "transparent"}`,
  };
}

export function componentStyle(bundle: ComponentBundle | undefined): Record<string, string> {
  const config = bundle?.visualConfig;
  if (!config) return { background: "#e2e8f0" };
  const background = config.background;
  // 格子负责页面间距和轮廓，组件根节点必须填满格子；外边距会让 100% 尺寸节点产生偏移并被裁剪。
  const style: Record<string, string> = {
    boxSizing: "border-box",
    borderRadius: `${Math.max(0, config.base?.borderRadius ?? 0)}px`,
  };
  if (background.kind === "solid") style.background = background.value || "#0ea5e9";
  else if (background.kind === "gradient") style.background = `linear-gradient(135deg, ${background.value || "#0ea5e9"}, ${background.secondaryValue || "#22d3ee"})`;
  else if (background.kind === "image" && background.mediaSource) {
    style.background = `url('${cssUrl(background.mediaSource)}') center / ${config.image?.size || "cover"} no-repeat`;
  } else style.background = "#0f172a";
  return style;
}

export function textStyle(text: VisualTextLayer): Record<string, string> {
  return {
    left: `${clamp(Number(text.x), 0, 100)}%`,
    top: `${clamp(Number(text.y), 0, 100)}%`,
    width: `${clamp(Number(text.width), 4, 100)}%`,
    minHeight: `${clamp(Number(text.height), 4, 100)}%`,
    color: text.color || "#ffffff",
    fontSize: `${Math.max(6, Number(text.fontSize) || 14)}px`,
  };
}

export function imageStyle(config: VisualConfig): Record<string, string> {
  const positions: Record<string, string> = { left: "left", right: "right", top: "top", bottom: "bottom", center: "center" };
  return {
    objectFit: config.image.size === "contain" ? "contain" : "cover",
    objectPosition: positions[config.image.position] || "center",
    padding: `${Math.max(0, Number(config.image.margin) || 0)}px`,
  };
}

function cssUrl(value: string) {
  return value.replace(/[\\'\n\r]/g, (character) => `\\${character}`);
}

function clamp(value: number, minimum: number, maximum: number) {
  return Math.min(maximum, Math.max(minimum, Number.isFinite(value) ? value : minimum));
}
