'use strict';
const { SceneControllerBase }=require('./SceneControllerBase');

/** Resource-backed GameOver controller for origin_project/scene/GameOverScene.ls. */
class GameOverSceneController extends SceneControllerBase {
  constructor(...args){super(...args);this.result=null;this.closed=false;this.claimed=false;this.winAnimation=null;this.loseAnimation=null;}
  _fallbackNode(name,Type){if(this[name])return this[name];const laya=this.requireDependency('laya');const node=new (Type||laya.Sprite)();node.name=name;this[name]=node;this.addChild(node);return node;}
  onAwake(){
    const laya=this.requireDependency('laya');
    // Actual _$var bindings from origin_project/scene/GameOverScene.ls.
    for(const name of ['winBg','loseBg','box','goldBg','allGoldNumTxt','winBox','loseBox','rankSp','weaponBox','goldLight','gold','goldNumTxt','getBtn','getBtnAd','getTxt'])this._fallbackNode(name,name.includes('Txt')?laya.Text:laya.Sprite);
    this.getBtn.on(laya.Event.CLICK,this,this.claimAndClose,[false]);
    this.getBtnAd.on(laya.Event.CLICK,this,this.claimAndClose,[true]);
    this._createResultAnimations();
  }
  _createResultAnimations(){const pool=this.deps.animationEntityPool;if(!pool)return;try{this.winAnimation=pool.create('aDou');this.winAnimation.name='winnerADou';this.winAnimation.play('zhan',true);this.winAnimation.scale(2.3,2.3);this.winAnimation.pos(320,790);this.winBox.addChild(this.winAnimation);this.loseAnimation=pool.create('aDou');this.loseAnimation.name='loserADou';this.loseAnimation.play('attack',true);this.loseAnimation.scale(1.8,1.8);this.loseAnimation.pos(320,820);this.loseBox.addChild(this.loseAnimation);}catch(error){this.resourceBindingError=error;}}
  onOpened(result={}){
    this.closed=false;this.claimed=false;this.result=result;
    const isWin=Boolean(result.isWin);this.winBg.visible=this.winBox.visible=isWin;this.loseBg.visible=this.loseBox.visible=!isWin;this.weaponBox.visible=isWin;
    const totalGold=result.totalGold!=null?result.totalGold:(result.playerGold!=null?result.playerGold:result.gold||0);this.allGoldNumTxt.text=String(totalGold);
    this.goldNumTxt.text=`X${result.gold||0}`;if(this.getTxt)this.getTxt.text='领取';
    this.getBtn.visible=true;this.getBtnAd.visible=Boolean(this.deps.platformResultPort&&typeof this.deps.platformResultPort.claimDoubleReward==='function');
    if(this.rankTxt1)this.rankTxt1.text=String(result.rankBefore||'');if(this.rankTxt2)this.rankTxt2.text=String(result.rankAfter||result.rankBefore||'');
    if(this.roundText)this.roundText.text=String(result.round||0);if(this.starText)this.starText.text=String(result.star||0);
    const audio=this.deps.audio;if(audio&&typeof audio.playSound==='function')audio.playSound(isWin?'game_win':'game_lose');
  }
  async claimAndClose(useRewarded=false){if(this.claimed)return;this.claimed=true;this.getBtn.mouseEnabled=false;this.getBtnAd.mouseEnabled=false;let result=this.result||{};if(useRewarded&&this.deps.platformResultPort&&typeof this.deps.platformResultPort.claimDoubleReward==='function')result=await this.deps.platformResultPort.claimDoubleReward(result);else if(this.deps.platformResultPort&&typeof this.deps.platformResultPort.claimReward==='function')result=await this.deps.platformResultPort.claimReward(result);this.returnToMain();return result;}
  restart(){const sceneManager=this.requireDependency('sceneManager');sceneManager.closeScene('GameOverScene');sceneManager.openScene('MatchScene',true);}
  returnToMain(){const sceneManager=this.requireDependency('sceneManager');sceneManager.closeScene('GameOverScene');sceneManager.openScene('MainScene',true);}
  onClosed(){if(this.closed)return;this.closed=true;const laya=this.requireDependency('laya');laya.timer.clearAll(this);if(laya.Tween&&typeof laya.Tween.killAll==='function')laya.Tween.killAll(this);this.getBtn&&this.getBtn.off(laya.Event.CLICK,this,this.claimAndClose);this.getBtnAd&&this.getBtnAd.off(laya.Event.CLICK,this,this.claimAndClose);const pool=this.deps.animationEntityPool;for(const [entity,key] of [[this.winAnimation,'aDou'],[this.loseAnimation,'aDou']])if(entity&&pool){try{pool.recover(entity,key);}catch(_error){entity.removeSelf&&entity.removeSelf();}}this.winAnimation=this.loseAnimation=null;}
}
GameOverSceneController.dependencies={laya:null,sceneManager:null,audio:null,platformResultPort:null,animationEntityPool:null};
module.exports={GameOverSceneController};
