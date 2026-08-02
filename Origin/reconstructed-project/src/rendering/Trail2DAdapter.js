'use strict';
class Trail2DAdapter {
  constructor({Laya=null,prefabFactory=null,registry=null,parentResolver=null,trailKey='trail'}={}){Object.assign(this,{Laya,prefabFactory,registry,parentResolver,trailKey});this.enabled=false;this.owner=null;this.pendingFade=null;this.node=null;this.component=null;this.paused=false;}
  bind(owner){this.owner=owner;return this;}
  configure(config={}){this.config={...config};if(config.trailKey)this.trailKey=config.trailKey;return this;}
  _resolveComponent(node){if(!node)return null;if(node.getComponent&&this.Laya&&this.Laya.Trail2DRender)return node.getComponent(this.Laya.Trail2DRender);const comps=node._components||node._comp||[];return comps.find(c=>c&&((c.constructor&&c.constructor.name==='Trail2DRender')||c._$type==='Trail2DRender'))||null;}
  create(){if(!this.prefabFactory)throw new Error('Trail2DAdapter requires prefabFactory for origin resources');this.node=this.prefabFactory.createSync(this.trailKey.includes('/')?this.trailKey:`bulletTrail/${this.trailKey}`);this.component=this._resolveComponent(this.node);const parent=this.parentResolver&&this.parentResolver();if(parent)parent.addChild(this.node);this._applyConfig();this.enabled=true;if(this.registry)this.registry.track(this);return this;}
  _applyConfig(){if(!this.component||!this.config)return;for(const key of ['time','minVertexDistance','widthMultiplier','textureMode'])if(this.config[key]!=null)this.component[key]=this.config[key];if(this.config.color&&this.component.color)this.component.color=this.config.color;}
  sync(position){if(!position||!this.node||this.paused)return;this.node.pos(position.x,position.y);if(position.rotation!=null)this.node.rotation=position.rotation;}
  pause(){this.paused=true;if(this.node)this.node.active=false;}
  resume(){this.paused=false;if(this.node)this.node.active=true;}
  fade(delay=0){if(!this.node)return;this.pendingFade=delay;if(this.registry)this.registry.markPending(this);const finish=()=>this.clear();if(delay>0&&this.Laya&&this.Laya.timer)this.Laya.timer.once(delay,this,finish);else finish();}
  clear(){if(this.Laya&&this.Laya.timer)this.Laya.timer.clearAll(this);if(this.node){this.node.removeSelf();this.node.active=true;this.node.visible=true;this.node.alpha=1;this.node.pos(0,0);this.node.rotation=0;}if(this.registry)this.registry.untrack(this);this.enabled=false;this.pendingFade=null;this.owner=null;this.component=null;this.node=null;}
  reset(){this.clear();this.paused=false;this.config=null;}
}
module.exports=Trail2DAdapter;
