'use strict';
const { ConfiguredEnemy } = require('./ConfiguredEnemy');
const PUPPET_HEALTH_MULTIPLIERS = Object.freeze([1,1.2,1.4,1.6,1.8]);
class PuppetEnemy extends ConfiguredEnemy {
  constructor(){ super({ typeKey:'Puppet', typeIndex:6, resourcePath:'resources/img/gameObject/soldier/soldier_0.png' }); this.puppetLevel=1; this.soldierSkinIndex=0; }
  configurePuppet({ level=1, soldierSkinIndex=0, startPosition=null, firstPathCenter=null, pathIndex=0 }={}) {
    this.puppetLevel=Math.max(1,Math.min(5,Number(level)||1)); this.soldierSkinIndex=Number(soldierSkinIndex)||0;
    this.resourcePath=`resources/img/gameObject/soldier/soldier_${this.soldierSkinIndex}.png`;
    if(startPosition) Object.assign(this.startPosition,startPosition); if(firstPathCenter) Object.assign(this.firstPathCenter,firstPathCenter); this.currentPathIndex=Number(pathIndex)||0; return this;
  }
  init(playerLane){ super.init(playerLane); const m=PUPPET_HEALTH_MULTIPLIERS[this.puppetLevel-1]; this.maxHealthBase*=m; this.health=this.maxHealthBase; return this; }
}
PuppetEnemy.originalSymbol='oo'; PuppetEnemy.sourceRange='31784-31923';
module.exports={ PuppetEnemy, PUPPET_HEALTH_MULTIPLIERS };
