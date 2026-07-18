import type { TriggerDefinition } from "./domain";

interface GestureState {
  startX: number;
  startY: number;
  startDistance: number;
  startAngle: number;
  maxFingers: number;
  held: boolean;
  holdTimer: number;
}

export class GestureRecognizer {
  private readonly states = new Map<string, GestureState>();
  private readonly lastTapAt = new Map<string, number>();

  constructor(private readonly dispatch: (key: string, target: EventTarget | null, triggerId: string, fingers: number) => void) {}

  start(key: string, event: TouchEvent) {
    const points = Array.from(event.touches);
    if (!points.length) return;
    const center = touchCenter(points);
    this.cancel(key);
    const state: GestureState = {
      startX: center.x,
      startY: center.y,
      startDistance: touchDistance(points),
      startAngle: touchAngle(points),
      maxFingers: points.length,
      held: false,
      holdTimer: 0,
    };
    state.holdTimer = window.setTimeout(() => {
      state.held = true;
      this.dispatch(key, event.currentTarget, "long-press", state.maxFingers);
    }, 620);
    this.states.set(key, state);
  }

  move(key: string, event: TouchEvent) {
    const state = this.states.get(key);
    const points = Array.from(event.touches);
    if (!state || !points.length) return;
    state.maxFingers = Math.max(state.maxFingers, points.length);
    const center = touchCenter(points);
    if (Math.hypot(center.x - state.startX, center.y - state.startY) > 12) window.clearTimeout(state.holdTimer);
  }

  end(key: string, event: TouchEvent) {
    const state = this.states.get(key);
    if (!state) return;
    this.states.delete(key);
    window.clearTimeout(state.holdTimer);
    if (state.held) return;
    const changed = Array.from(event.changedTouches);
    const end = touchCenter(changed);
    const dx = end.x - state.startX;
    const dy = end.y - state.startY;
    const distance = Math.hypot(dx, dy);
    let trigger = "tap";
    if (state.maxFingers >= 2 && changed.length >= 2) {
      const endDistance = touchDistance(changed);
      const endAngle = touchAngle(changed);
      if (state.startDistance > 0 && Math.abs(endDistance - state.startDistance) > 42) {
        trigger = endDistance > state.startDistance ? "pinch-out" : "pinch-in";
      } else if (Math.abs(normalizeAngle(endAngle - state.startAngle)) > 18) {
        trigger = "rotate";
      } else if (distance >= 36) trigger = swipeTrigger(dx, dy, state.maxFingers);
    } else if (distance >= 36) trigger = swipeTrigger(dx, dy, state.maxFingers);
    else {
      const previousTap = this.lastTapAt.get(key) ?? 0;
      const now = performance.now();
      trigger = now - previousTap < 320 ? "double-tap" : "tap";
      this.lastTapAt.set(key, now);
    }
    this.dispatch(key, event.currentTarget, trigger, state.maxFingers);
  }

  cancel(key: string) {
    const state = this.states.get(key);
    if (state) window.clearTimeout(state.holdTimer);
    this.states.delete(key);
  }

  dispose() {
    for (const state of this.states.values()) window.clearTimeout(state.holdTimer);
    this.states.clear();
    this.lastTapAt.clear();
  }
}

export function triggerMatches(trigger: TriggerDefinition | undefined, actual: string, fingers: number) {
  if (!trigger) return false;
  const expectedFingers = Number(trigger.fingerCount || 1);
  if (expectedFingers > 0 && expectedFingers !== fingers) return false;
  if (trigger.id === actual) return true;
  if (trigger.id === "press-and-hold" && actual === "long-press") return true;
  if (trigger.id === "horizontal-swipe" && (actual.endsWith("swipe-left") || actual.endsWith("swipe-right"))) return true;
  return trigger.id === "vertical-swipe" && (actual.endsWith("swipe-up") || actual.endsWith("swipe-down"));
}

function swipeTrigger(dx: number, dy: number, fingers: number) {
  const direction = Math.abs(dx) > Math.abs(dy) ? (dx > 0 ? "right" : "left") : dy > 0 ? "down" : "up";
  return fingers > 1 ? `${fingers}-finger-swipe-${direction}` : `swipe-${direction}`;
}

function touchCenter(points: Touch[]) {
  if (!points.length) return { x: 0, y: 0 };
  return points.reduce((sum, point) => ({ x: sum.x + point.clientX / points.length, y: sum.y + point.clientY / points.length }), { x: 0, y: 0 });
}

function touchDistance(points: Touch[]) {
  return points.length < 2 ? 0 : Math.hypot(points[1].clientX - points[0].clientX, points[1].clientY - points[0].clientY);
}

function touchAngle(points: Touch[]) {
  return points.length < 2 ? 0 : Math.atan2(points[1].clientY - points[0].clientY, points[1].clientX - points[0].clientX) * 180 / Math.PI;
}

function normalizeAngle(value: number) {
  let result = value;
  while (result > 180) result -= 360;
  while (result < -180) result += 360;
  return result;
}
