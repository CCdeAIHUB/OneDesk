export const uiColorScheme = {
  theme: "sky-500",
  shell: {
    light: "bg-white/74 backdrop-blur-xl",
    dark: "bg-black/82 backdrop-blur-xl",
  },
  surface: {
    level1: {
      light: "#ffffff",
      dark: "#0f172a",
    },
    level2: {
      light: "#f1f5f9",
      dark: "#1e293b",
    },
    level3: {
      light: "#eaf6ff",
      dark: "#020617",
    },
  },
  border: {
    light: "rgba(203,213,225,0.78)",
    dark: "rgba(51,65,85,0.88)",
  },
  text: {
    primaryLight: "#0f172a",
    primaryDark: "#f8fafc",
    secondaryLight: "#64748b",
    secondaryDark: "#94a3b8",
  },
} as const;
