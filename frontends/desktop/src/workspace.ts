import { reactive } from "vue";
import type {
  ComponentSummary,
  NavigationItem,
  PageSummary,
  PermissionItem,
  QuickAction,
  QuickStartItem,
  SchemeSummary,
} from "./domain";

export const navItems: NavigationItem[] = [
  { key: "home", label: "首页", icon: "solar:widget-2-bold-duotone" },
  { key: "component", label: "组件", icon: "solar:card-bold-duotone" },
  { key: "page", label: "页面", icon: "solar:layers-bold-duotone" },
  { key: "scheme", label: "方案", icon: "solar:play-circle-bold-duotone" },
  { key: "plugin", label: "插件", icon: "solar:plug-circle-bold-duotone" },
  { key: "permission", label: "设置", icon: "solar:settings-bold-duotone" },
  { key: "log", label: "账户", icon: "solar:user-rounded-bold-duotone" },
];

export const workspace = reactive({
  activeScheme: "直播控制台",
  selectedDevice: "OneDesk Stream Deck",
  toast: "方案缓存已校验",
  selectedComponentId: "scene-switch",
  selectedPageId: "capture",
  selectedSchemeId: "live-console",
});

export const quickActions: QuickAction[] = [
  { label: "创建新方案", icon: "solar:add-circle-bold-duotone", color: "text-sky-500" },
  { label: "导入方案", icon: "solar:download-minimalistic-bold-duotone", color: "text-green-500" },
  { label: "打开动作编辑器", icon: "solar:bolt-bold-duotone", color: "text-violet-500" },
];

export const quickStart: QuickStartItem[] = [
  { label: "连接新设备", desc: "连接并设置新的控制设备", icon: "solar:usb-bold-duotone", color: "text-sky-500" },
  { label: "浏览插件", desc: "扩展你的 OneDesk 能力", icon: "solar:plug-circle-bold-duotone", color: "text-green-500" },
  { label: "使用帮助", desc: "查看使用文档和教程", icon: "solar:question-circle-bold-duotone", color: "text-violet-500" },
];

export const components: ComponentSummary[] = [
  { id: "scene-switch", name: "场景切换", mode: "可视化", actions: 3, ratio: "1:1", status: "已授权" },
  { id: "volume-strip", name: "音量推子", mode: "代码", actions: 5, ratio: "2:3", status: "缺少插件" },
  { id: "asset-marker", name: "素材标记", mode: "可视化", actions: 4, ratio: "1:1", status: "待确认" },
  { id: "focus-timer", name: "专注计时", mode: "可视化", actions: 2, ratio: "4:6", status: "已授权" },
];

export const pages: PageSummary[] = [
  { id: "capture", name: "采集", grid: "4 x 3", components: 9, background: "渐变" },
  { id: "live", name: "直播", grid: "5 x 3", components: 12, background: "视频" },
  { id: "edit", name: "剪辑", grid: "4 x 4", components: 10, background: "纯色" },
  { id: "system", name: "系统", grid: "3 x 3", components: 6, background: "图片" },
];

export const schemes: SchemeSummary[] = [
  { id: "live-console", name: "直播控制台", pages: 4, devices: 1, status: "已应用" },
  { id: "edit-workspace", name: "剪辑工作台", pages: 3, devices: 0, status: "未应用" },
  { id: "game-profile", name: "游戏配置", pages: 5, devices: 1, status: "有更新" },
];

export const permissions: PermissionItem[] = [
  { id: "file.writeExternal", category: "文件管理", label: "修改私有目录外文件", risk: "高危" },
  { id: "plugin.invoke", category: "插件", label: "调用桌面端插件方法", risk: "普通" },
  { id: "notification.native", category: "通知", label: "发送系统通知", risk: "普通" },
  { id: "input.keyboardMouseSimulation", category: "输入控制", label: "模拟键盘和鼠标", risk: "高危" },
];

export const logs = ["小米平板 6 已连接", "断联日志已上传，共 12 条", "OBS Control 插件权限已更新", "页面切换动画已保存"];
