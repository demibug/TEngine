'use strict';
const { StateBuffHandler } = require('../StateBuffHandler');

/** 原 uQ：首次应用和后续合并均再次施加传入向量。 */
class KnockbackBuffHandler extends StateBuffHandler {
  onMergedLayer(_layer, data) { this.target.setState(5, true, data.qw); }
  label() { return '击退'; }
}
module.exports = { KnockbackBuffHandler };
