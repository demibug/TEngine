'use strict';
const { OriginRuntimeServices }=require('../resources/OriginRuntimeServices');
const { AnimationEntityPool }=require('../core/AnimationEntityPool');

/**
 * Prepares the resource-backed Laya presentation layer recovered from origin_project.
 * This does not replace the gameplay bootstrap; it supplies real scenes/prefabs/Spine.
 */
class OriginAssetBootstrap {
  constructor({Laya,objectPool=null,animationEntityPool=AnimationEntityPool.instance(),audioRegistry=null,vfxRegistry=null,layerResolver=null,logger=console}={}){
    if(!Laya)throw new TypeError('OriginAssetBootstrap requires Laya');
    this.services=new OriginRuntimeServices({Laya,audioRegistry,vfxRegistry,animationEntityPool,layerResolver,logger});
    this.objectPool=objectPool;this.animationEntityPool=animationEntityPool;
  }
  async prepare(){await this.services.preloadCritical();this.services.runtime.configureAnimationEntityPool(this.animationEntityPool);if(this.objectPool)this.services.registerObjectPoolPrefabs(this.objectPool);return this.services;}
}
module.exports={OriginAssetBootstrap};
