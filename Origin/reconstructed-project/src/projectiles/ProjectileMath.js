'use strict';

/**
 * 重建模块：BOW-PROJECTILE-COMBAT-01 / projectile math
 * 原始范围：bundle.strings-decoded.js:2698-2798
 * 原始符号：np.bs / np.Ms / np.angle / np.Rs / np.Us
 * 重建状态：COMPLETE_FOR_SIMPLE_DYNAMIC_ARROW
 */
function distance(a, b) {
  const dx = a.x - b.x;
  const dy = a.y - b.y;
  return Math.sqrt(dx * dx + dy * dy);
}

function distanceSquared(a, b) {
  const dx = a.x - b.x;
  const dy = a.y - b.y;
  return dx * dx + dy * dy;
}

/** np.angle：0° 朝上，90° 朝右。 */
function displayAngle(from, to) {
  const dx = to.x - from.x;
  const invertedDy = from.y - to.y;
  if (dx === 0) return invertedDy >= 0 ? 0 : 180;
  if (invertedDy === 0) return dx > 0 ? 90 : 270;
  return Math.atan2(dx, invertedDy) * 180 / Math.PI;
}

/** np.Rs：二次贝塞尔在 t 处切线的标准角度。 */
function quadraticTangentDegrees(start, control, end, progress) {
  const dx = 2 * (1 - progress) * (control.x - start.x) + 2 * progress * (end.x - control.x);
  const dy = 2 * (1 - progress) * (control.y - start.y) + 2 * progress * (end.y - control.y);
  return Math.atan2(dy, dx) * 180 / Math.PI;
}

/**
 * np.Us：de Casteljau 二次贝塞尔插值。
 * 返回 progress >= 1，与原函数布尔返回值一致。
 */
function quadraticBezier(start, control, end, output, progress) {
  const firstX = start.x + (control.x - start.x) * progress;
  const firstY = start.y + (control.y - start.y) * progress;
  const secondX = control.x + (end.x - control.x) * progress;
  const secondY = control.y + (end.y - control.y) * progress;
  output.x = firstX + (secondX - firstX) * progress;
  output.y = firstY + (secondY - firstY) * progress;
  return !(progress < 1);
}

module.exports = {
  distance,
  distanceSquared,
  displayAngle,
  quadraticTangentDegrees,
  quadraticBezier,
};
