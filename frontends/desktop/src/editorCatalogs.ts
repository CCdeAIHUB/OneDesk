import type { TriggerDefinition } from "./domain";

export interface TriggerOption {
  id: string;
  category: string;
  displayName: string;
  fingerCount?: number;
  platformLimited?: boolean;
}

const triggerGroups: Array<{ category: string; label: string; triggers: Omit<TriggerOption, "category">[] }> = [
  {
    category: "touch.standard",
    label: "标准触摸",
    triggers: [
      { id: "tap", displayName: "单击" },
      { id: "double-tap", displayName: "双击" },
      { id: "long-press", displayName: "长按" },
      { id: "press-and-hold", displayName: "按住" },
      { id: "swipe-up", displayName: "上滑", fingerCount: 1 },
      { id: "swipe-down", displayName: "下滑", fingerCount: 1 },
      { id: "swipe-left", displayName: "左滑", fingerCount: 1 },
      { id: "swipe-right", displayName: "右滑", fingerCount: 1 },
      { id: "horizontal-swipe", displayName: "横向滑动", fingerCount: 1 },
      { id: "vertical-swipe", displayName: "纵向滑动", fingerCount: 1 },
      { id: "pinch-in", displayName: "捏合" },
      { id: "pinch-out", displayName: "张开" },
      { id: "rotate", displayName: "旋转" },
    ],
  },
  {
    category: "touch.multi",
    label: "多指触摸",
    triggers: [2, 3, 4, 5].flatMap((finger) =>
      ["up", "down", "left", "right"].map((direction) => ({
        id: `${finger}-finger-swipe-${direction}`,
        displayName: `${finger}指${direction === "up" ? "上" : direction === "down" ? "下" : direction === "left" ? "左" : "右"}滑`,
        fingerCount: finger,
      })),
    ),
  },
  {
    category: "sensor",
    label: "设备传感器",
    triggers: [
      { id: "shake", displayName: "摇晃" },
      { id: "orientation-change", displayName: "方向变化" },
      { id: "tilt-up", displayName: "向上倾斜" },
      { id: "tilt-down", displayName: "向下倾斜" },
      { id: "tilt-left", displayName: "向左倾斜" },
      { id: "tilt-right", displayName: "向右倾斜" },
    ],
  },
];

export const triggerOptions: TriggerOption[] = triggerGroups.flatMap((group) =>
  group.triggers.map((trigger) => ({ ...trigger, category: group.category })),
);
export const triggerSelectOptions = triggerGroups.flatMap((group) =>
  group.triggers.map((trigger) => ({ value: trigger.id, label: trigger.displayName, group: group.label })),
);
export const layoutOptions = [
  { value: "center", label: "居中" }, { value: "left", label: "靠左" },
  { value: "right", label: "靠右" }, { value: "bottom", label: "靠下" },
];
export const backgroundKindOptions = [
  { value: "gradient", label: "渐变背景" }, { value: "solid", label: "纯色背景" },
  { value: "image", label: "图片背景" }, { value: "video", label: "视频背景" },
];
export const imageSizeOptions = [
  { value: "cover", label: "填充覆盖" }, { value: "contain", label: "完整显示" },
];
export const positionOptions = [
  { value: "center", label: "居中" }, { value: "left", label: "靠左" },
  { value: "right", label: "靠右" }, { value: "top", label: "靠上" }, { value: "bottom", label: "靠下" },
];
export const horizontalAlignOptions = positionOptions.filter((option) => ["left", "center", "right"].includes(option.value));
export const verticalAlignOptions = positionOptions.filter((option) => ["top", "center", "bottom"].includes(option.value));
export const pressedStateOptions = [
  { value: "scale-95", label: "按下缩小" }, { value: "brightness-110", label: "按下高亮" }, { value: "none", label: "无" },
];
export const lockedStateOptions = [
  { value: "opacity-60", label: "降低透明度" }, { value: "grayscale", label: "增加灰度蒙层" }, { value: "none", label: "无" },
];
export const outlineStyleOptions = [
  { value: "solid", label: "实线" }, { value: "dashed", label: "虚线" }, { value: "dotted", label: "点线" },
];
export const animationOptions = [
  { value: "fade", label: "渐入渐退" }, { value: "slide", label: "滑动" }, { value: "none", label: "无动画" },
];
export const themeOptions = [
  { value: "system", label: "跟随系统" }, { value: "light", label: "浅色" }, { value: "dark", label: "深色" },
];
export const languageOptions = [{ value: "zh-CN", label: "简体中文" }];

export function findTrigger(id: string): TriggerOption {
  return triggerOptions.find((trigger) => trigger.id === id) ?? { id, displayName: id, category: "touch.standard" };
}

export function buildTriggerDefinition(trigger: TriggerOption): TriggerDefinition {
  return {
    id: trigger.id,
    category: trigger.category,
    displayName: trigger.displayName,
    fingerCount: trigger.fingerCount ?? (trigger.category === "sensor" ? 0 : 1),
    platformLimited: trigger.platformLimited,
  };
}

export function triggerLabel(trigger: { id: string; displayName: string }) {
  return trigger.displayName || findTrigger(trigger.id).displayName;
}
