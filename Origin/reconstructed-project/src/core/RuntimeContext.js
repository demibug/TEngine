'use strict';
class RuntimeContext {
  constructor(services = {}) { Object.assign(this, services); }
  require(name) {
    const value = this[name];
    if (value === undefined || value === null) throw new Error(`RuntimeContext service is missing: ${name}`);
    return value;
  }
}
module.exports = { RuntimeContext };
