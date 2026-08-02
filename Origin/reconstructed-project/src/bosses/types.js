'use strict';
const { BossBase }=require('./BossBase');
const { getBossDefinition }=require('./BossDefinitions');
class ZhangLiangBoss extends BossBase{constructor(){super(getBossDefinition('ZhangLiang'));}}
class ZhangBaoBoss extends BossBase{constructor(){super(getBossDefinition('ZhangBao'));}}
class ZhangJiaoBoss extends BossBase{constructor(){super(getBossDefinition('ZhangJiao'));}}
class SunShangXiangBoss extends BossBase{constructor(){super(getBossDefinition('SunShangXiang'));}}
class ZhenFuBoss extends BossBase{constructor(){super(getBossDefinition('ZhenFu'));}}
class DiaoChanBoss extends BossBase{constructor(){super(getBossDefinition('DiaoChan'));}}
class HuaXiongBoss extends BossBase{constructor(){super(getBossDefinition('HuaXiong'));}}
class LvBuBoss extends BossBase{constructor(){super(getBossDefinition('LvBu'));}}
class DongZhuoBoss extends BossBase{constructor(){super(getBossDefinition('DongZhuo'));}}
class DianWeiBoss extends BossBase{constructor(){super(getBossDefinition('DianWei'));}}
class XiaHouDunBoss extends BossBase{constructor(){super(getBossDefinition('XiaHouDun'));}}
class CaoCaoBoss extends BossBase{constructor(){super(getBossDefinition('CaoCao'));}}
module.exports={ZhangLiangBoss,ZhangBaoBoss,ZhangJiaoBoss,SunShangXiangBoss,ZhenFuBoss,DiaoChanBoss,HuaXiongBoss,LvBuBoss,DongZhuoBoss,DianWeiBoss,XiaHouDunBoss,CaoCaoBoss};
