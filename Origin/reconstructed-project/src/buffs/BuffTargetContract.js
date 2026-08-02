'use strict';
function assertBuffTarget(target, type = 'unknown') {
  const required = ['am', 'jw', 'zw', 'setState'];
  for (const method of required) {
    if (!target || typeof target[method] !== 'function') {
      throw new TypeError(`Buff target ${target && target.id} for type ${type} requires ${method}()`);
    }
  }
  return target;
}
module.exports = { assertBuffTarget };
