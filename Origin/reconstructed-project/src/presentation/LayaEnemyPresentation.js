'use strict';
const { createLayaSpineAnimation } = require('./LayaSpineAnimation');

/** Resource-backed enemy presentation using origin_project prefabs and Spine2D. */
class LayaEnemyPresentation {
  constructor({ Laya, prefabFactory, spawnDurationMs=250, deathDurationMs=350, logger=console }={}) {
    if(!Laya||!prefabFactory)throw new TypeError('LayaEnemyPresentation requires Laya and LayaPrefabFactory');
    Object.assign(this,{Laya,prefabFactory,spawnDurationMs,deathDurationMs,logger});this.calls=[];
  }
  createMobVisual(){return this.prefabFactory.createSync('mob');}
  createBossVisual(){return this.prefabFactory.createSync('boss');}
  resetVisual(visual){if(!visual)return;this.Laya.timer.clearAll(visual);if(this.Laya.Tween&&this.Laya.Tween.killAll)this.Laya.Tween.killAll(visual);visual.offAll&&visual.offAll();visual.removeSelf();visual.visible=true;visual.alpha=1;visual.rotation=0;visual.scale(1,1);visual.pos(0,0);for(const child of [...(visual.children||[])])if(child.name==='sp')child.removeSelf();const stun=visual.getChildByName&&visual.getChildByName('stun');if(stun)stun.visible=false;}
  createAnimation(_enemy,resourcePath,fastMode=false){const animation=createLayaSpineAnimation(this.Laya,resourcePath);animation.setIsFastMode(Boolean(fastMode));this.calls.push(['createAnimation',resourcePath,Boolean(fastMode)]);return animation;}
  playSpawn(enemy,complete){const visual=enemy.visual;visual.visible=true;visual.alpha=1;visual.scale(0,0);const finish=()=>{visual.scale(1,1);complete&&complete();};if(this.Laya.Tween&&this.Laya.Tween.to)this.Laya.Tween.to(visual,{scaleX:1,scaleY:1},this.spawnDurationMs,null,this.Laya.Handler&&this.Laya.Handler.create?this.Laya.Handler.create(this,finish):null);else this.Laya.timer.once(this.spawnDurationMs,enemy,finish);}
  playDeath(enemy,_color,complete){const visual=enemy.visual;const finish=()=>{visual.alpha=0;complete&&complete();};if(this.Laya.Tween&&this.Laya.Tween.to)this.Laya.Tween.to(visual,{alpha:0},this.deathDurationMs,null,this.Laya.Handler&&this.Laya.Handler.create?this.Laya.Handler.create(this,finish):null);else this.Laya.timer.once(this.deathDurationMs,enemy,finish);}
  setMovePlaybackRate(enemy,rate){if(enemy.animation&&enemy.animation.playbackRate)enemy.animation.playbackRate(rate);}
  startMoving(enemy){if(enemy.animation&&enemy.moveAnimation)enemy.animation.play(enemy.moveAnimation,true);}
  stopMoving(enemy){if(enemy.animation&&enemy.animation.stop)enemy.animation.stop();}
  startMob0Breathing(){}
  stopMob0Breathing(){}
  playHitReaction(enemy){const stun=enemy.visual&&enemy.visual.getChildByName&&enemy.visual.getChildByName('stun');if(stun){stun.visible=true;this.Laya.timer.once(120,enemy,()=>{if(stun&&!stun.destroyed)stun.visible=false;});}}
  resetAnimation(animation){if(animation&&animation.resetForPool)animation.resetForPool();}
  resetForPool(enemy){if(enemy&&enemy.visual)this.resetVisual(enemy.visual);}
  createFootprint(){return null;}
  /**
   * Zombie 沼泽贴图 + 遮罩创建（bundle:31970-31987，符号 uB 表现层）。
   * 创建 swamp.png(64×32) 地面层贴图 pB：pos(8,47)、alpha=0、zIndex=-1，addChild 到 enemy(visual)。
   * 创建水位线遮罩 Sprite yB：drawRect(0,0,enemy.width,0,"#fff")（初始高度 0，gB phase3 动态绘制）。
   * 返回 {pB,yB} 供逻辑层 uB 设 animation.mask=yB 与 gB/dB 操作 alpha/graphics。
   * hu[51]=64 hu[31]=32 hu[55]=47（swamp 贴图尺寸与位置）。
   */
  createSwampDecal(enemy){
    const Laya=this.Laya;const visual=enemy.visual;
    const SWAMP_W=64,SWAMP_H=32,SWAMP_X=8,SWAMP_Y=47; // hu[51]/hu[31]/pos(8,hu[55])
    // pB：沼泽贴图（复用，避免重复创建）。bundle:31982 this.pB||(this.pB=new Laya.Image(swamp.png))。
    let pB=visual.getChildByName&&visual.getChildByName('swampDecal');
    if(!pB){pB=new Laya.Image('resources/img/gameObject/enemy/swamp.png');pB.name='swampDecal';pB.size(SWAMP_W,SWAMP_H);pB.pos(SWAMP_X,SWAMP_Y);visual.addChild(pB);}
    pB.alpha=0;pB.zIndex=-1;
    // yB：水位线遮罩 Sprite（bundle:31982 new Laya.Sprite + drawRect(0,0,width,0,#fff)）。
    let yB=visual.getChildByName&&visual.getChildByName('swampMask');
    if(!yB){yB=new Laya.Sprite();yB.name='swampMask';visual.addChild(yB);}
    yB.graphics.clear();yB.graphics.drawRect(0,0,visual.width,0,'#fff');
    this.calls.push(['createSwampDecal',enemy.id]);
    return {pB,yB};
  }
  /**
   * Zombie 气泡粒子创建（bundle:32046 rw.getItem("bubble") 表现层）。
   * 创建气泡 Sprite（bubble.png），返回节点供逻辑层 bubble 持有并驱动 y/alpha。
   * 逻辑层 pos(x,40)/scale(0,0)；port 只创建节点。
   */
  createBubbleParticle(enemy){
    const Laya=this.Laya;
    let bubble;
    if(Laya.Pool&&typeof Laya.Pool.getItemByCreateFun==='function'){
      bubble=Laya.Pool.getItemByCreateFun('bubble',()=>{const s=new Laya.Sprite();s.size(16,16);return s;},this);
    }else{bubble=new Laya.Sprite();bubble.size(16,16);}
    bubble.alpha=1;bubble.scaleX=1;bubble.scaleY=1;
    if(enemy.visual&&typeof enemy.visual.addChild==='function')enemy.visual.addChild(bubble);
    this.calls.push(['createBubbleParticle',enemy.id]);
    return bubble;
  }
  /**
   * Zombie 气泡粒子淡出回收（bundle:32053-32057/32122-32126 表现层）。
   * Tween.to(alpha:0,100ms) 完成后 scale(1,1)/alpha=1 复位 + removeSelf + recover("bubble")。
   * hu[81]=100（淡出时长 ms）。
   */
  recoverBubbleParticle(bubble){
    if(!bubble)return;
    const Laya=this.Laya;const FADE_MS=100; // hu[81]
    const finish=()=>{bubble.scale(1,1);bubble.alpha=1;if(typeof bubble.removeSelf==='function')bubble.removeSelf();if(Laya.Pool&&typeof Laya.Pool.recover==='function')Laya.Pool.recover('bubble',bubble);};
    if(Laya.Tween&&Laya.Tween.to&&Laya.Handler&&Laya.Handler.create){Laya.Tween.to(bubble,{alpha:0},FADE_MS,null,Laya.Handler.create(this,finish));}
    else if(Laya.timer){Laya.timer.once(FADE_MS,this,finish);}
    else{finish();}
    this.calls.push(['recoverBubbleParticle']);
  }
  /**
   * Zombie 蹒跚呼吸启动（bundle:32083-32103，符号 tB 表现层）。
   * 三段链式 Tween 自循环（then(tB) 自调用）：
   *   段1: scaleX1.06/scaleY0.93/y+4  duration=2/15*1000/bm (hu[12]=15 hu[123]=1000)
   *   段2: scaleX1.08/scaleY0.91/y+3  duration=1/30*1000/bm (hu[22]=30)
   *   段3: scaleX1/scaleY1/y原         duration=1/6*1000/bm
   * bm=enemy.playbackRate（按移动速率缩放）。m=tw.y 记录起始 y。
   * 用 Laya.Tween.create(tw).to().duration().chain()...then() 链式 API。
   * 缺少 chain/then 支持时退化为单次 Tween（呼吸不循环但不报错）。
   */
  startZombieBreathing(enemy){
    const Laya=this.Laya;const tw=enemy.animation;if(!tw)return;
    const bm=enemy.playbackRate||1;const MS_PER_S=1000; // hu[123]
    const m=tw.y;
    const d1=2/15*MS_PER_S/bm; // hu[12]=15
    const d2=1/30*MS_PER_S/bm; // hu[22]=30
    const d3=1/6*MS_PER_S/bm;
    this.calls.push(['startZombieBreathing',enemy.id]);
    if(Laya.Tween&&Laya.Tween.create&&typeof Laya.Tween.create(tw).chain==='function'){
      // 链式自循环（bundle:32099 原版链式 then(tB,this) 自调用）。
      // 注：Laya.Tween.create 的 to/duration/chain/then API 与 bundle 一致。
      const run=()=>{Laya.Tween.create(tw).to('scaleX',1.06).to('scaleY',0.93).to('y',m+4).duration(d1).chain().to('scaleX',1.08).to('scaleY',0.91).to('y',m+3).duration(d2).chain().to('scaleX',1).to('scaleY',1).to('y',m).duration(d3).then(run,this);};
      run();
    }else if(Laya.Tween&&Laya.Tween.to){
      // 退化：单段 Tween（不循环）。
      Laya.Tween.to(tw,{scaleX:1.06,scaleY:0.93,y:m+4},d1);
    }
  }
  /**
   * Zombie 蹒跚呼吸停止（bundle:32072-32081 mw 表现层）：killAll Tween + scale(1,1) 复位。
   */
  stopZombieBreathing(enemy){
    const Laya=this.Laya;const tw=enemy.animation;
    if(Laya.Tween&&typeof Laya.Tween.killAll==='function'&&tw)Laya.Tween.killAll(tw);
    if(tw&&typeof tw.scale==='function')tw.scale(1,1);
    this.calls.push(['stopZombieBreathing',enemy&&enemy.id]);
  }
  /**
   * Cavalry 黄圈光环创建（bundle:32400-32412，符号 iB 表现层）。
   * 创建 yellowCircle.png(80×30) 脚下地面层贴图：size(hu[65]=80,hu[22]=30)、pos(0,hu[43]=40)、zIndex=-1，addChild 到 enemy(visual)。
   * bundle 原用 this.iB||(this.iB=new Laya.Image(...)) 复用模式（gameOver 仅 removeSelf 不销毁，下次 init 复用同一 iB）。
   * 此处按名 'cavalryAura' 查 visual 已有子节点复用；无则创建。返回光环节点供逻辑层持引用。
   */
  createCavalryAura(enemy,auraResource){
    const Laya=this.Laya;const visual=enemy.visual;
    const AURA_W=80,AURA_H=30,AURA_X=0,AURA_Y=40; // hu[65]/hu[22]/pos(0,hu[43])
    // 复用模式（bundle:32403 this.iB||(...)）：优先取已缓存的 cavalryAura 子节点。
    let aura=visual.getChildByName&&visual.getChildByName('cavalryAura');
    if(!aura){aura=new Laya.Image(auraResource);aura.name='cavalryAura';aura.size(AURA_W,AURA_H);aura.pos(AURA_X,AURA_Y);visual.addChild(aura);}
    aura.zIndex=-1;
    this.calls.push(['createCavalryAura',enemy.id]);
    return aura;
  }
  /**
   * Cavalry 黄圈光环移除（bundle:32413-32417 iB.removeSelf 表现层）。
   * removeSelf 将光环移出 visual 显示树但不销毁节点（下次 init 经 createCavalryAura 按名复用）。
   */
  removeCavalryAura(enemy){
    const visual=enemy&&enemy.visual;
    const aura=visual&&visual.getChildByName&&visual.getChildByName('cavalryAura');
    if(aura&&typeof aura.removeSelf==='function')aura.removeSelf();
    this.calls.push(['removeCavalryAura',enemy&&enemy.id]);
  }
  /**
   * Cavalry 骑兵呼吸启动（bundle:32447-32461，符号 tB 表现层）。
   * 纵向颠簸三段链式 Tween 自循环（then(tB) 自调用），仅改 scaleY：
   *   段1: scaleY 0.78  duration=hu[171]=130 ms
   *   段2: scaleY 0.82  duration=130 ms
   *   段3: scaleY 0.8   duration=130 ms
   * 用 Laya.Tween.create(tw).to('scaleY',v).duration(d).chain()...then() 链式 API。
   * 缺少 chain/then 支持时退化为单段 Tween（呼吸不循环但不报错）。
   * 幅度（0.78~0.82）大于 Zombie/Puppet，对应骑兵颠簸步态。
   */
  startCavalryBreathing(enemy){
    const Laya=this.Laya;const tw=enemy.animation;if(!tw)return;
    const D=130; // hu[171]
    this.calls.push(['startCavalryBreathing',enemy.id]);
    if(Laya.Tween&&Laya.Tween.create&&typeof Laya.Tween.create(tw).chain==='function'){
      // 链式自循环（bundle:32457 原版链式 then(this.tB,this) 自调用）。
      const run=()=>{Laya.Tween.create(tw).to('scaleY',0.78).duration(D).chain().to('scaleY',0.82).duration(D).chain().to('scaleY',0.8).duration(D).then(run,this);};
      run();
    }else if(Laya.Tween&&Laya.Tween.to){
      // 退化：单段 Tween（不循环）。
      Laya.Tween.to(tw,{scaleY:0.78},D);
    }
  }
  /**
   * Cavalry 骑兵呼吸停止（bundle:32436-32445 mw 表现层）：killAll Tween + scale(1,1) 复位。
   */
  stopCavalryBreathing(enemy){
    const Laya=this.Laya;const tw=enemy.animation;
    if(Laya.Tween&&typeof Laya.Tween.killAll==='function'&&tw)Laya.Tween.killAll(tw);
    if(tw&&typeof tw.scale==='function')tw.scale(1,1);
    this.calls.push(['stopCavalryBreathing',enemy&&enemy.id]);
  }
  /**
   * Puppet 爱心粒子创建（bundle:31902-31909 rw.getItem("loveHeart") 表现层）。
   * 经 Laya.Pool 取池对象 oB（loveHeart 池，prefab/loveHeart.lh）。bundle 原版 rw.instance().getItem("loveHeart",this)。
   * 返回爱心节点供逻辑层 rB 持有（push {img:oB, targetScale}）并驱动 scale/pos/alpha。
   * port 只创建/取池节点；scale(0,0)/pos/addChild 由逻辑层 rB 设置（bundle:31907-31909）。
   */
  createPuppetHeart(enemy){
    const Laya=this.Laya;
    let heart;
    if(Laya.Pool&&typeof Laya.Pool.getItemByCreateFun==='function'){
      // 池创建回调：loveHeart prefab（PrefabCatalog: prefab/loveHeart.lh）。无 prefab 时用 Sprite 占位。
      heart=Laya.Pool.getItemByCreateFun('loveHeart',()=>{const s=new Laya.Sprite();s.size(16,16);return s;},this);
    }else{heart=new Laya.Sprite();heart.size(16,16);}
    heart.alpha=1;heart.scaleX=0;heart.scaleY=0;
    this.calls.push(['createPuppetHeart',enemy.id]);
    return heart;
  }
  /**
   * Puppet 爱心粒子更新（bundle:31911-31919 表现层）。
   * 逻辑层 rB 直接读写 oB.scaleX/scaleY/alpha（节点属性），port 此方法为表现契约占位，
   * 供未来 P2 表现层接入时承载额外渲染（如粒子特效/Tween）。当前逻辑层直接操作节点属性，
   * 此方法记录调用供测试断言（可选调用）。
   * bundle 原版每帧 oB.scaleX+=deltaMs/3000；scaleX>=target → alpha-=deltaMs/1000；alpha<=0 回收。
   */
  updatePuppetHeart(enemy,heart,_deltaMs){
    this.calls.push(['updatePuppetHeart',enemy&&enemy.id]);
  }
  /**
   * Puppet 爱心粒子回收（bundle:31915-31919/31817-31820 表现层）。
   * removeSelf + alpha=1 复位 + recover("loveHeart",oB)。gameOver 遍历 eB 全部回收。
   * bundle:31918 oB.removeSelf(); oB.alpha=1; rw.instance().recover("loveHeart",oB)。
   */
  recoverPuppetHeart(heart){
    if(!heart)return;
    if(typeof heart.removeSelf==='function')heart.removeSelf();
    heart.alpha=1;
    const Laya=this.Laya;
    if(Laya.Pool&&typeof Laya.Pool.recover==='function')Laya.Pool.recover('loveHeart',heart);
    this.calls.push(['recoverPuppetHeart']);
  }
  /**
   * Cavalry 横扫视觉对象创建（DEFERRED 桩 no-op，实体 VFX 归 P2）。
   * 对齐 bundle:24818-24820：原版 vA.gx(n) 创建两个 sweep 视觉对象 m/o（m=vA.instance().gx(n); o=vA.instance().gx(n)），
   * 延迟 b[112]→150ms 后 m.LS(); o.LS()（命中）。此处只定义 port 契约签名，不创建实际视觉对象、不渲染。
   * P2 正式 Spine/VFX 接入后由表现层实现：创建两个 sweep 视觉对象并返回（或返回句柄供逻辑层持生命周期标志）。
   * 当前 DEFERRED 桩 no-op：不创建渲染对象、不抛异常，返回 null。纯逻辑层 CavalrySweepEffect 只持调度与生命周期标志，
   * 伤害结算（CAVALRY_SWEEP_DELAY_MS=150 双横扫各半攻击力/半径）不依赖本视觉对象。
   */
  createCavalrySweepVisual(_owner,_config){
    // DEFERRED：P2 正式 Spine/VFX 接入，桩 no-op（不渲染、不创建实际视觉对象）。
    this.calls.push(['createCavalrySweepVisual']);
    return null;
  }
  /**
   * Cavalry 横扫视觉对象移除（DEFERRED 桩 no-op，实体 VFX 归 P2）。
   * 对齐 bundle:24818-24820：原版创建的两个 sweep 视觉对象 m/o 在横扫完成后回收。
   * P2 正式 Spine/VFX 接入后由表现层实现：移除/回收传入的 sweep 视觉对象（removeSelf/对象池 recover）。
   * 当前 DEFERRED 桩 no-op：不操作渲染对象、不抛异常。纯逻辑层只做生命周期收尾标志，伤害结算已由规则层 hit() 完成。
   */
  removeCavalrySweepVisual(_visual){
    // DEFERRED：P2 正式 Spine/VFX 接入，桩 no-op（不操作渲染对象）。
    this.calls.push(['removeCavalrySweepVisual']);
  }
  /**
   * 枪尖 Qx 表现对象创建（DEFERRED 桩 no-op，实体 VFX 归 P2）。
   * 对齐 bundle:24585 pikeEff1.png（枪尖特效图 Qx 创建）+ bundle:24736/24740 显隐+Tween：
   *   bundle:24736 段2末 this.Qx.visible=true; Laya.Tween.create(this.Qx).to("y",-b[45]).duration(m)
   *   bundle:24740 段3末 this.Qx.visible=false
   * 此处只定义 port 契约签名，不创建实际视觉对象、不渲染。P2 正式 Spine/Tween 接入后由表现层实现：
   * 创建 pikeEff1.png 枪尖节点并返回句柄。当前 DEFERRED 桩 no-op：返回 null，不抛异常。
   * 纯逻辑层 PikeAttackEffect 命中结算（hit()）不依赖本视觉对象。
   */
  createPikeTipVisual(_owner){
    // DEFERRED：P2 正式 Spine/Tween 接入，桩 no-op（不渲染、不创建实际视觉对象）。
    this.calls.push(['createPikeTipVisual']);
    return null;
  }
  /**
   * 枪尖 Qx 突刺动画（DEFERRED 桩 no-op，实体 VFX 归 P2）。
   * 对齐 bundle:24736：段2末 Qx.visible=true 后 Laya.Tween.create(this.Qx).to("y",-b[45]).duration(m)（Tween 位移）。
   * P2 正式 Spine/Tween 接入后由表现层实现：对传入 Qx 视觉对象执行 Tween.to("y",...) 位移动画（按 durationMs）。
   * 当前 DEFERRED 桩 no-op：不执行 Tween、不操作渲染对象、不抛异常。命中结算由规则层 hit() 驱动不依赖本动画。
   */
  animatePikeTipThrust(_visual,_durationMs){
    // DEFERRED：P2 正式 Spine/Tween 接入，桩 no-op（不执行 Tween、不操作渲染对象）。
    this.calls.push(['animatePikeTipThrust',_durationMs]);
  }
  /**
   * 枪尖 Qx 表现对象隐藏（DEFERRED 桩 no-op，实体 VFX 归 P2）。
   * 对齐 bundle:24740：段3末 this.Qx.visible=false（枪尖特效回收段隐藏）。
   * P2 正式 Spine/Tween 接入后由表现层实现：将传入 Qx 视觉对象置 visible=false（或 removeSelf/对象池 recover）。
   * 当前 DEFERRED 桩 no-op：不操作渲染对象、不抛异常。命中结算已由规则层 hit() 完成，不依赖本隐藏动作。
   */
  hidePikeTipVisual(_visual){
    // DEFERRED：P2 正式 Spine/Tween 接入，桩 no-op（不操作渲染对象）。
    this.calls.push(['hidePikeTipVisual']);
  }
}
module.exports={LayaEnemyPresentation};
