'use strict';
const { SPINE_CATALOG } = require('../../resources/SpineCatalog');
const { PREFAB_CATALOG } = require('../../resources/PrefabCatalog');
const { hasImage } = require('../../resources/ImageCatalog');

const BOSS_RESOURCE_MANIFEST = Object.freeze({
  ZhangLiang: { skeleton:'resources/anim/boss0/skeleton.json', animationKey:'boss0', idle:'goliang', attack:'attackliang', audio:['boss_sweep_skill'], source:'32468-32631' },
  ZhangBao: { skeleton:'resources/anim/boss0/skeleton.json', animationKey:'boss0', idle:'gobao', attack:'attackbao', audio:[], source:'32632-32697' },
  ZhangJiao: { skeleton:'resources/anim/boss0/skeleton.json', animationKey:'boss0', idle:'gojiao', attack:'attackjiao', audio:['zhangJiao_skill_horn'], source:'31120-31187' },
  SunShangXiang: { skeleton:'resources/anim/boss1/skeleton.json', animationKey:'boss1', idle:'goxiang', attack:'attackxiang', audio:[], source:'32875-32938' },
  ZhenFu: { skeleton:'resources/anim/boss1/skeleton.json', animationKey:'boss1', idle:'gozhen', attack:'attackzhen', audio:['zhenFu_skill_rain','zhenFu_skill_rain_cycle'], source:'31188-31251' },
  DiaoChan: { skeleton:'resources/anim/boss1/skeleton.json', animationKey:'boss1', idle:'godiao', attack:'attackdiao', audio:['diaoChan_skill_charm'], source:'32264-32389' },
  HuaXiong: { skeleton:'resources/anim/huaXiong/skeleton.json', animationKey:'huaXiong', idle:'gohx', attack:'attackhx', audio:['summon_cavalry_skill'], source:'32753-32804' },
  LvBu: { skeleton:'resources/anim/lvBu/skeleton.json', animationKey:'lvBu', idle:'golvbu', attack:'attacklvbu', audio:['boss_sweep_skill','luBu_skill'], source:'31649-31712' },
  DongZhuo: { skeleton:'resources/anim/dongZhuo/skeleton.json', animationKey:'dongZhuo', idle:'godz', attack:'attackdz', followup:'attack2dz', audio:['dongZhuo_skill_phase1_suck','dongZhuo_skill_phantom'], source:'32144-32257' },
  DianWei: { skeleton:'resources/anim/boss2/skeleton.json', animationKey:'boss2', idle:'godian', attack:'attackdian', audio:[], source:'31485-31648' },
  XiaHouDun: { skeleton:'resources/anim/boss2/skeleton.json', animationKey:'boss2', idle:'goxia', attack:'attackdun', audio:['xiahouDun_skill_cloud','xiahouDun_skill_lightning'], source:'31713-31783' },
  CaoCao: { skeleton:'resources/anim/boss2/skeleton.json', animationKey:'boss2', idle:'gocao', attack:'attackcao', audio:['caoCao_skill_seal','chain_lock'], source:'31001-31119' },
});

for (const [bossKey, config] of Object.entries(BOSS_RESOURCE_MANIFEST)) {
  const catalog = SPINE_CATALOG[config.animationKey];
  config.resourceAvailable = Boolean(catalog);
  config.animationNamesVerified = Boolean(catalog && catalog.animations.includes(config.idle) && catalog.animations.includes(config.attack));
  config.presentationStatus = config.animationNamesVerified ? 'AVAILABLE_IN_ORIGIN_PROJECT' : 'PARTIAL_WITH_EXACT_GAPS';
}

const SKILL_VFX_MANIFEST = Object.freeze({
  SoulSummon: { key:'soul-summon', expected:'resources/img/gameObject/enemy/soulHead.png', source:'31252-31484,32632-32697', kind:'image' },
  Demolition: { key:'demolition-tile', expected:'prefab/mapItem.lh', source:'32875-32938', kind:'prefab' },
  RainStorm: { key:'rain-overlay', expected:'resources/img/gameObject/enemy/rain.png', source:'31188-31251', kind:'image' },
  FangTianHalberd: { key:'level-down', expected:'prefab/lvlDownEff.lh', source:'31649-31712', kind:'prefab' },
  Devour: { key:'devour', expected:'resources/img/battleUI/eat1.png', alternate:'resources/img/battleUI/eat2.png', source:'32144-32257', kind:'image' },
  DevourEyes: { key:'darkness-overlay', expected:'resources/img/gameObject/enemy/blackCloud0.png', source:'31713-31783', kind:'image' },
});
for (const config of Object.values(SKILL_VFX_MANIFEST)) {
  config.resourceAvailable = config.kind === 'prefab'
    ? Object.values(PREFAB_CATALOG).some(entry => entry.path === config.expected)
    : hasImage(config.expected);
  config.presentationStatus = config.resourceAvailable ? 'AVAILABLE_IN_ORIGIN_PROJECT' : 'TODO_RESOURCE_MISSING';
}

function resourceRecord(feature, resourceType, formalKey, expectedPath, animationNames, sourceRanges, available=false) {
  return Object.freeze({ feature, resourceType, formalKey, expectedPath, animationNames:animationNames||[], sourceRanges:sourceRanges||[], logicStatus:'COMPLETE', presentationStatus:available?'AVAILABLE_IN_ORIGIN_PROJECT':'TODO_RESOURCE_MISSING' });
}
const resourceTodo = resourceRecord;
module.exports={BOSS_RESOURCE_MANIFEST,SKILL_VFX_MANIFEST,resourceRecord,resourceTodo};
