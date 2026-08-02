'use strict';
const { ORIGIN_TRAIL_CATALOG }=require('./OriginTrailCatalog');
class Trail2DRegistry {
  constructor(){this.definitions=new Map(Object.entries(ORIGIN_TRAIL_CATALOG));this.active=new Set();this.pendingFade=new Set();}
  register(key,config){if(this.definitions.has(key))throw new Error(`Duplicate trail key: ${key}`);this.definitions.set(key,Object.freeze({...config}));return this;}
  get(key){const v=this.definitions.get(key);if(!v)throw new Error(`Unknown trail key: ${key}`);return v;}
  track(adapter){if(adapter)this.active.add(adapter);return adapter;}
  untrack(adapter){this.active.delete(adapter);this.pendingFade.delete(adapter);}
  markPending(adapter){this.pendingFade.add(adapter);}
  clearAllDeferredTrails(){for(const adapter of [...this.pendingFade])adapter.clear();this.pendingFade.clear();for(const adapter of [...this.active])adapter.clear();this.active.clear();}
  keys(){return [...this.definitions.keys()];}
}
module.exports=Trail2DRegistry;
