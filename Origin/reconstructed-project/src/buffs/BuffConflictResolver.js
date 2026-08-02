'use strict';
const { BuffType } = require('./BuffTypes');

class BuffConflictResolver {
  constructor() {
    // 原 nB.data.sh / data.ih：击退与 limit 为互斥/替换关系。
    this.conflicts = new Map([[BuffType.KNOCKBACK, [BuffType.LIMIT]]]);
    this.replacements = new Map([[BuffType.LIMIT, [BuffType.KNOCKBACK]]]);
  }
  hasConflict(activeMap, type) {
    const list = this.conflicts.get(type) || [];
    return list.some(item => activeMap.has(item));
  }
  typesToRemove(type) { return (this.replacements.get(type) || []).slice(); }
  removeReplaced(activeMap, type, remove) {
    for (const oldType of this.typesToRemove(type)) if (activeMap.has(oldType)) remove(oldType);
  }
}
module.exports = { BuffConflictResolver };
