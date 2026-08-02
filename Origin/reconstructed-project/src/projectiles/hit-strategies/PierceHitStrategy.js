'use strict';
const {HitEnemyStrategy}=require('../HitEnemyStrategy');
class PierceHitStrategy extends HitEnemyStrategy { constructor(){super();this.typeCode=101;} reset(o={}){return super.reset({...o,removeAfterHit:false,triggerMode:o.triggerMode||'hitEnable'});} }
module.exports={PierceHitStrategy};
