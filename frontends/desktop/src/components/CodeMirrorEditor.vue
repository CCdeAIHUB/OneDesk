<script setup lang="ts">
import { EditorView, keymap } from "@codemirror/view";
import { EditorState } from "@codemirror/state";
import { defaultKeymap, history, historyKeymap, indentWithTab } from "@codemirror/commands";
import { bracketMatching, defaultHighlightStyle, indentOnInput, syntaxHighlighting } from "@codemirror/language";
import { closeBrackets, closeBracketsKeymap, completionKeymap } from "@codemirror/autocomplete";
import { searchKeymap, highlightSelectionMatches } from "@codemirror/search";
import { lintKeymap } from "@codemirror/lint";
import { vue } from "@codemirror/lang-vue";
import { oneDark } from "@codemirror/theme-one-dark";
import { onBeforeUnmount, onMounted, ref, watch } from "vue";

const model = defineModel<string>({ default: "" });
const props = defineProps<{ filename?: string }>();

const host = ref<HTMLElement | null>(null);
let view: EditorView | null = null;
let syncingFromEditor = false;

const editorTheme = EditorView.theme({
  "&": {
    height: "100%",
    backgroundColor: "#020617",
    fontSize: "12px",
  },
  ".cm-scroller": {
    fontFamily: "Consolas, 'JetBrains Mono', 'SFMono-Regular', monospace",
    lineHeight: "1.65",
  },
  ".cm-content": {
    padding: "14px 0",
  },
  ".cm-gutters": {
    backgroundColor: "#020617",
    borderRight: "1px solid rgba(148, 163, 184, 0.18)",
  },
  ".cm-activeLineGutter, .cm-activeLine": {
    backgroundColor: "rgba(14, 165, 233, 0.12)",
  },
  ".cm-selectionBackground": {
    backgroundColor: "rgba(14, 165, 233, 0.28) !important",
  },
});

const extensions = [
  history(),
  indentOnInput(),
  bracketMatching(),
  closeBrackets(),
  highlightSelectionMatches(),
  syntaxHighlighting(defaultHighlightStyle, { fallback: true }),
  vue(),
  oneDark,
  editorTheme,
  EditorView.lineWrapping,
  EditorView.updateListener.of((update) => {
    if (!update.docChanged) return;
    syncingFromEditor = true;
    model.value = update.state.doc.toString();
    syncingFromEditor = false;
  }),
  keymap.of([
    indentWithTab,
    ...defaultKeymap,
    ...historyKeymap,
    ...closeBracketsKeymap,
    ...searchKeymap,
    ...completionKeymap,
    ...lintKeymap,
  ]),
];

onMounted(() => {
  if (!host.value) return;
  view = new EditorView({
    parent: host.value,
    state: EditorState.create({
      doc: model.value,
      extensions,
    }),
  });
});

watch(model, (value) => {
  if (!view || syncingFromEditor || value === view.state.doc.toString()) return;
  view.dispatch({
    changes: { from: 0, to: view.state.doc.length, insert: value },
  });
});

watch(() => props.filename, () => {
  requestAnimationFrame(() => view?.focus());
});

onBeforeUnmount(() => {
  view?.destroy();
  view = null;
});
</script>

<template>
  <div ref="host" class="h-full min-h-0 w-full overflow-hidden" data-no-window-drag></div>
</template>
