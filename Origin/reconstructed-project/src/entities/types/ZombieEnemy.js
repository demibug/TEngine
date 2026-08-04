'use strict';
const { ConfiguredEnemy } = require('./ConfiguredEnemy');
const { EnemyRuntimeState } = require('../EnemyBase');

/**
 * 重建模块：ENEMY-RUNTIME-01 / Zombie
 * 原始范围：bundle.strings-decoded.js:31932-32135
 * 原始主要符号：sQ
 * 重建状态：沼泽浮现状态机 + 气泡粒子恢复（7 方法 uB/Hw/gB/bubble/fw/mw/tB/dB）
 *
 * bundle 字段映射：
 *   lB=浮现阶段(0..3)  cB={x,y,index}  bubbles=[]  fB=出生回调  pB=沼泽贴图  yB=遮罩 Sprite
 *   tw=animation 节点(src this.animation)  enemy=visual 节点(src this.visual)
 *
 * BREAKING（还原内部契约）：Zombie 不走 NormalEnemyBase.init 的 presentation.playSpawn 出生路径，
 * 改用 Hw(callback)→gB 三阶段浮现状态机（沼泽淡入→上升冒泡→遮罩露出+淡出），
 * 到位后调 fB 回调触发 changeState(MOVING)。气泡仅在 phase2/3 生成。
 *
 * hook 映射（bundle→src）：
 *   Hw(callback)→覆写 init 内出生段（不调 playSpawn，改调 uB()+Hw()）
 *   fw()→startMovingAnimation()  mw()→stopMovingAnimation()  tB()→呼吸经 port
 *
 * gB 由 gameLoop.register("mob1_"+id) 每帧驱动（bundle:31993 nx.La("mob1_"+id) 等价，
 * 任务组2已注入 gameLoop）。bubble/tB 经 presentation port 承载渲染，逻辑层持状态与调度。
 */
class ZombieEnemy extends ConfiguredEnemy {
  constructor() {
    super({ typeKey: 'Zombie', typeIndex: 4, resourcePath: 'resources/img/gameObject/enemy/zombie.png' });
    // lB：浮现阶段（0=未开始，1=沼泽淡入，2=上升冒泡，3=遮罩露出+淡出）。bundle:31941。
    this.lB = 0;
    // cB：出生坐标缓存{x,y,index}。bundle:31941-31945。index=起始路径索引，Hw 记录到 Lm(currentPathIndex)。
    this.cB = { x: 0, y: 0, index: 0 };
    // bubbles[]：当前并存气泡节点。bundle:31945。
    this.bubbles = [];
    // fB：出生完成回调（等价 pe.playSpawn 的 callback，触发 changeState(MOVING)）。bundle:31993。
    this.fB = null;
    // pB：沼泽贴图节点（swamp.png 64×32）。yB：水位线遮罩 Sprite。经 port 创建，逻辑层持引用。
    this.pB = null;
    this.yB = null;
  }

  /**
   * 覆写 init（bundle:31947-31954）：取代 NormalEnemyBase.init 的 playSpawn 出生路径。
   * ConfiguredEnemy.init 已完成 visual 取池 + super.init(NormalEnemyBase.init: stats/animation/getPath)
   * + baseSpeedOverride + animation.pos。但 NormalEnemyBase.init 末尾会调 playSpawn——Zombie 须避免。
   * 故 Zombie 不调 ConfiguredEnemy.init 的 super.init 段，而是自行编排：取 visual→EnemyBase.init→
   * stats/animation→getPath→uB()→Hw(callback)。Hw 启动 gB 浮现状态机取代 playSpawn。
   */
  init(playerLane) {
    this.fastAnimation = false;
    this.visual = this.objectPool.takeByKey(this.visualPoolKey, this);
    this.enemy = this.visual;
    // 跳过 NormalEnemyBase.init（其末尾调 playSpawn），直接调 EnemyBase.init 完成基础出生注册。
    const { EnemyBase } = require('../EnemyBase');
    EnemyBase.prototype.init.call(this, playerLane);
    // 复刻 NormalEnemyBase.init 的 stats/animation/healthText/getPath 段（bundle:31277-31285 pe.init 中段）。
    this._initializeStatsAndAnimation();
    this.healthText.text = this.health.toFixed(0);
    this.healthText.visible = false;
    this.healthBarBackground.visible = false;
    this.getPath();
    // animation 定位到 visual 脚底中央（bundle:31953 tw.pos(width/2,height)）。
    if (this.animation && typeof this.animation.pos === 'function') {
      this.animation.pos(this.visual.width / 2, this.visual.height);
    }
    // 缓存出生坐标（cB），Hw 用 cB.index 记录起始路径索引。bundle:31953 enemy.pos(cB.x,cB.y)。
    this.cB.x = this.visual.x;
    this.cB.y = this.visual.y;
    this.cB.index = this.currentPathIndex;
    this.visual.visible = false;
    // 创建沼泽贴图+遮罩（bundle:31953 this.uB()）。
    this.uB();
    // 启动浮现状态机取代 playSpawn（bundle pe.init 调 Hw(callback)，Zombie 覆写 Hw 改用 gB）。
    const generation = this._lifecycleGeneration;
    this.Hw(() => {
      if (generation !== this._lifecycleGeneration || this.inPool) return;
      // 等价 pe.init 的 Hw 回调：触发 MOVING + 血条显示（bundle:31286-31288）。
      this.changeState(EnemyRuntimeState.MOVING);
      this.healthBarImmediate.width = 0;
      this.healthBarBackground.visible = true;
      this.healthBarImmediate.width = this.healthBarWidth;
    });
    return this;
  }

  /**
   * _initializeStatsAndAnimation 委托 NormalEnemyBase（复用其 stats 解析+血量÷2+animation 创建）。
   * Zombie typeIndex=4 血量减半已在 NormalEnemyBase._initializeStatsAndAnimation 实现（bundle:31386）。
   */
  _initializeStatsAndAnimation() {
    const { NormalEnemyBase } = require('../NormalEnemyBase');
    NormalEnemyBase.prototype._initializeStatsAndAnimation.call(this);
  }

  /**
   * 沼泽贴图 + 遮罩创建（bundle:31970-31987，符号 uB）。
   * 经 presentation port createSwampDecal 创建 swamp.png(64×32) 地面层贴图 pB(pos(8,47),alpha=0,zIndex=-1)
   * 与水位线遮罩 Sprite yB，并设 animation.mask=yB（实现"水位线露出水面以下"裁剪效果）。
   * port 持渲染细节（Laya.Image/Sprite），逻辑层持 pB/yB 引用供 gB/dB 操作 alpha/graphics。
   */
  uB() {
    const decal = this.presentation.createSwampDecal(this);
    // decal={pB,yB}：pB=沼泽贴图，yB=遮罩 Sprite。port 已将 pB addChild 到 visual 并设初始 alpha=0/zIndex=-1。
    this.pB = decal.pB;
    this.yB = decal.yB;
    // 设动画遮罩（bundle:31982 tw.mask=yB）。mask 是节点属性，逻辑层设。
    this.animation.mask = this.yB;
  }

  /**
   * 出生 hook（bundle:31988-31997，符号 Hw）：启动浮现状态机取代 pe 的 playSpawn。
   * enemy.visible=true；记录 cB.index 到 Lm(currentPathIndex) 作为起始路径索引；存 fB=callback；
   * 置 lB=1（进入 phase1）；注册 gB 定时器驱动（bundle:31993 nx.La("mob1_"+id) 等价 gameLoop.register）。
   */
  Hw(callback) {
    this.visual.visible = true;
    this.currentPathIndex = this.cB.index;
    this.fB = callback;
    this.lB = 1;
    this.gameLoop.register(`mob1_${this.id}`, this, this.gB);
  }

  /**
   * 浮现三阶段状态机（bundle:31999-32028，符号 gB）。由 gameLoop 每帧以真实步长调用。
   * hu 常量：hu[132]=200(淡入时长ms) hu[200]=160(phase1末y) hu[65]=80(上升速度px/s+phase3末y)
   *          hu[261]=140(phase2末y) hu[123]=1000(秒转ms)。
   *   phase1(lB==1)：沼泽 alpha 淡入(200ms)；alpha>=1 夹紧→tw.y=160→进入 phase2。
   *   phase2(lB==2)：tw.y -= 80*deltaMs/1000 上升；每帧 bubble；tw.y<=140 夹紧→进入 phase3。
   *   phase3(lB==3)：tw.y -= 80*deltaMs/1000 上升；动态遮罩 drawRect 露出水面以下；
   *                 沼泽 alpha 淡出(1s)；每帧 bubble；tw.y<=80 夹紧→调 fB()(出生回调触发MOVING)+dB()清理。
   */
  gB(deltaMs) {
    const SWAMP_FADE_MS = 200;   // hu[132]
    const RISE_SPEED = 80;       // hu[65] px/s
    const MS_PER_S = 1000;       // hu[123]
    const PHASE1_END_Y = 160;    // hu[200]
    const PHASE2_END_Y = 140;    // hu[261]
    const PHASE3_END_Y = 80;     // hu[65]
    if (this.lB === 1) {
      // phase1：沼泽贴图淡入。
      this.pB.alpha += deltaMs / SWAMP_FADE_MS;
      if (this.pB.alpha >= 1) {
        this.pB.alpha = 1;
        this.animation.y = PHASE1_END_Y;
        this.lB = 2;
      }
    } else if (this.lB === 2) {
      // phase2：上升 + 冒泡。
      this.animation.y -= RISE_SPEED * deltaMs / MS_PER_S;
      this.bubble(deltaMs);
      if (this.animation.y <= PHASE2_END_Y) {
        this.animation.y = PHASE2_END_Y;
        this.lB = 3;
      }
    } else if (this.lB === 3) {
      // phase3：上升 + 动态遮罩 + 沼泽淡出 + 冒泡。
      this.animation.y -= RISE_SPEED * deltaMs / MS_PER_S;
      // 动态遮罩：drawRect 露出水面以下部分（bundle:32023 yB.drawRect(-width/2,-height,width,140-tw.y)）。
      // 遮罩高度=PHASE2_END_Y - tw.y，随 tw 上升而增大，露出更多身体。
      this.yB.graphics.clear();
      this.yB.graphics.drawRect(
        -this.visual.width / 2,
        -this.visual.height,
        this.visual.width,
        PHASE2_END_Y - this.animation.y,
        '#fff',
      );
      // 沼泽贴图淡出（bundle:32023 pB.alpha -= deltaMs/1000，约 1s 淡出）。
      this.pB.alpha -= deltaMs / MS_PER_S;
      this.bubble(deltaMs);
      if (this.animation.y <= PHASE3_END_Y) {
        this.animation.y = PHASE3_END_Y;
        // 到位：调出生回调（触发 changeState(MOVING)）+ 清理浮现资源。
        if (this.fB) this.fB();
        this.dB();
      }
    }
  }

  /**
   * 气泡粒子（bundle:32030-32062，符号 bubble）。仅 phase2/3 由 gB 调用。
   * hu 常量：hu[43]=40(气泡初始y + 上升速度px/s) hu[24]=70(气泡x上限) hu[123]=1000 hu[81]=100(淡出ms)。
   *   生成：Math.random()<0.05 && bubbles.length<3；经 port createBubbleParticle 创建气泡节点；
   *         pos(np.range(10,70), 40)；push bubbles。
   *   每帧更新：bubbles[i].y -= 40*deltaMs/1000 上升；alpha -= 0.7*deltaMs/1000 衰减；
   *   出水面(y<=0)：经 port recoverBubbleParticle 淡出回收（bundle Laya.Tween.to alpha:0 100ms 后回收），
   *                 立即 splice。
   */
  bubble(deltaMs) {
    const BUBBLE_Y = 40;        // hu[43] 初始 y + 上升速度
    const MS_PER_S = 1000;      // hu[123]
    const ALPHA_DECAY = 0.7;    // bundle:32050 .7*deltaMs/1000
    // 生成判定（5% 概率 / 上限 3）。
    if (Math.random() < 0.05 && this.bubbles.length < 3) {
      const bubble = this.presentation.createBubbleParticle(this);
      if (bubble) {
        // pos(np.range(10,70), 40)（bundle:32047）。np.range(10,70)=10~70 随机 x。
        const x = 10 + Math.random() * (70 - 10);
        if (typeof bubble.pos === 'function') bubble.pos(x, BUBBLE_Y);
        else { bubble.x = x; bubble.y = BUBBLE_Y; }
        this.bubbles.push(bubble);
      }
    }
    // 每帧更新上升 + alpha 衰减 + 出水面回收。
    for (let i = this.bubbles.length - 1; i >= 0; i--) {
      const b = this.bubbles[i];
      b.y -= BUBBLE_Y * deltaMs / MS_PER_S;
      b.alpha -= ALPHA_DECAY * deltaMs / MS_PER_S;
      if (b.y <= 0) {
        // 出水面：经 port 淡出回收（bundle:32053-32057 Tween.to alpha:0 100ms 后 scale/alpha 复位+removeSelf+recover）。
        this.bubbles.splice(i, 1);
        this.presentation.recoverBubbleParticle(b);
      }
    }
  }

  /**
   * 移动动画启动 hook（bundle:32064-32071，符号 fw）：启动 tB 蹒跚呼吸。
   * src 中 fw 对应 startMovingAnimation（由 _enterState(MOVING) 调用）。
   */
  startMovingAnimation() {
    super.startMovingAnimation();
    this.tB();
  }

  /**
   * 移动动画停止 hook（bundle:32072-32081，符号 mw）：killAll Tween + scale(1,1) 复位。
   * src 中 mw 对应 stopMovingAnimation（由 _exitState(MOVING) 调用）。
   */
  stopMovingAnimation() {
    this.presentation.stopZombieBreathing(this);
    super.stopMovingAnimation();
  }

  /**
   * 蹒跚呼吸启动（bundle:32083-32103，符号 tB）：三段链式 Tween 自循环。
   * 段1: scaleX1.06/scaleY0.93/y+4 duration=2/15*1000/bm≈133/bm ms (hu[12]=15 hu[123]=1000)
   * 段2: scaleX1.08/scaleY0.91/y+3 duration=1/30*1000/bm≈33/bm ms (hu[22]=30)
   * 段3: scaleX1/scaleY1/y原 duration=1/6*1000/bm≈167/bm ms
   * 完成后 then(tB,this) 自循环。bm=playbackRate（按移动速率缩放）。
   * 经 presentation port startZombieBreathing 承载 Tween（生产层用 Laya.Tween.create chain/then；
   * 逻辑层只调度，不直接操作 Laya.Tween）。
   */
  tB() {
    this.presentation.startZombieBreathing(this);
  }

  /**
   * 浮现资源清理（bundle:32105-32131，符号 dB）。
   * 取消 gB 定时器（gameLoop.unregister "mob1_"+id）；清除 animation.mask；
   * 移除沼泽贴图 pB + alpha=0；遍历 bubbles 经 port 淡出回收；lB=0。
   */
  dB() {
    this.gameLoop.unregister(`mob1_${this.id}`);
    this.animation.mask = null;
    if (this.yB && this.yB.graphics) this.yB.graphics.clear();
    if (this.pB) {
      if (typeof this.pB.removeSelf === 'function') this.pB.removeSelf();
      this.pB.alpha = 0;
    }
    this.lB = 0;
    // 遍历气泡逐个淡出回收（bundle:32119-32127 Tween.to alpha:0 100ms 后回收）。
    for (let i = this.bubbles.length - 1; i >= 0; i--) {
      const b = this.bubbles[i];
      this.bubbles.splice(i, 1);
      this.presentation.recoverBubbleParticle(b);
    }
  }

  /**
   * 覆写 gameOver（bundle:31955-31958）：先 dB() 清理浮现资源（定时器/遮罩/贴图/气泡），
   * 再 super.gameOver()（ConfiguredEnemy.gameOver：NormalEnemyBase.gameOver + visual 回收）。
   * NormalEnemyBase.gameOver 已处理吹飞定时器/Tween 清理；dB 处理 Zombie 专属浮现资源。
   */
  gameOver() {
    if (this.inPool || this.__InPool) return false;
    this.dB();
    return super.gameOver();
  }
}
ZombieEnemy.originalSymbol = 'sQ';
ZombieEnemy.sourceRange = '31932-32135';
module.exports = { ZombieEnemy };
