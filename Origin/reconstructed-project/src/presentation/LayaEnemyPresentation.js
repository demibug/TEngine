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
}
module.exports={LayaEnemyPresentation};
