'use strict';
const {HitEnemyStrategy}=require('../HitEnemyStrategy');
class AreaHitStrategy extends HitEnemyStrategy { constructor(){super();this.typeCode=102;this.radius=0;} reset(o={}){this.radius=o.radius||0;return super.reset({...o,removeAfterHit:true,triggerMode:o.triggerMode||'both'});} recover(){this.radius=0;super.recover();}}
module.exports={AreaHitStrategy};
