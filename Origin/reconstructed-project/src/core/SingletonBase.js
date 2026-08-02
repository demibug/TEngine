'use strict';

/** 原始 qU：延迟单例基类。 */
class SingletonBase {
  static instance() {
    return this.Instance || (this.Instance = new this());
  }

  /** TEST_ONLY：不参与生产初始化。 */
  static resetInstanceForTests() {
    if (Object.prototype.hasOwnProperty.call(this, 'Instance')) delete this.Instance;
  }
}

module.exports = { SingletonBase };
