<script setup lang="ts">
import { Icon } from "@iconify/vue";
import { computed, nextTick, onBeforeUnmount, onMounted, ref, useAttrs, watch } from "vue";

defineOptions({ inheritAttrs: false });

interface SelectOption {
  value: any;
  label: string;
  group?: string;
  disabled?: boolean;
}

const props = withDefaults(defineProps<{
  modelValue?: any;
  options: SelectOption[];
  placeholder?: string;
  disabled?: boolean;
  ariaLabel?: string;
}>(), {
  modelValue: null,
  placeholder: "请选择",
  disabled: false,
  ariaLabel: "选择选项",
});

const emit = defineEmits<{
  "update:modelValue": [value: any];
  change: [value: any];
}>();
const attrs = useAttrs();

const trigger = ref<HTMLButtonElement | null>(null);
const menu = ref<HTMLElement | null>(null);
const open = ref(false);
const activeIndex = ref(-1);
const menuStyle = ref<Record<string, string>>({});

const selectedOption = computed(() => props.options.find((option) => sameValue(option.value, props.modelValue)) ?? null);

function sameValue(left: any, right: any) {
  if (left === right) return true;
  if (left === null || left === undefined || right === null || right === undefined) return false;
  return String(left) === String(right);
}

function enabledIndexFrom(start: number, direction: 1 | -1) {
  if (!props.options.length) return -1;
  let index = start;
  for (let attempt = 0; attempt < props.options.length; attempt += 1) {
    index = (index + direction + props.options.length) % props.options.length;
    if (!props.options[index]?.disabled) return index;
  }
  return -1;
}

function updateMenuPosition() {
  const element = trigger.value;
  if (!element) return;
  const rect = element.getBoundingClientRect();
  const viewportPadding = 12;
  const below = window.innerHeight - rect.bottom - viewportPadding;
  const above = rect.top - viewportPadding;
  const placeAbove = below < 180 && above > below;
  const maxHeight = Math.max(120, Math.min(320, placeAbove ? above - 8 : below - 8));
  const width = Math.max(rect.width, 180);
  const left = Math.min(Math.max(viewportPadding, rect.left), Math.max(viewportPadding, window.innerWidth - width - viewportPadding));
  menuStyle.value = {
    left: `${Math.round(left)}px`,
    width: `${Math.round(width)}px`,
    maxHeight: `${Math.round(maxHeight)}px`,
    ...(placeAbove
      ? { bottom: `${Math.round(window.innerHeight - rect.top + 6)}px` }
      : { top: `${Math.round(rect.bottom + 6)}px` }),
  };
}

async function openMenu() {
  if (props.disabled || !props.options.length) return;
  open.value = true;
  const selectedIndex = props.options.findIndex((option) => sameValue(option.value, props.modelValue) && !option.disabled);
  activeIndex.value = selectedIndex >= 0 ? selectedIndex : enabledIndexFrom(-1, 1);
  await nextTick();
  updateMenuPosition();
  menu.value?.querySelector<HTMLElement>(`[data-option-index="${activeIndex.value}"]`)?.scrollIntoView({ block: "nearest" });
}

function closeMenu(restoreFocus = false) {
  open.value = false;
  activeIndex.value = -1;
  if (restoreFocus) nextTick(() => trigger.value?.focus());
}

function toggleMenu() {
  if (open.value) closeMenu();
  else void openMenu();
}

function choose(option: SelectOption) {
  if (option.disabled) return;
  emit("update:modelValue", option.value);
  emit("change", option.value);
  closeMenu(true);
}

function handleKeydown(event: KeyboardEvent) {
  if (props.disabled) return;
  if (event.key === "Escape") {
    if (open.value) {
      event.preventDefault();
      event.stopPropagation();
      closeMenu(true);
    }
    return;
  }
  if (event.key === "ArrowDown" || event.key === "ArrowUp") {
    event.preventDefault();
    if (!open.value) {
      void openMenu();
      return;
    }
    activeIndex.value = enabledIndexFrom(activeIndex.value, event.key === "ArrowDown" ? 1 : -1);
    nextTick(() => menu.value?.querySelector<HTMLElement>(`[data-option-index="${activeIndex.value}"]`)?.scrollIntoView({ block: "nearest" }));
    return;
  }
  if (event.key === "Home" || event.key === "End") {
    if (!open.value) return;
    event.preventDefault();
    activeIndex.value = event.key === "Home"
      ? enabledIndexFrom(-1, 1)
      : enabledIndexFrom(0, -1);
    return;
  }
  if (event.key === "Enter" || event.key === " ") {
    event.preventDefault();
    if (!open.value) {
      void openMenu();
      return;
    }
    const option = props.options[activeIndex.value];
    if (option) choose(option);
  }
}

function handleDocumentPointerDown(event: PointerEvent) {
  if (!open.value) return;
  const target = event.target;
  if (target instanceof Node && (trigger.value?.contains(target) || menu.value?.contains(target))) return;
  closeMenu();
}

function handleViewportChange() {
  if (open.value) updateMenuPosition();
}

watch(() => props.options, () => {
  if (open.value && !props.options.length) closeMenu();
}, { deep: true });

onMounted(() => {
  document.addEventListener("pointerdown", handleDocumentPointerDown, true);
  window.addEventListener("resize", handleViewportChange);
  window.addEventListener("scroll", handleViewportChange, true);
});

onBeforeUnmount(() => {
  document.removeEventListener("pointerdown", handleDocumentPointerDown, true);
  window.removeEventListener("resize", handleViewportChange);
  window.removeEventListener("scroll", handleViewportChange, true);
});
</script>

<template>
  <button
    ref="trigger"
    v-bind="attrs"
    type="button"
    class="field ui-select-trigger"
    :class="{ 'ui-select-open': open }"
    :disabled="disabled"
    role="combobox"
    aria-haspopup="listbox"
    :aria-label="ariaLabel"
    :aria-expanded="open"
    data-no-window-drag
    @click="toggleMenu"
    @keydown="handleKeydown"
  >
    <span class="min-w-0 flex-1 truncate text-left" :class="selectedOption ? '' : 'text-slate-400'">{{ selectedOption?.label ?? placeholder }}</span>
    <Icon icon="solar:alt-arrow-down-linear" class="size-4 shrink-0 transition-transform" :class="open ? 'rotate-180 text-sky-500' : 'text-slate-400'" />
  </button>

  <Teleport to="body">
    <div
      v-if="open"
      ref="menu"
      class="ui-select-menu scrollable"
      :style="menuStyle"
      role="listbox"
      data-no-window-drag
      @keydown="handleKeydown"
    >
      <button
        v-for="(option, index) in options"
        :key="`${String(option.value)}-${index}`"
        type="button"
        class="ui-select-option"
        :class="{
          'ui-select-option-active': activeIndex === index,
          'ui-select-option-selected': sameValue(option.value, modelValue),
        }"
        :disabled="option.disabled"
        :data-option-index="index"
        role="option"
        :aria-selected="sameValue(option.value, modelValue)"
        @pointerenter="!option.disabled && (activeIndex = index)"
        @click="choose(option)"
      >
        <span class="min-w-0 flex-1">
          <span v-if="option.group" class="mr-1 text-[10px] text-slate-400">{{ option.group }}</span>
          <span>{{ option.label }}</span>
        </span>
        <Icon v-if="sameValue(option.value, modelValue)" icon="solar:check-circle-bold" class="size-4 shrink-0 text-sky-500" />
      </button>
    </div>
  </Teleport>
</template>
