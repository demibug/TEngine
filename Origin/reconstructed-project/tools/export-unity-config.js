#!/usr/bin/env node
'use strict';
const fs=require('node:fs');const path=require('node:path');
const root=path.resolve(__dirname,'..'),out=path.join(root,'unity-export/config');fs.mkdirSync(out,{recursive:true});
const write=(name,data)=>fs.writeFileSync(path.join(out,name),JSON.stringify(data,null,2));
const {BASE_SOLDIER_CONFIGS,DAMAGE_LEVEL_MULTIPLIERS,ATTACK_SPEED_LEVEL_MULTIPLIERS,MAX_SOLDIER_LEVEL}=require('../src/units/UnitConfig');
const {GENERAL_DEFINITIONS}=require('../src/generals/GeneralDefinitions');
const {BOSS_DEFINITIONS}=require('../src/bosses/BossDefinitions');
const {BuffDefinitions}=require('../src/buffs/BuffDefinitions');
const {SKILL_DEFINITIONS}=require('../src/skills/SkillDefinitions');
const {EnemyDataCore,MapDataCore}=require('../src/data/BattleDataCore');
const {DeckDefinitions}=require('../src/deck/DeckDefinitions');
const {GameEvents}=require('../src/core/EventBus');
const readCatalog=name=>{const p=path.join(root,'analysis/catalogs',name);return fs.existsSync(p)?JSON.parse(fs.readFileSync(p,'utf8')):null;};
// maxLevel=3 对齐 bundle Dp 表长度；倍率表保留 5 元素作快照，4-5 级因 maxLevel=3 不可达。
write('units.json',{status:'CORE_COMPLETE',maxLevel:MAX_SOLDIER_LEVEL,damageLevelMultipliers:DAMAGE_LEVEL_MULTIPLIERS,attackSpeedLevelMultipliers:ATTACK_SPEED_LEVEL_MULTIPLIERS,units:BASE_SOLDIER_CONFIGS});
write('generals.json',{status:'PARTIAL_CORE_CONFIG',gap:'glyph merge and formal general battle component not fully reconstructed',generals:GENERAL_DEFINITIONS});
write('enemies.json',readCatalog('enemy-registry.json'));
write('bosses.json',{status:'CORE_COMPLETE_NO_PRESENTATION',bosses:BOSS_DEFINITIONS});
write('weapons.json',readCatalog('weapon-registry.json'));
write('projectiles.json',readCatalog('projectile-registry.json'));
write('buffs.json',{status:'CORE_COMPLETE',buffs:Array.from(BuffDefinitions.values())});
write('skills.json',{status:'CORE_COMPLETE_NO_PRESENTATION',skills:SKILL_DEFINITIONS});
const enemy=new EnemyDataCore();write('waves.json',{waveUnitCounts:enemy.waveUnitCounts,bossWaveNumbers:enemy.bossWaveNumbers,bossSpawnChances:enemy.bossSpawnChances,spawnStrategyWeights:enemy.spawnStrategyWeights,spawnStrategies:enemy.spawnStrategies});
const map=new MapDataCore();map.initialize(0);write('maps.json',{gridWidth:map.gridWidth,gridHeight:map.gridHeight,width:map.width,height:map.height,mapIndex:map.mapIndex,blocks:map.blocks||null,playerPath:map.pathForSide(true),opponentPath:map.pathForSide(false)});
write('battle-economy.json',{initialGold:20,refreshCostStart:10,refreshCostIncrement:2,unitBaseCost:DeckDefinitions.baseUnitCost,handSize:DeckDefinitions.handSize});
write('events.json',GameEvents);
write('battle-result-schema.json',{isWin:'boolean',star:'number',gold:'number',battleDuration:'milliseconds',round:'number',playerTargetHealth:'number',opponentTargetHealth:'number',weaponFragments:'array',killCount:'number',bossKillCount:'number',endlessRound:'number',gameMode:'normal|endless',resultState:'WIN|LOSE'});
process.stdout.write(JSON.stringify({output:out,files:fs.readdirSync(out).sort()},null,2)+'\n');
