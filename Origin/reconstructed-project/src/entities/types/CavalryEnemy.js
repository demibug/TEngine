'use strict';
const { ConfiguredEnemy } = require('./ConfiguredEnemy');

/**
 * 重建模块：ENEMY-RUNTIME-01 / Cavalry
 * 原始范围：bundle.strings-decoded.js:32390-32467
 * 原始主要符号：qT
 * 重建状态：黄圈光环创建 + 骑兵呼吸恢复（init/gameOver/fw/mw/tB）
 *
 * bundle 字段映射：
 *   tw=animation 节点(src this.animation)  enemy=visual 节点(src this.visual)
 *   JE=resourcePath  Sm=baseMoveSpeed(hu[65]=80)  iB=黄圈光环节点(yellowCircle.png)
 *
 * 取证澄清（任务组1校验 PASS）：bundle 中 Cavalry 的"光环"（yellowCircle.png 80×30 脚下贴图）
 * 纯视觉装饰，无攻速/伤害加成逻辑——不查周围单位、不施加 buff。Cavalry 与 Mob0 的唯一逻辑差异
 * 是移动速度 80（hu[65]，构造 baseSpeedOverride:80 已正确）。本类恢复：
 *   - init 创建黄圈光环并 addChild 到脚下（zIndex=-1 地面层），gameOver 移除但不销毁（复用）。
 *   - 骑兵呼吸 tB（bundle:32447-32461）：纵向颠簸 0.78→0.82→0.8，130ms/段自循环。
 *   - 表现层经 presentation port 承载（createCavalryAura/removeCavalryAura/
 *     startCavalryBreathing/stopCavalryBreathing），纯逻辑层只持 auraResource 字段与启停调用。
 *
 * hook 映射（bundle→src）：
 *   fw()→startMovingAnimation()  mw()→stopMovingAnimation()  tB()→呼吸经 port
 * Cavalry 不跳过 NormalEnemyBase.init（不像 Zombie 有专属出生动画），仍走 pe.init 的 playSpawn 出生，
 * 仅在 init 后追加光环创建。
 */
class CavalryEnemy extends ConfiguredEnemy {
  constructor() {
    super({ typeKey: 'Cavalry', typeIndex: 5, resourcePath: 'resources/img/gameObject/soldier/soldier_3.png', baseSpeedOverride: 80 });
    // iB：黄圈光环贴图资源路径（bundle:32402 yellowCircle.png）。逻辑层持 auraResource 供 port 创建。
    this.auraResource = 'resources/img/gameObject/enemy/yellowCircle.png';
  }

  /**
   * 覆写 init（bundle:32400-32412）：super.init（ConfiguredEnemy.init：取池+NormalEnemyBase.init
   * 含 playSpawn 出生+baseSpeedOverride:80+animation.pos）后，经 port createCavalryAura 创建黄圈光环。
   * 光环：yellowCircle.png 80×30（hu[65]/hu[22]），pos(0,40)（hu[43]），zIndex=-1 地面层，addChild 到 visual。
   * bundle 原用 this.iB||(this.iB=new Laya.Image(...)) 复用模式；port 内按名 'cavalryAura' 复用节点。
   * Cavalry 不跳过 NormalEnemyBase.init（仍走 playSpawn 出生），仅在 init 后加光环。
   */
  init(playerLane) {
    super.init(playerLane);
    // 经 presentation port 创建黄圈光环（bundle:32403-32412 iB 创建+addChild）。
    this.presentation.createCavalryAura(this, this.auraResource);
    return this;
  }

  /**
   * 覆写 gameOver（bundle:32413-32417）：super.gameOver（ConfiguredEnemy.gameOver：
   * NormalEnemyBase.gameOver 吹飞/Tween 清理 + visual 回收）后，经 port removeCavalryAura 移除光环。
   * 光环 removeSelf 不销毁（bundle:32417 iB.removeSelf），下次 init 经 port 按名复用同一节点。
   */
  gameOver() {
    if (this.inPool || this.__InPool) return false;
    const result = super.gameOver();
    // 经 presentation port 移除光环（不销毁，复用）。
    this.presentation.removeCavalryAura(this);
    return result;
  }

  /**
   * 移动动画启动 hook（bundle:32428-32434，符号 fw）：启动 tB 骑兵呼吸。
   * src 中 fw 对应 startMovingAnimation（由 _enterState(MOVING) 调用）。
   * 先调 super.startMovingAnimation（presentation.startMoving 播放移动动画），再启动骑兵呼吸。
   */
  startMovingAnimation() {
    super.startMovingAnimation();
    this.tB();
  }

  /**
   * 移动动画停止 hook（bundle:32436-32445，符号 mw）：killAll Tween + scale(1,1) 复位。
   * src 中 mw 对应 stopMovingAnimation（由 _exitState(MOVING) 调用）。
   * 经 presentation port stopCavalryBreathing 承载 Tween.killAll + scale 复位。
   */
  stopMovingAnimation() {
    this.presentation.stopCavalryBreathing(this);
    super.stopMovingAnimation();
  }

  /**
   * 骑兵呼吸启动（bundle:32447-32461，符号 tB）：纵向颠簸三段链式 Tween 自循环。
   * 段1: scaleY 0.78  duration=hu[171]=130 ms
   * 段2: scaleY 0.82  duration=130 ms
   * 段3: scaleY 0.8   duration=130 ms
   * 完成后 then(this.tB,this) 自循环。仅改 scaleY（纵向颠簸），幅度大于 Zombie/Puppet。
   * 经 presentation port startCavalryBreathing 承载 Tween（生产层用 Laya.Tween.create chain/then；
   * 逻辑层只调度，不直接操作 Laya.Tween）。
   */
  tB() {
    this.presentation.startCavalryBreathing(this);
  }
}
CavalryEnemy.originalSymbol = 'qT';
CavalryEnemy.sourceRange = '32390-32467';
module.exports = { CavalryEnemy };
