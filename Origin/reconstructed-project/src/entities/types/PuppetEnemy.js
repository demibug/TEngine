'use strict';
const { ConfiguredEnemy } = require('./ConfiguredEnemy');
const { EnemyRuntimeState } = require('../EnemyBase');
const { GameEvents } = require('../../core/EventBus');
const PUPPET_HEALTH_MULTIPLIERS = Object.freeze([1,1.2,1.4,1.6,1.8]);

/**
 * 重建模块：ENEMY-RUNTIME-01 / Puppet
 * 原始范围：bundle.strings-decoded.js:31784-31923
 * 原始主要符号：oo
 * 重建状态：爱心粒子系统 + 路径事件订阅 + 速度10 恢复（rB/nB/update/mw/gameOver）
 *
 * bundle 字段映射：
 *   hB=puppetLevel(傀儡等级1..5)  Sm=baseMoveSpeed(10)  eB=爱心粒子状态数组{img,scale}
 *   aB=爱心生成累计时间  Lp=soldierSkinIndex  JE=resourcePath  Lm=currentPathIndex
 *   tw=animation 节点(src this.animation)  enemy=visual 节点(src this.visual)
 *
 * 取证澄清（任务组1校验 PASS）：爱心粒子（loveHeart 池）纯表现，无治疗/增益逻辑——
 * Puppet 不覆写 hit/不调 rewardService。爱心是"被操控傀儡"的视觉标识。
 * 本类恢复：
 *   - 速度 10（bundle:31793 字面量 Sm=10，傀儡几乎不动）。
 *   - 爱心粒子 rB（bundle:31890-31919）：每 300ms 生成一个，随机目标缩放 0.1~0.5，
 *     缓慢放大（增速 1/3000）达目标后淡出（alpha 减速 1/1000），总寿命约 2.5s，最多并存约 8 个。
 *     在 update 中 super.update 后调 rB。
 *   - yt 路径事件订阅 nB（bundle:31882-31888）：傀儡的 currentPathIndex 由全局 yt 事件驱动
 *     （同步到被操控士兵的真实路径）。
 *   - 待机缩放 0.9（mw，bundle:31871-31880）：停止移动时缩到 0.9（非 1）。
 *   - gameOver（bundle:31811-31822）：取消 puppetSkip 定时器 + 回收全部爱心粒子 + super.gameOver。
 *
 * hook 映射（bundle→src）：
 *   update()→update()  mw()→stopMovingAnimation()  nB()→yt 回调  rB()→爱心粒子
 * Puppet 不跳过 NormalEnemyBase.init（仍走 pe.init 的 playSpawn 出生），仅覆写 update/mw/gameOver + 增 rB/nB。
 */
class PuppetEnemy extends ConfiguredEnemy {
  constructor() {
    // Sm=10（bundle:31793 字面量，傀儡几乎不动）。ConfiguredEnemy.init 会用 baseSpeedOverride 覆盖 baseMoveSpeed。
    super({ typeKey: 'Puppet', typeIndex: 6, resourcePath: 'resources/img/gameObject/soldier/soldier_0.png', baseSpeedOverride: 10 });
    // hB：傀儡等级（1..5）。bundle:31788 hB=1。
    this.puppetLevel = 1;
    // Lp：士兵贴图索引（决定 soldier_N.png 贴图）。bundle:31805 JE="soldier_"+Lp+".png"。
    this.soldierSkinIndex = 0;
    // eB：爱心粒子状态数组。每项 {img, scale}：img=爱心节点（经 port 创建/回收），scale=目标缩放(0.1~0.5)。
    // bundle:31790 eB=[]；31905 eB.push({img:oB, scale: np.range(.1,.5)})。
    this.eB = [];
    // aB：爱心生成累计时间(ms)。每帧 aB+=deltaMs；>=300(hu[167]) 时归零并生成一个新爱心。bundle:31790 aB=0。
    this.aB = 0;
  }

  /**
   * 配置傀儡等级与士兵贴图（保留既有 configurePuppet 接口）。
   * 血量倍率 Sh[level-1]（bundle:31848/12149）在 init 中施加（见 init）。
   */
  configurePuppet({ level=1, soldierSkinIndex=0, startPosition=null, firstPathCenter=null, pathIndex=0 }={}) {
    this.puppetLevel=Math.max(1,Math.min(5,Number(level)||1)); this.soldierSkinIndex=Number(soldierSkinIndex)||0;
    this.resourcePath=`resources/img/gameObject/soldier/soldier_${this.soldierSkinIndex}.png`;
    if(startPosition) Object.assign(this.startPosition,startPosition); if(firstPathCenter) Object.assign(this.firstPathCenter,firstPathCenter); this.currentPathIndex=Number(pathIndex)||0; return this;
  }

  /**
   * 覆写 init（bundle:31803-31805）：super.init（ConfiguredEnemy.init：取池+NormalEnemyBase.init
   * 含 playSpawn 出生+baseSpeedOverride:10+animation.pos）后，注册 yt 路径事件订阅。
   * bundle 原版在 init 末尾 oc.instance.on(sS["yt"], this, this["nB"])——此处经 eventBus.on
   * （sS["yt"]==字符串"yt"，src 映射为 GameEvents.PUPPET_PATH_SYNC）。
   * 血量倍率：Puppet 不跳过 NormalEnemyBase.init，但需在 stats 解析后施加 Sh[level-1] 倍率。
   * ConfiguredEnemy.init→super.init(NormalEnemyBase.init) 会调 _initializeStatsAndAnimation 解析血量，
   * 故在 super.init 返回后施加倍率（等价 bundle:31848 Zi=l.ph*Sh[hB-1]）。
   */
  init(playerLane) {
    super.init(playerLane);
    // 血量倍率 Sh[hB-1]（bundle:31848/31850 Zi=l.ph*Sh[hB-1], Km=l.ph*Sh[hB-1]）。
    // NormalEnemyBase._initializeStatsAndAnimation 已设 health/maxHealthBase=stats.ph（无 Zombie ÷2，typeIndex=6）。
    const m = PUPPET_HEALTH_MULTIPLIERS[this.puppetLevel - 1];
    this.maxHealthBase *= m;
    this.health = this.maxHealthBase;
    // 注册 yt 路径事件订阅（bundle:31805 oc.instance.on(sS["yt"], this, this["nB"])）。
    // nB 回调更新 currentPathIndex（同步到被操控士兵真实路径）。
    this.eventBus.on(GameEvents.PUPPET_PATH_SYNC, this, this.nB);
    return this;
  }

  /**
   * 覆写 update（bundle:31807-31809）：super.update(a) 后调 this.rB(a)。
   * EnemyBase.update 在 MOVING 态调 move，Puppet 速度10几乎不动但仍走路径。
   * rB 持续生成/更新/回收爱心粒子（只要 Puppet 存活）。
   */
  update(deltaMs) {
    super.update(deltaMs);
    this.rB(deltaMs);
  }

  /**
   * 爱心粒子系统（bundle:31890-31919，符号 rB）。
   * hu 常量：hu[167]=300(生成周期ms) hu[1]=40(头顶 x 下限/上限偏移) hu[135]=3000(放大增速倒数ms)
   *          hu[123]=1000(淡出 alpha 减速倒数ms)。
   *   生成：aB+=deltaMs；aB>=300 时 aB=0，经 port createPuppetHeart 取池对象 oB（loveHeart 池），
   *         push {img:oB, scale: np.range(.1,.5)}（随机目标缩放 0.1~0.5）；
   *         oB.scale(0,0)；oB.pos(np.range(40, width-40), np.range(0, height/2))（头顶随机位置）；addChild。
   *   每帧更新所有粒子：oB.scaleX += deltaMs/3000；oB.scaleY += deltaMs/3000（缓慢放大）；
   *         当 oB.scaleX >= 目标scale → 开始淡出 oB.alpha -= deltaMs/1000；
   *         当 oB.alpha <= 0 → 经 port recoverPuppetHeart 回收（removeSelf/alpha=1/recover）+ splice。
   * 生命周期：放大阶段（约1.5s，scale增速1/3000，目标0.1~0.5）→淡出阶段（约1s，alpha减速1/1000）。
   * 总寿命约2.5s，每300ms生成一个，最多并存约8个。
   * 爱心粒子纯表现，无治疗/增益逻辑（Puppet 不覆写 hit/不调 rewardService）。
   */
  rB(deltaMs) {
    const SPAWN_INTERVAL_MS = 300; // hu[167]
    const HEAD_X_MARGIN = 40;     // hu[1] 头顶 x 随机下限/偏移
    const SCALE_GROW_DIVISOR = 3000; // hu[135] 放大增速倒数（scaleX += deltaMs/3000）
    const ALPHA_FADE_DIVISOR = 1000; // hu[123] 淡出减速倒数（alpha -= deltaMs/1000）
    // np.range(min,max) 等价：Math.random()*(max-min)+min。
    const range = (min, max) => min + Math.random() * (max - min);
    // 生成判定（bundle:31900-31909）。
    this.aB += deltaMs;
    if (this.aB >= SPAWN_INTERVAL_MS) {
      this.aB = 0;
      // 经 port 取池对象 oB（bundle:31902 rw.instance().getItem("loveHeart", this)）。
      const oB = this.presentation.createPuppetHeart(this);
      if (oB) {
        // 随机目标缩放 0.1~0.5（bundle:31905 np.range(.1,.5)）。
        const targetScale = range(0.1, 0.5);
        this.eB.push({ img: oB, targetScale });
        // 初始 scale(0,0)（bundle:31907 oB.scale(0,0)）。
        if (typeof oB.scale === 'function') oB.scale(0, 0);
        else { oB.scaleX = 0; oB.scaleY = 0; }
        // 头顶随机位置（bundle:31908 oB.pos(np.range(40, width-40), np.range(0, height/2))）。
        const x = range(HEAD_X_MARGIN, this.visual.width - HEAD_X_MARGIN);
        const y = range(0, this.visual.height / 2);
        if (typeof oB.pos === 'function') oB.pos(x, y);
        else { oB.x = x; oB.y = y; }
        // addChild 到 visual（bundle:31909 enemy.addChild(oB)）。
        if (typeof this.visual.addChild === 'function') this.visual.addChild(oB);
      }
    }
    // 每帧更新所有粒子：放大 → 达目标后淡出 → alpha<=0 回收（bundle:31911-31919）。
    for (let i = this.eB.length - 1; i >= 0; i--) {
      const particle = this.eB[i];
      const oB = particle.img;
      // 缓慢放大（bundle:31912-31913 scaleX/scaleY += deltaMs/3000）。
      oB.scaleX += deltaMs / SCALE_GROW_DIVISOR;
      oB.scaleY += deltaMs / SCALE_GROW_DIVISOR;
      // 达目标缩放后开始淡出（bundle:31914 scaleX>=scale → alpha -= deltaMs/1000）。
      if (oB.scaleX >= particle.targetScale) {
        oB.alpha -= deltaMs / ALPHA_FADE_DIVISOR;
        // alpha<=0 回收（bundle:31915-31919 removeSelf/alpha=1/recover/splice）。
        if (oB.alpha <= 0) {
          this.presentation.recoverPuppetHeart(oB);
          this.eB.splice(i, 1);
        }
      }
    }
  }

  /**
   * yt 路径事件回调（bundle:31882-31888，符号 nB）。
   * bundle: nB(a){ this["Lm"]=a } —— 更新 currentPathIndex 为事件携带的 pathIndex。
   * 傀儡的 currentPathIndex 由全局 yt 事件驱动（同步到被操控士兵的真实路径），
   * 而非自身移动推进（Puppet 速度10几乎不动）。
   * 在 init 注册订阅（eventBus.on），gameOver 取消订阅（eventBus.off）。
   */
  nB(pathIndex) {
    this.currentPathIndex = pathIndex;
  }

  /**
   * 移动动画停止 hook（bundle:31871-31880，符号 mw）：killAll Tween + scale(0.9,0.9) 待机缩放。
   * src 中 mw 对应 stopMovingAnimation（由 _exitState(MOVING) 调用）。
   * bundle:31875-31878 Laya.Tween.killAll(this.tw); this.tw.scale(.9,.9)——停止移动时缩到0.9（非1）。
   * "0.9 待机缩放"是 Puppet 的视觉标识（傀儡静止时略小）。经 presentation port 无专属方法
   * （mw 仅操作 animation transform，与 Cavalry stopCavalryBreathing 的 scale(1,1) 不同），
   * 故直接在逻辑层 killAll Tween + scale(0.9,0.9)。
   */
  stopMovingAnimation() {
    const laya = this.laya;
    if (laya && laya.Tween && typeof laya.Tween.killAll === 'function' && this.animation) {
      laya.Tween.killAll(this.animation);
    }
    if (this.animation && typeof this.animation.scale === 'function') {
      this.animation.scale(0.9, 0.9);
    }
    // 不调 super.stopMovingAnimation：其调 presentation.stopMoving(enemy.animation.stop)，
    // bundle mw 不停 animation 播放只 killAll Tween + scale。Puppet 待机仍播放基础动画。
    // 注：若需停止移动动画播放，super.stopMovingAnimation 会在 _exitState(MOVING) 已由
    // NormalEnemyBase 未覆写路径调用——但 bundle mw 明确不调 stopMoving，只 killAll+scale。
  }

  /**
   * 覆写 gameOver（bundle:31811-31822）：取消 yt 订阅 + 回收全部爱心粒子 + 清空数组 + super.gameOver。
   * bundle:31813 nx.instance().wa("puppetSkip"+id) 取消 puppetSkip 定时器（外部召唤方注册的定时器，
   *   src 无此定时器键——Puppet 自身不注册 puppetSkip，由外部召唤方经 gameLoop 管理，DEFERRED）；
   *   super.gameOver()；遍历 eB 全部 removeSelf/alpha=1/recover("loveHeart")；eB.length=0。
   * src 映射：gameLoop.unregister("puppetSkip"+id) 等价 nx.wa（若外部注册了该键则注销；未注册则 no-op 安全）；
   *   遍历 eB 经 port recoverPuppetHeart 回收；eventBus.off 取消 yt 订阅；super.gameOver。
   */
  gameOver() {
    if (this.inPool || this.__InPool) return false;
    // 取消 yt 路径事件订阅（bundle 无显式 off，但 src 须在 gameOver 取消订阅避免池复用后回调泄漏）。
    this.eventBus.off(GameEvents.PUPPET_PATH_SYNC, this, this.nB);
    // 取消 puppetSkip 定时器（bundle:31813 nx.wa("puppetSkip"+id)）。外部召唤方注册的定时器，
    // src 经 gameLoop.unregister 注销（未注册则 no-op 安全）。DEFERRED: 调用方属提案 ②③。
    this.gameLoop.unregister(`puppetSkip${this.id}`);
    // 回收全部存活爱心粒子（bundle:31817-31820 遍历 eB removeSelf/alpha=1/recover）。
    for (let i = this.eB.length - 1; i >= 0; i--) {
      const oB = this.eB[i].img;
      this.presentation.recoverPuppetHeart(oB);
    }
    this.eB.length = 0;
    this.aB = 0;
    return super.gameOver();
  }
}
PuppetEnemy.originalSymbol='oo'; PuppetEnemy.sourceRange='31784-31923';
module.exports={ PuppetEnemy, PUPPET_HEALTH_MULTIPLIERS };
