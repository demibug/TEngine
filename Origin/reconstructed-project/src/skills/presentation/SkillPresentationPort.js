'use strict';
class SkillPresentationPort {
  constructor({laya=null,audioRegistry=null,vfxRegistry=null,layerResolver=null,animationEntityPool=null,resourceCatalog=null,logger=console}={}){Object.assign(this,{laya,audioRegistry,vfxRegistry,layerResolver,animationEntityPool,resourceCatalog,logger});this.calls=[];this.ownerNodes=new Map();this.missingResources=[];}
  configure(options={}){Object.assign(this,options);return this;}
  loadSpine(resourcePath){this.calls.push(['loadSpine',resourcePath]);if(this.laya&&this.laya.loader&&typeof this.laya.loader.load==='function')return this.laya.loader.load(resourcePath);return Promise.resolve(null);}
  createSpine(animationKey,resourcePath){this.calls.push(['createSpine',animationKey,resourcePath]);if(this.animationEntityPool)return this.animationEntityPool.create(animationKey);this.requireResource({feature:animationKey,resourceType:'Spine',formalKey:animationKey,expectedPath:resourcePath,animationNames:[],sourceRanges:[]});return null;}
  playAnimation(animation,name,loop=false){this.calls.push(['playAnimation',name,Boolean(loop)]);if(animation&&typeof animation.play==='function')animation.play(name,Boolean(loop));}
  playAnimationSegment(animation,name,startMs,endMs,loop=false){this.calls.push(['playAnimationSegment',name,startMs,endMs,Boolean(loop)]);if(animation&&typeof animation.play==='function')animation.play(name,Boolean(loop));}
  waitForStopped(animation,callback){if(animation&&typeof animation.onStop==='function')animation.onStop(callback);else if(animation&&typeof animation.once==='function'&&this.laya)animation.once(this.laya.Event.STOPPED,this,callback);return callback;}
  listenAnimationEvent(animation,eventName,callback){if(animation&&typeof animation.on==='function')animation.on(eventName,this,callback);return()=>{if(animation&&typeof animation.off==='function')animation.off(eventName,this,callback);};}
  beginBossSkill(owner,skill,timeline){this.calls.push(['beginBossSkill',owner&&owner.id,skill&&skill.key,timeline]);this.playAnimation(owner&&owner.animation,(timeline&&timeline.animation)||owner.attackAnimation||'attack',false);if(this.audioRegistry)this.audioRegistry.playBossSkill(owner.typeKey,owner.id);}
  effectPoint(owner,skill){this.calls.push(['effectPoint',owner&&owner.id,skill&&skill.key]);}
  completeBossSkill(owner){this.calls.push(['completeBossSkill',owner&&owner.id]);this.playAnimation(owner&&owner.animation,owner.idleAnimation||'animation',true);}
  cancelBossSkill(owner,skill,timeline,reason){this.calls.push(['cancelBossSkill',owner&&owner.id,skill&&skill.key,reason]);this.playAnimation(owner&&owner.animation,owner.idleAnimation||'animation',true);this.clearOwner(owner&&owner.id);}
  createOverlay(){throw new Error('SkillPresentationPort.createOverlay() requires a concrete implementation');}
  removeOverlay(handle){if(handle&&typeof handle.remove==='function')handle.remove();}
  createTileMarker(){throw new Error('SkillPresentationPort.createTileMarker() requires a concrete implementation');}
  createEntityVfx(){return null;}
  updateEntityScale(owner,scale){if(owner&&owner.visual&&typeof owner.visual.scale==='function')owner.visual.scale(scale,scale);}
  playSound(key,options){return this.audioRegistry&&this.audioRegistry.play(key,options);}
  stopSound(key,ownerId){return this.audioRegistry&&this.audioRegistry.stop(key,ownerId);}
  showOverlay(key,options){return this.createOverlay(key,options);}
  updateOverlay(handle,values={}){if(handle&&handle.node)Object.assign(handle.node,values);}
  hideOverlay(handle){this.removeOverlay(handle);}
  requireResource(record){const available=this.resourceCatalog&&typeof this.resourceCatalog.has==='function'&&this.resourceCatalog.has(record.expectedPath);const item={...record,presentationStatus:available?'AVAILABLE_IN_ORIGIN_PROJECT':'TODO_RESOURCE_MISSING'};if(!available)this.missingResources.push(item);return item;}
  clearOwner(ownerId){if(ownerId==null)return;const set=this.ownerNodes.get(ownerId);if(set)for(const node of set)if(node&&typeof node.removeSelf==='function')node.removeSelf();this.ownerNodes.delete(ownerId);if(this.audioRegistry)this.audioRegistry.clearOwner(ownerId);}
  track(ownerId,node){if(ownerId==null||!node)return node;let set=this.ownerNodes.get(ownerId);if(!set){set=new Set();this.ownerNodes.set(ownerId,set);}set.add(node);return node;}
  gameOver(){for(const id of [...this.ownerNodes.keys()])this.clearOwner(id);if(this.audioRegistry)this.audioRegistry.gameOver();}
}
module.exports={SkillPresentationPort};
