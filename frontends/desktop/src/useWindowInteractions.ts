import { onUnmounted, type Ref } from "vue";
import { maximizeWindow, moveWindowBy, startWindowResize } from "./nativeBridge";

export function useWindowInteractions(isMaximized: Ref<boolean>) {
  let pointerId = -1;
  let lastScreenX = 0;
  let lastScreenY = 0;
  let pendingX = 0;
  let pendingY = 0;
  let frame = 0;

  async function toggleMaximize() {
    isMaximized.value = await maximizeWindow();
  }

  function handleWindowDrag(event: PointerEvent) {
    if (event.button !== 0) return;
    const target = event.target instanceof Element ? event.target : null;
    if (!isMaximized.value) {
      const edge = resizeEdgeFromPointer(event);
      if (edge) {
        void startWindowResize(edge);
        return;
      }
    }
    if (isMaximized.value || shouldIgnoreDrag(target)) return;
    if (!target?.closest("header") && !target?.closest("aside")) return;
    const scroller = target.closest(".scrollable");
    if (scroller) {
      const rect = scroller.getBoundingClientRect();
      if (event.clientX >= rect.right - 24 || event.clientY >= rect.bottom - 24) return;
    }
    beginMove(event);
  }

  function handleWindowPointerMove(event: PointerEvent) {
    if (isMaximized.value || event.buttons !== 0 || shouldIgnoreDrag(event.target instanceof Element ? event.target : null)) {
      document.body.style.cursor = "";
      return;
    }
    document.body.style.cursor = resizeCursorMap[resizeEdgeFromPointer(event)] ?? "";
  }

  function beginMove(event: PointerEvent) {
    pointerId = event.pointerId;
    lastScreenX = event.screenX;
    lastScreenY = event.screenY;
    document.documentElement.classList.add("window-moving");
    window.addEventListener("pointermove", move);
    window.addEventListener("pointerup", endMove);
    window.addEventListener("pointercancel", endMove);
    event.preventDefault();
  }

  function move(event: PointerEvent) {
    if (event.pointerId !== pointerId) return;
    pendingX += Math.round(event.screenX - lastScreenX);
    pendingY += Math.round(event.screenY - lastScreenY);
    lastScreenX = event.screenX;
    lastScreenY = event.screenY;
    if (!frame) frame = window.requestAnimationFrame(flushMove);
  }

  function flushMove() {
    frame = 0;
    const deltaX = pendingX;
    const deltaY = pendingY;
    pendingX = 0;
    pendingY = 0;
    if (deltaX || deltaY) void moveWindowBy(deltaX, deltaY);
  }

  function endMove(event: PointerEvent) {
    if (event.pointerId !== pointerId) return;
    pointerId = -1;
    removeMoveListeners();
    if (frame) {
      window.cancelAnimationFrame(frame);
      flushMove();
    }
  }

  function removeMoveListeners() {
    window.removeEventListener("pointermove", move);
    window.removeEventListener("pointerup", endMove);
    window.removeEventListener("pointercancel", endMove);
    document.documentElement.classList.remove("window-moving");
  }

  onUnmounted(removeMoveListeners);
  return { toggleMaximize, handleWindowDrag, handleWindowPointerMove };
}

function resizeEdgeFromPointer(event: PointerEvent) {
  const margin = Math.max(12, Math.round(12 * window.devicePixelRatio));
  const left = event.clientX <= margin;
  const right = event.clientX >= window.innerWidth - margin;
  const top = event.clientY <= margin;
  const bottom = event.clientY >= window.innerHeight - margin;
  if (top && left) return "top-left";
  if (top && right) return "top-right";
  if (bottom && left) return "bottom-left";
  if (bottom && right) return "bottom-right";
  if (left) return "left";
  if (right) return "right";
  if (top) return "top";
  if (bottom) return "bottom";
  return "";
}

function shouldIgnoreDrag(target: Element | null) {
  return Boolean(target?.closest("button,input,select,textarea,a,nav,.soft-card,.soft-row,.soft-start,.theme-dot,.window-controls,.no-drag,.device-menu,[data-no-window-drag]"));
}

const resizeCursorMap: Record<string, string> = {
  left: "ew-resize", right: "ew-resize", top: "ns-resize", bottom: "ns-resize",
  "top-left": "nwse-resize", "bottom-right": "nwse-resize", "top-right": "nesw-resize", "bottom-left": "nesw-resize",
};
