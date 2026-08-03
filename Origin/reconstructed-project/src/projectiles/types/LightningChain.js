'use strict';
const { ProjectileBase } = require('../ProjectileBase');

/**
 * 重建模块：特殊投射物 / LightningChain（闪电链）
 * 原始注册：bundle.strings-decoded.js:35487
 * 重建状态：PARTIAL（帧序列契约登记 + 链式命中钩子，专属渲染 DEFERRED 至 P2）
 *
 * 取证（bundle:618-623）：闪电链含多帧资源 lightningChainStart_01..03 / lightningChainEnd_01..03，
 * 命中时连接源敌人与目标敌人。纯逻辑层登记帧序列契约与链式命中计数，
 * 实际闪电链渲染/连接线表现为 P2 非目标。
 *
 * 帧动画契约登记：
 * - 启动帧序列：lightningChainStart_01, _02, _03
 * - 结束帧序列：lightningChainEnd_01, _02, _03
 * - 命中行为：链式跳跃（LightningArrow 命中后 50% 概率生成本弹种连跳下一目标）
 */
class LightningChain extends ProjectileBase {
  initializeAppearance(appearance = {}) {
    if (this.imageNode) return;
    const img = new this.laya.Image(appearance.resourcePath || LightningChain.DEFAULT_APPEARANCE.resourcePath);
    this.renderNode.addChild(img);
    this.imageNode = img;
  }

  /**
   * 帧序列契约：登记启动/结束帧名序列，供表现层（P2）驱动帧动画。
   * 纯逻辑层仅登记，不实现渲染。
   */
  static FRAME_SEQUENCE = Object.freeze({
    start: ['lightningChainStart_01', 'lightningChainStart_02', 'lightningChainStart_03'],
    end: ['lightningChainEnd_01', 'lightningChainEnd_02', 'lightningChainEnd_03'],
  });

  onReset(config) {
    // 链式源/目标登记（供链式命中计数，DEFERRED: 链式跳跃逻辑由 LightningArrow 触发）
    this.chainSource = config.chainSource || null;
    this.chainTarget = config.chainTarget || null;
  }

  applyHit(enemy) {
    const result = enemy.hit(this.damage, this.attacker);
    this.applyImpactEffects(enemy);
    return result;
  }
}

LightningChain.projectileTypeKey = 'LightningChain';
LightningChain.DEFAULT_APPEARANCE = Object.freeze({ resourcePath: 'resources/img/weapon/bullet/lightningChain_01.png' });
module.exports = { LightningChain };
