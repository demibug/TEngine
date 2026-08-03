'use strict';
class EffectHandle {
  constructor({ownerId=null,persistent=false,disposeOnTimelineEnd=false,update=null,dispose=null,onOwnerAttack=null,metadata=null}={}){
    this.ownerId=ownerId;this.persistent=persistent;this.disposeOnTimelineEnd=disposeOnTimelineEnd;
    this._update=update;this._dispose=dispose;this._onOwnerAttack=onOwnerAttack;this.metadata=metadata;this.disposed=false;this.disposeReason=null;
  }
  update(dt){if(!this.disposed&&this._update)this._update(dt,this);}
  onOwnerAttack(context={}){if(!this.disposed&&this._onOwnerAttack)this._onOwnerAttack(context,this);}
  dispose(reason='dispose'){if(this.disposed)return false;this.disposed=true;this.disposeReason=reason;if(this._dispose)this._dispose(reason);return true;}
}
module.exports={EffectHandle};
