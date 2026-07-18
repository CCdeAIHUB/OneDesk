export interface FrameSize { width: number; height: number }

export function normalizeRatioNumber(value: number, fallback: number) {
  const parsed = Number(value);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
}

export function calculatePreviewFrameSize(
  parentWidth: number,
  parentHeight: number,
  ratioWidth: number,
  ratioHeight: number,
): FrameSize {
  const safeWidth = Math.max(0, parentWidth);
  const safeHeight = Math.max(0, parentHeight);
  if (safeWidth <= 0 || safeHeight <= 0) return { width: 0, height: 0 };

  // 预览必须完整收进父容器：先按宽度推导高度，超高时再以高度反算宽度。
  const ratio = normalizeRatioNumber(ratioWidth, 21) / normalizeRatioNumber(ratioHeight, 9);
  let width = safeWidth;
  let height = width / ratio;
  if (height > safeHeight) {
    height = safeHeight;
    width = height * ratio;
  }
  return { width: Math.floor(width), height: Math.floor(height) };
}
