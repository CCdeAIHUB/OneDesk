export type ViewKey = "home" | "component" | "page" | "scheme" | "plugin" | "permission";
export type ThemeMode = "light" | "dark" | "system";
export type SectionRoute = "manager" | "editor";

export interface NavigationItem {
  key: ViewKey;
  label: string;
  icon: string;
}

export interface ComponentSummary {
  id: string;
  name: string;
  mode: "可视化" | "代码";
  actions: number;
  ratio: string;
  status: "已授权" | "缺少插件" | "待确认";
}

export interface PageSummary {
  id: string;
  name: string;
  grid: string;
  components: number;
  background: string;
}

export interface SchemeSummary {
  id: string;
  name: string;
  pages: number;
  devices: number;
  status: "已应用" | "未应用" | "有更新";
}

export interface PermissionItem {
  id: string;
  category: string;
  label: string;
  risk: "普通" | "高危";
}

export interface QuickAction {
  label: string;
  icon: string;
  color: string;
}

export interface QuickStartItem extends QuickAction {
  desc: string;
}
