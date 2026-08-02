'use strict';
function getLaya() {
  if (!globalThis.Laya) throw new Error('Laya runtime is not available');
  return globalThis.Laya;
}
module.exports = { getLaya };
