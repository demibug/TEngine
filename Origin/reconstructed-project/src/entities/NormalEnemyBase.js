'use strict';

const { EnemyBase, EnemyRuntimeState } = require('./EnemyBase');
const { GameEvents } = require('../core/EventBus');
// np：贝塞尔/距离/角度数学（bundle:31455 np.angle、31474 np.Us、31329 np.bs）。
const ProjectileMath = require('../projectiles/ProjectileMath');

/**
 * 重建模块：ENEMY-RUNTIME-01 普通敌人表现/死亡层
 * 原始范围：bundle.strings-decoded.js:31262-31482
 * 原始符号：pe
 * 重建状态：COMPLETE_FOR_MOB0_LIFECYCLE + 灵魂投射 sB / 吹飞 Xw/Gw 通用能力恢复
 *
 * 重要结论：pe 继承 ro；它不是 MovementController。路径移动仍由 ro/EnemyBase 实现。
 * 灵魂投射 sB（bundle:31408-31432）与吹飞 Xw/Gw（bundle:31434-31478）为 pe 通用能力：
 *   - sB 在死亡表现完成后按条件触发（typeIndex!=1 + 对方灵魂塔 Ci+num<3+距离<range）。
 *   - Xw 为外部触发 API（攻击/技能系统调用），设置贝塞尔曲线+濒死+经 gameLoop.register 注册 Gw 每帧推进。
 *   - Gw 由 GameLoop.update 以 80ms 子步长调用，推进贝塞尔曲线，time>=1 时 hit(1) 致死。
 * 塔状态/飞行管理器/ut 事件消费者以可注入接口承载（DEFERRED，详见 EnemyBase.configure）。
 * gameLoop 默认委托 GameLoop.instance() 单例（对齐 bundle nx.instance()），亦可经 configure 注入。
 */
class NormalEnemyBase extends EnemyBase {
  constructor() {
    super();
    // QE：吹飞二次贝塞尔曲线。键名对齐 bundle（bundle:31270-31275 为 {ug,p1,p2,time}）。
    // ug=起点(敌人当前位置)，p1=控制点(中点x、y抬高 3*(60-heightArg))，p2=终点(朝击中方向反向偏移一半)，time=归一化进度。
    this.blowUpCurve = { ug: null, p1: null, p2: null, time: 0 }; // QE
    this.blowUpState = 0; // ZE：吹飞状态(0=未激活，1=激活)
    this.resourcePath = null; // JE
  }

  init(playerLane) {
    super.init(playerLane);
    this._initializeStatsAndAnimation();
    this.healthText.text = this.health.toFixed(0);
    this.healthText.visible = false;
    this.healthBarBackground.visible = false;
    this.getPath();
    this.visual.visible = false;
    const generation = this._lifecycleGeneration;
    this.presentation.playSpawn(this, () => {
      if (generation !== this._lifecycleGeneration || this.inPool) return;
      this.changeState(EnemyRuntimeState.MOVING);
      this.healthBarImmediate.width = 0;
      this.healthBarBackground.visible = true;
      this.healthBarImmediate.width = this.healthBarWidth;
    });
    return this;
  }

  _initializeStatsAndAnimation() {
    const stats = this.gameData.resolveEnemyStats(this.typeIndex, this.isPlayerLane);
    this.health = this.typeIndex === 4 ? stats.ph / 2 : stats.ph;
    this.maxHealthBase = stats.ph;
    this.baseMoveSpeed = stats.speed;
    this.healthBarImmediate.skin = this.typeIndex === 4
      ? 'resources/img/gameObject/enemy/hp3.png'
      : 'resources/img/gameObject/enemy/hp2.png';
    this.animation = this.visual.getChildByName('sp');
    if (!this.animation) {
      this.animation = this.presentation.createAnimation(this, this.resourcePath, this.fastAnimation);
      if (!this.animation || typeof this.visual.addChild !== 'function') throw new Error('Enemy presentation failed to create animation child');
      this.animation.name = 'sp';
      this.visual.addChild(this.animation);
    }
    if (typeof this.animation.play !== 'function') throw new Error('Enemy animation must implement play()');
    this.animation.play('animation', true);
  }

  get moveSpeed() {
    const speed = this.baseMoveSpeed + this.moveSpeedModifier;
    this.playbackRate = speed / this.baseMoveSpeed;
    this.presentation.setMovePlaybackRate(this, this.playbackRate);
    return speed;
  }

  hit(damage, attacker = null) {
    const applied = super.hit(damage, attacker);
    return applied;
  }

  beginDeath() {
    if (this.deathStarted) return;
    super.beginDeath();
    this._deathScheduled = true;
    const generation = this._lifecycleGeneration;
    this.presentation.playDeath(this, this.typeIndex === 4 ? '#c1f6cb' : '#000000', () => {
      if (generation !== this._lifecycleGeneration || this.inPool) return;
      this._deathScheduled = false;
      this.visual.alpha = 1;
      this.visual.visible = false;
      // 灵魂投射分支（bundle:31326-31333 Lw 的 onComplete）：死亡表现完成后，
      // 仅 typeIndex!=1 的敌人在对方灵魂塔条件满足时触发 sB()。typeIndex=1 不投射。
      if (this.typeIndex !== 1) this._tryDeliverSoul();
      this.gameOver();
    });
  }

  /**
   * 灵魂投射条件判定与触发（bundle:31326-31333 / 31408-31432）。
   * 条件：typeIndex!=1 && tower.Ci && tower.num<3 && distance(enemy中心, tower.pos) < tower.range。
   * 满足后调用 sB()。塔状态经 soulTowerResolver(isPlayerLane) 取（DEFERRED 接口，默认桩 Ci:false 不触发）。
   */
  _tryDeliverSoul() {
    const tower = this.soulTowerResolver(this.isPlayerLane);
    if (!tower || !tower.Ci) return;
    if (!(tower.num < 3)) return;
    const enemyCenter = { x: this.visual.x + this.visual.width / 2, y: this.visual.y + this.visual.height / 2 };
    const towerPos = tower.pos || { x: 0, y: 0 };
    if (ProjectileMath.distance(enemyCenter, towerPos) >= tower.range) return;
    this.sB();
  }

  /**
   * 灵魂投射（bundle:31408-31432，符号 sB）。
   * 经 soulFlightManager 创建 soulHead.png 绿色（#05fe77）灵魂头投射物，飞行 300ms（hu[167]），
   * 到达后发 ENEMY_SOUL_DELIVERED 事件（携带 isPlayerLane/敌人坐标/路径索引）。
   * bundle 原将塔 pos 与敌人中心经 localToGlobal 转全局坐标后传飞行管理器；此处经 DEFERRED
   * 飞行管理器接口传递坐标（真实实现由 ②③ 注入，坐标系由其决定），并捕获死亡时敌人局部坐标用于事件。
   */
  sB() {
    const tower = this.soulTowerResolver(this.isPlayerLane);
    const towerPos = (tower && tower.pos) || { x: 0, y: 0 };
    // 捕获死亡时敌人局部坐标与路径索引，用于飞行到达事件（bundle:31425 o=enemy.x, p=enemy.y, n=Lm）。
    const pathIndex = this.currentPathIndex;
    const enemyX = this.visual.x;
    const enemyY = this.visual.y;
    // 敌人中心（bundle:31424 Qy={width/2, height}，enemy.localToGlobal）。逻辑层传敌人中心局部坐标。
    const enemyCenterX = this.visual.x + this.visual.width / 2;
    const enemyCenterY = this.visual.y + this.visual.height / 2;
    this.soulFlightManager.fly(
      towerPos.x, towerPos.y,
      enemyCenterX, enemyCenterY,
      300, // hu[167]：灵魂飞行时长（ms）
      '#05fe77',
      'resources/img/gameObject/enemy/soulHead.png',
      () => {
        // bundle:31427 oc.event(sS["ut"], nm, o, p, n) → ENEMY_SOUL_DELIVERED(isPlayerLane, x, y, pathIndex)
        this.eventBus.event(GameEvents.ENEMY_SOUL_DELIVERED, this.isPlayerLane, enemyX, enemyY, pathIndex);
      },
    );
  }

  /**
   * 吹飞外部触发 API（bundle:31434-31465，符号 Xw）。
   * 由攻击/技能系统调用（DEFERRED：调用方与 heightArg 来源属提案 ②③，本提案只恢复被触发能力）。
   * 设置二次贝塞尔曲线 QE：ug=敌人当前位置，p2=朝击中方向反向偏移一半，p1=中点x、y抬高 3*(60-heightArg)；
   * 置吹飞状态 ZE=1，立刻 hit(当前血量-0.1) 致濒死（不致死，由 Gw 落地 hit(1) 致死）；
   * 旋转动画 tw 朝向击退方向（np.angle{hitX,hitY}→{中心}）；经 gameLoop.register("blownUp"+id) 注册 Gw 每帧推进。
   */
  Xw(heightArg, hitX, hitY) {
    const enemy = this.visual;
    // m=敌人中心x，l=敌人中心y（bundle:31445 m=enemy.x+width/2, l=enemy.y+height/2）。
    const m = enemy.x + enemy.width / 2;
    const l = enemy.y + enemy.height / 2;
    // QE.ug=起点=敌人左上角当前位置（bundle:31446-31448）。
    this.blowUpCurve.ug = { x: enemy.x, y: enemy.y };
    // QE.p2=终点=朝击中方向反向偏移一半（bundle:31449-31451：enemy + (中心-击中)/2）。
    this.blowUpCurve.p2 = { x: enemy.x + (m - hitX) / 2, y: enemy.y + (l - hitY) / 2 };
    // QE.p1=控制点=ug→p2 中点x，y 抬高 3*(60-heightArg)（bundle:31452-31454，hu[61]=60）。
    this.blowUpCurve.p1 = {
      x: this.blowUpCurve.ug.x + (this.blowUpCurve.p2.x - this.blowUpCurve.ug.x) / 2,
      y: this.blowUpCurve.ug.y - 3 * (60 - heightArg),
    };
    this.blowUpCurve.time = 0;
    this.blowUpState = 1; // ZE=1
    // hit(当前血量-0.1) 致濒死（bundle:31455 hit(Zi-0.1)）。Zi-0.1>0 故不触发死亡分支。
    this.hit(this.health - 0.1, null);
    // 旋转动画朝向击退方向（bundle:31455-31461 np.angle({hitX,hitY},{m,l})）。
    if (this.animation && typeof this.animation.rotation !== 'undefined') {
      this.animation.rotation = ProjectileMath.displayAngle({ x: hitX, y: hitY }, { x: m, y: l });
    }
    // 注册 Gw 每帧推进定时器（bundle:31461 nx.La("blownUp"+id, this, this.Gw)）。
    // 经 gameLoop.register 注册：GameLoop.update 以 80ms 逻辑子步长调用 Gw(deltaMs)，
    // deltaMs 为真实步长（GameLoop.LOGIC_STEP_MS=80），Gw 内 time += deltaMs/200 正常推进吹飞约 200ms。
    // gameLoop 默认委托 GameLoop.instance() 单例（对齐 bundle nx.instance()）；若该单例未 init（update 未跑），
    // Gw 不被调用——吹飞停滞但不致死（远安全于 frameLoop 传 undefined 导致 NaN 瞬死）。
    this.gameLoop.register(`blownUp${this.id}`, this, this.Gw);
  }

  /**
   * 吹飞每帧推进（bundle:31467-31478，符号 Gw）。
   * deltaMs 由 GameLoop.update 以 80ms 逻辑子步长传入（GameLoop.LOGIC_STEP_MS=80，对应 bundle update 的 _a 子步）。
   * ZE!=1 时直接返回（守卫阻止 gameOver 后继续推进）。归一化时间 QE.time += deltaMs/200（hu[132]=200）；
   * 二次贝塞尔插值（np.Us 等价 ProjectileMath.quadraticBezier）写入敌人位置；time>=1 时 hit(1) 致死 + ZE=0。
   * 吹飞总时长约 200ms（80ms 步长 × 约 2.5 帧 → time 达 1.0 致死）。
   */
  Gw(deltaMs) {
    if (this.blowUpState !== 1) return; // bundle:31474 1==this.ZE 守卫
    this.blowUpCurve.time += deltaMs / 200; // hu[132]=200，吹飞总时长约 200ms
    const reached = ProjectileMath.quadraticBezier(
      this.blowUpCurve.ug,
      this.blowUpCurve.p1,
      this.blowUpCurve.p2,
      this.visual,
      this.blowUpCurve.time,
    );
    if (reached) {
      this.hit(1, null); // 落地致死（bundle:31474 hit(1)）
      this.blowUpState = 0; // ZE=0
    }
  }

  gameOver() {
    if (this.inPool || this.__InPool) return false;
    if (this.animation) this.presentation.stopMoving(this);
    // 吹飞清理（bundle:31337-31344）：
    //   nx.wa("blownUp") 取消吹飞定时器。bundle 原版 wa("blownUp") 无 id，是 Map 精确匹配键 "blownUp"，
    //   实际无法注销 "blownUp"+id 定时器——KR1：照 bundle 原样意图取消吹飞定时器。
    //   src 做得更正确：用 gameLoop.unregister("blownUp"+id) 精确注销本实例的 Gw 定时器
    //   （对应 Xw 的 register("blownUp"+id)）；ZE=0 守卫双重阻止后续推进。
    this.gameLoop.unregister(`blownUp${this.id}`);
    // Tween.killAll(tw)：清除动画上的所有 Tween（bundle:31344 Laya.Tween.killAll(this.tw)）。
    if (this.laya.Tween && typeof this.laya.Tween.killAll === 'function' && this.animation) {
      this.laya.Tween.killAll(this.animation);
    }
    const animation = this.animation;
    // 先置 ZE=0 守卫，再调 super.gameOver（其 clearAll(this) 会清剩余定时器/Tween）。
    this.blowUpState = 0;
    const result = super.gameOver();
    // enemy.filters=null（bundle:31344）。
    if (this.visual && 'filters' in this.visual) this.visual.filters = null;
    if (animation) {
      this.presentation.resetAnimation(animation);
      if (typeof animation.removeSelf === 'function') animation.removeSelf();
      if (typeof animation.recover === 'function') animation.recover();
    }
    this.animation = null;
    return result;
  }
}

module.exports = { NormalEnemyBase };
