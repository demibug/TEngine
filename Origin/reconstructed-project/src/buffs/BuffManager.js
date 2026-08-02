'use strict';
const { BuffType } = require('./BuffTypes');
const { BuffTimeMode } = require('./BuffTimeMode');
const { createBuffData } = require('./BuffData');
const { definitionFor } = require('./BuffDefinitions');
const { BuffTargetResolver } = require('./BuffTargetResolver');
const { BuffConflictResolver } = require('./BuffConflictResolver');
const { BuffHandlerFactory } = require('./BuffHandlerFactory');
const { assertBuffTarget } = require('./BuffTargetContract');
const { GameEvents } = require('../core/EventBus');

/** 原 vd：战斗 Buff 单例职责的可注入实现。 */
class BuffManager {
  constructor(options = {}) {
    this.activeByTarget = new Map();
    this.nextBuffId = 0;
    this.initialized = false;
    this.started = false;
    this.configure(options);
  }

  configure({ enemyManager=null, unitRegistry=null, gameLoop=null, eventBus=null, objectPool=null, targetResolver=null, conflictResolver=null, handlerFactory=null, logger=console } = {}) {
    this.enemyManager=enemyManager || this.enemyManager;
    this.unitRegistry=unitRegistry || this.unitRegistry;
    this.gameLoop=gameLoop || this.gameLoop;
    this.eventBus=eventBus || this.eventBus;
    this.objectPool=objectPool || this.objectPool;
    this.logger=logger || this.logger;
    this.targetResolver=targetResolver || this.targetResolver || new BuffTargetResolver({enemyManager:this.enemyManager,unitRegistry:this.unitRegistry});
    if (this.targetResolver && typeof this.targetResolver.configure === 'function') this.targetResolver.configure({enemyManager:this.enemyManager,unitRegistry:this.unitRegistry});
    this.conflictResolver=conflictResolver || this.conflictResolver || new BuffConflictResolver();
    this.handlerFactory=handlerFactory || this.handlerFactory || new BuffHandlerFactory({objectPool:this.objectPool});
    return this;
  }

  init(){
    this.handlerFactory.validate();
    if (this.eventBus && !this.initialized) this.eventBus.on(GameEvents.ENEMY_REMOVED, this, this.SE);
    this.initialized=true;
    return this;
  }

  startGame(){
    if(!this.initialized)this.init();
    if(this.gameLoop&&typeof this.gameLoop.register==='function')this.gameLoop.register('BuffMgr',this,this.update);
    this.started=true;
    return this;
  }

  allocateBuffId(){ this.nextBuffId += 1; return this.nextBuffId; }

  mapFor(targetId){
    const id=Number(targetId);
    let map=this.activeByTarget.get(id);
    if(!map){map=new Map();this.activeByTarget.set(id,map);}
    return map;
  }

  applyBuff(targetId,type,num,multiplicative=false,time=BuffTimeMode.PERMANENT,custom=null){
    const target=this.targetResolver.resolve(targetId);
    if(!target){this.logger.log('没有buff作用目标');return -1;}
    assertBuffTarget(target,type);
    type=Number(type);
    const data=createBuffData(num,multiplicative,time,custom);
    let map=this.mapFor(targetId);
    this.conflictResolver.removeReplaced(map,type,oldType=>this.removeType(targetId,oldType));
    map=this.mapFor(targetId);
    if(this.conflictResolver.hasConflict(map,type))return -1;
    let handler=map.get(type);
    let id;
    if(handler) id=handler.add(data);
    else {
      handler=this.handlerFactory.create(type);
      handler.configure({manager:this,target,type,definition:definitionFor(type)});
      map.set(type,handler);
      id=handler.applyData(data);
    }
    return id;
  }

  applyData(targetId,type,data){ return this.applyBuff(targetId,type,data.num,data.Nw,data.time,data.qw); }
  applyCustom(targetId,time,custom){ return this.applyBuff(targetId,BuffType.CUSTOM,0,false,time,custom); }
  getTargetBuffs(targetId){ return this.activeByTarget.get(Number(targetId)); }
  PE(targetId){ return this.getTargetBuffs(targetId); }
  has(targetId,type){ return Boolean(this.activeByTarget.get(Number(targetId))?.get(Number(type))); }
  dE(targetId,type){ return this.has(targetId,type); }

  removeType(targetId,type){
    const id=Number(targetId),map=this.activeByTarget.get(id);
    if(!map)return false;
    const handler=map.get(Number(type));
    if(!handler)return false;
    map.delete(Number(type));
    handler.remove();
    this.handlerFactory.recover(handler);
    if(!map.size)this.activeByTarget.delete(id);
    return true;
  }

  onHandlerEmpty(target,type){
    if(!target)return;
    const map=this.activeByTarget.get(Number(target.id));
    if(!map)return;
    const handler=map.get(Number(type));
    if(!handler||handler.layers.length)return;
    map.delete(Number(type));
    handler.remove();
    this.handlerFactory.recover(handler);
    if(!map.size)this.activeByTarget.delete(Number(target.id));
  }

  update(deltaMs){
    for(const map of [...this.activeByTarget.values()]) {
      for(const handler of [...map.values()]) if(handler.needsUpdate())handler.update(deltaMs);
    }
  }

  Jw(targetId,type,buffId){
    const handler=this.activeByTarget.get(Number(targetId))?.get(Number(type));
    return handler ? handler.removeLayerById(Number(buffId)) : false;
  }
  removeBuff(targetId,type,buffId){ return this.Jw(targetId,type,buffId); }

  modify(targetId,type,buffId,value,multiplicative,time,custom){
    const handler=this.activeByTarget.get(Number(targetId))?.get(Number(type));
    return handler ? handler.modify(Number(buffId),value,multiplicative,time,custom) : false;
  }

  SE(targetId){
    const id=Number(targetId),map=this.activeByTarget.get(id);
    if(!map)return;
    for(const type of [...map.keys()])this.removeType(id,type);
    this.activeByTarget.delete(id);
  }
  clearTarget(targetId){ this.SE(targetId); }

  gameOver(){
    if(this.gameLoop&&typeof this.gameLoop.unregister==='function')this.gameLoop.unregister('BuffMgr');
    for(const id of [...this.activeByTarget.keys()])this.SE(id);
    this.activeByTarget.clear();
    this.started=false;
  }

  get activeTargetCount(){return this.activeByTarget.size;}
  get activeHandlerCount(){let n=0;for(const m of this.activeByTarget.values())n+=m.size;return n;}
}
module.exports = { BuffManager };
