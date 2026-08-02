'use strict';
class UnitLevelService {
  constructor({ maxLevel = 5 }={}){this.maxLevel=maxLevel;}
  canUpgrade(unit,delta=1){return unit && !unit.mergeDisabled && unit.level + delta <= this.maxLevel;}
  upgrade(unit,delta=1){if(!this.canUpgrade(unit,delta))return {success:false,reason:unit&&unit.mergeDisabled?'单位被禁止合成':'已达到最高等级'};const applied=unit.levelUp(delta,true);return {success:applied>0,applied,level:unit.level};}
}
module.exports={UnitLevelService};
