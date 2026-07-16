/**
 * Ekran koordinatını React Flow'un canvas (flow) koordinatına çevirir.
 *
 * Normalde `useReactFlow().screenToFlowPosition()` kullanılır; ancak bu dönüşüme
 * ihtiyaç duyan kod (useMultiplayer) ReactFlowProvider'ın DIŞINDA çalıştığı için
 * hook'a erişemez. Bu yüzden viewport'un uygulanmış CSS transform'u okunur —
 * React Flow'un kendi dönüşümüyle aynı matematik.
 *
 * Canvas mount değilse null döner.
 */
export function screenToFlowPosition(clientX: number, clientY: number): { x: number; y: number } | null {
  if (typeof document === 'undefined') return null;

  const container = document.querySelector('.react-flow');
  const viewport = document.querySelector('.react-flow__viewport');
  if (!container || !viewport) return null;

  const rect = container.getBoundingClientRect();

  // matrix(scaleX, skewY, skewX, scaleY, translateX, translateY)
  const transform = window.getComputedStyle(viewport).transform;
  if (!transform || transform === 'none') {
    return { x: clientX - rect.left, y: clientY - rect.top };
  }

  const match = transform.match(/matrix\(([^)]+)\)/);
  if (!match) return { x: clientX - rect.left, y: clientY - rect.top };

  const parts = match[1].split(',').map(v => parseFloat(v.trim()));
  if (parts.length < 6) return null;

  const [scale, , , , translateX, translateY] = parts;
  if (!scale) return null;

  return {
    x: (clientX - rect.left - translateX) / scale,
    y: (clientY - rect.top - translateY) / scale,
  };
}
