'use strict';
const { OriginProjectRuntime } = require('./OriginProjectRuntime');
const { LayaEnemyPresentation } = require('../presentation/LayaEnemyPresentation');
const { LayaSkillPresentation } = require('../skills/presentation/LayaSkillPresentation');

class OriginRuntimeServices {
  constructor({Laya,assetPrefix='',audioRegistry=null,vfxRegistry=null,animationEntityPool=null,layerResolver=null,logger=console}={}){
    this.runtime=new OriginProjectRuntime({Laya,assetPrefix,logger});
    this.enemyPresentation=new LayaEnemyPresentation({Laya,prefabFactory:this.runtime.prefabs,logger});
    this.skillPresentation=new LayaSkillPresentation({laya:Laya,audioRegistry,vfxRegistry,animationEntityPool,prefabFactory:this.runtime.prefabs,spineFactory:(key,path)=>this.runtime.createSpine(key||path),resourceCatalog:this.runtime,resourcePathResolver:path=>this.runtime.resolvePath(path),layerResolver,logger});
  }
  async preloadCritical(){return this.runtime.preloadCritical();}
  registerObjectPoolPrefabs(objectPool){
    objectPool.registerKey('mob',()=>this.runtime.prefabs.createSync('mob'),visual=>this.enemyPresentation.resetVisual(visual));
    objectPool.registerKey('boss',()=>this.runtime.prefabs.createSync('boss'),visual=>this.enemyPresentation.resetVisual(visual));
    for(const key of ['mapItem','trail','knifeHit','bowHit','pikeHit','cavalryHit','lvlUpEff','lvlDownEff','heart','loveHeart']){
      objectPool.registerKey(key,()=>this.runtime.prefabs.createSync(key),node=>{if(node){node.removeSelf();node.visible=true;node.alpha=1;node.rotation=0;node.scale(1,1);}});
    }
    return this;
  }
}
module.exports={OriginRuntimeServices};
