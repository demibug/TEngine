'use strict';
const BOSS_DEFINITIONS=Object.freeze([
 {key:'ZhangLiang',name:'张梁',originalSymbol:'nj',sourceRange:'32468-32631',skillKey:'SoulCapture',animationKey:'boss0',resourcePath:'resources/anim/boss0/skeleton.json',attackAnimation:'attackliang',idleAnimation:'goliang',timeline:{effectAtMs:500,completeAtMs:1400}},
 {key:'ZhangBao',name:'张宝',originalSymbol:'oO',sourceRange:'32632-32697',skillKey:'SoulSummon',animationKey:'boss0',resourcePath:'resources/anim/boss0/skeleton.json',attackAnimation:'attackbao',idleAnimation:'gobao',timeline:{effectAtMs:0,completeAtMs:1000}},
 {key:'ZhangJiao',name:'张角',originalSymbol:'tj',sourceRange:'31120-31187',skillKey:'Inspire',animationKey:'boss0',resourcePath:'resources/anim/boss0/skeleton.json',attackAnimation:'attackjiao',idleAnimation:'gojiao',timeline:{effectAtMs:500,completeAtMs:1000}},
 {key:'SunShangXiang',name:'孙尚香',originalSymbol:'oP',sourceRange:'32875-32938',skillKey:'Demolition',animationKey:'boss1',resourcePath:'resources/anim/boss1/skeleton.json',attackAnimation:'attackxiang',idleAnimation:'goxiang',timeline:{effectAtMs:500,completeAtMs:1000}},
 {key:'ZhenFu',name:'甄宓',originalSymbol:'rT',sourceRange:'31188-31251',skillKey:'RainStorm',animationKey:'boss1',resourcePath:'resources/anim/boss1/skeleton.json',attackAnimation:'attackzhen',idleAnimation:'gozhen',timeline:{effectAtMs:900,completeAtMs:1000}},
 {key:'DiaoChan',name:'貂蝉',originalSymbol:'sR',sourceRange:'32264-32389',skillKey:'Enthrall',animationKey:'boss1',resourcePath:'resources/anim/boss1/skeleton.json',attackAnimation:'attackdiao',idleAnimation:'godiao',timeline:{effectAtMs:200,completeAtMs:1200}},
 {key:'HuaXiong',name:'华雄',originalSymbol:'tz',sourceRange:'32753-32804',skillKey:'CavalryOrder',animationKey:'huaXiong',resourcePath:'resources/anim/huaXiong/skeleton.json',attackAnimation:'attackhx',idleAnimation:'gohx',timeline:{effectAtMs:500,completeAtMs:1000}},
 {key:'LvBu',name:'吕布',originalSymbol:'nJ',sourceRange:'31649-31712',skillKey:'FangTianHalberd',animationKey:'lvBu',resourcePath:'resources/anim/lvBu/skeleton.json',attackAnimation:'attacklvbu',idleAnimation:'golvbu',timeline:{effectAtMs:650,completeAtMs:1000}},
 {key:'DongZhuo',name:'董卓',originalSymbol:'oA',sourceRange:'32144-32257',skillKey:'Devour',animationKey:'dongZhuo',resourcePath:'resources/anim/dongZhuo/skeleton.json',attackAnimation:'attackdz',followupAnimation:'attack2dz',idleAnimation:'godz',timeline:{effectAtMs:500,completeAtMs:1400}},
 {key:'DianWei',name:'典韦',originalSymbol:'rv',sourceRange:'31485-31648',skillKey:'Madness',animationKey:'boss2',resourcePath:'resources/anim/boss2/skeleton.json',attackAnimation:'attackdian',idleAnimation:'godian',timeline:{effectAtMs:800,completeAtMs:1400}},
 {key:'XiaHouDun',name:'夏侯惇',originalSymbol:'q5',sourceRange:'31713-31783',skillKey:'DevourEyes',animationKey:'boss2',resourcePath:'resources/anim/boss2/skeleton.json',attackAnimation:'attackdun',idleAnimation:'goxia',timeline:{effectAtMs:1000,completeAtMs:1500}},
 {key:'CaoCao',name:'曹操',originalSymbol:'vg',sourceRange:'31001-31119',skillKey:'WarlordSeal',animationKey:'boss2',resourcePath:'resources/anim/boss2/skeleton.json',attackAnimation:'attackcao',idleAnimation:'gocao',timeline:{effectAtMs:900,completeAtMs:1000}},
]);
const byKey=new Map(BOSS_DEFINITIONS.map((d,i)=>[d.key,Object.freeze({...d,typeIndex:i})]));
function getBossDefinition(key){const d=byKey.get(key);if(!d)throw new Error(`Unknown boss type: ${key}`);return d;}
module.exports={BOSS_DEFINITIONS,getBossDefinition};
