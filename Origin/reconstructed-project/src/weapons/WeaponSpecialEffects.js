'use strict';

const { WeaponAttackEffect } = require('./WeaponAttackEffect');
const { BuffType } = require('../buffs/BuffTypes');

/**
 * 武器特殊效果工厂（提案 ④ special-weapons-projectiles / P1-01）。
 *
 * 24 把特殊非弓武器的专属 effect 逻辑在此承载，Weapon.attack 按 this.special
 * 分派到 applySpecial。效果数值对齐 bundle weaponDesc 取证（work/bundle.strings-decoded.js）。
 *
 * 数值标注约定：
 * - bundle 明示的数值直接采用；
 * - bundle 未明示的（青龙偃月刀刀气倍率、君子小人剑技能伤害）以可注入常量承载并标注 PARTIAL；
 * - 弹种专属逻辑 bundle 未取证的标注 DEFERRED。
 */

// PARTIAL: bundle:38732 青龙偃月刀「数团刀气」未明示倍率，以可注入默认值承载
const DRAGON_BLADE_QI_MULTIPLIER = 1.5;
// PARTIAL: bundle:1235 君子小人剑「君子/小人技能」未明示伤害数值，以可注入默认值承载
const GENTLEMAN_VILLAIN_DAMAGE_MULTIPLIER = 2;
const GENTLEMAN_VILLAIN_INTERVAL = 10;

/**
 * 对武器特殊效果进行结算。
 * @param {object} weapon - Weapon 实例（含 definition、owner、randomSource、attackCount）
 * @param {object} context - 规范化攻击上下文（target/targets/damage/enemyManager 等）
 * @returns {object|null} 结算结果对象，null 表示该 special 不触发本次攻击（回退通用攻击）
 */
function applySpecial(weapon, context) {
  const def = weapon.definition || {};
  const special = def.special;
  if (!special) return null;
  const handler = HANDLERS[special];
  if (!handler) return null;
  return handler(weapon, context, def);
}

function ownerOf(weapon) {
  return weapon.owner || {};
}

function combatCenter(owner) {
  return owner.combatCenter || { x: Number(owner.x) || 0, y: Number(owner.y) || 0 };
}

function queryAreaEnemies(owner, center, radius) {
  const em = owner.enemyManager;
  if (!em || !center || !(radius > 0)) return [];
  const query = em.queryEnemyObjects || em.queryTargets;
  if (typeof query !== 'function') return [];
  const result = query.call(em, center.x, center.y, radius, owner.side, []);
  return Array.isArray(result) ? result.filter(Boolean) : [];
}

function hitTarget(target, damage, attacker) {
  if (!target) return false;
  if (typeof target.hit === 'function') return target.hit(damage, attacker);
  if (typeof target.takeDamage === 'function') return target.takeDamage(damage, attacker);
  return false;
}

function targetId(target) {
  return target && target.id != null ? target.id : null;
}

function applyBuffTo(buffManager, targetId, type, num, multiplicative, durationMs, source) {
  if (!buffManager || targetId == null) return -1;
  return buffManager.applyBuff(targetId, type, num, multiplicative, durationMs, { source });
}

// ---- 概率触发攻速类 ----

function tigerRoar(weapon, context, def) {
  if (weapon.randomSource() >= def.chance) return { triggered: false, reason: 'chance-miss' };
  const owner = ownerOf(weapon);
  const center = combatCenter(owner);
  const radius = Number(owner.attackRange || 96);
  const allies = queryAllies(owner, center, radius);
  const buffManager = weapon.buffManager || owner.buffManager;
  let applied = 0;
  for (const ally of allies) {
    if (applyBuffTo(buffManager, targetId(ally), BuffType.ATTACK_SPEED, def.attackSpeedBonus, true, def.durationMs, 'tigerRoar') >= 0) applied += 1;
  }
  return { attacked: true, triggered: true, attackType: 'tiger-roar', buffTargets: applied };
}

function wolfHowl(weapon, context, def) {
  if (weapon.randomSource() >= def.chance) return { triggered: false, reason: 'chance-miss' };
  const owner = ownerOf(weapon);
  const center = combatCenter(owner);
  const radius = Number(owner.attackRange || 96);
  const allies = queryAllies(owner, center, radius);
  const buffManager = weapon.buffManager || owner.buffManager;
  let applied = 0;
  for (const ally of allies) {
    if (applyBuffTo(buffManager, targetId(ally), BuffType.ATTACK_SPEED, def.attackSpeedBonus, true, def.durationMs, 'wolfHowl') >= 0) applied += 1;
  }
  return { attacked: true, triggered: true, attackType: 'wolf-howl', buffTargets: applied };
}

function stunChance(weapon, context, def) {
  const target = context.target || context.targets?.[0];
  if (!target) return { triggered: false, reason: 'no-target' };
  const baseDamage = Number(context.damage ?? ownerOf(weapon).attackDamage ?? 0);
  hitTarget(target, baseDamage, ownerOf(weapon));
  let stunned = false;
  if (weapon.randomSource() < def.chance) {
    const buffManager = weapon.buffManager || ownerOf(weapon).buffManager;
    // STUN 定时：layer.time 为毫秒，BuffManager.update 驱动到期移除
    const STUN_DURATION = 1000; // PARTIAL: bundle:44248 未明示眩晕时长，以默认 1000ms 承载
    if (applyBuffTo(buffManager, targetId(target), BuffType.STUN, 1, false, STUN_DURATION, 'stunChance') >= 0) stunned = true;
  }
  return { attacked: true, triggered: stunned, attackType: 'stun-chance', stunned, hits: [{ targetId: targetId(target), damage: baseDamage }] };
}

// ---- 首击触发类 ----

function isFirstHitOnTarget(weapon, target) {
  const id = targetId(target);
  if (id == null) return true;
  if (!weapon._hitTargets) weapon._hitTargets = new Set();
  if (weapon._hitTargets.has(id)) return false;
  weapon._hitTargets.add(id);
  return true;
}

function goldenSpearArray(weapon, context, def) {
  const target = context.target || context.targets?.[0];
  if (!target) return { triggered: false, reason: 'no-target' };
  if (!isFirstHitOnTarget(weapon, target)) return { triggered: false, reason: 'not-first-hit' };
  if (weapon.randomSource() >= def.chance) return { triggered: false, reason: 'chance-miss' };
  const owner = ownerOf(weapon);
  const arrayCount = resolveExclusiveArrayCount(def, owner);
  const damage = Number(context.damage ?? owner.attackDamage ?? 0) * def.multiplier;
  const buffManager = weapon.buffManager || owner.buffManager;
  const enemies = context.targets && context.targets.length ? context.targets : [target];
  const hits = [];
  for (let i = 0; i < arrayCount; i += 1) {
    const victim = enemies[i % enemies.length] || target;
    hitTarget(victim, damage, owner);
    hits.push({ targetId: targetId(victim), damage });
    applyBuffTo(buffManager, targetId(victim), BuffType.STUN, 1, false, def.stunMs, 'goldenSpearArray');
  }
  return { attacked: true, triggered: true, attackType: 'golden-spear-array', arrays: arrayCount, multiplier: def.multiplier, hits };
}

function ironSpearArray(weapon, context, def) {
  const target = context.target || context.targets?.[0];
  if (!target) return { triggered: false, reason: 'no-target' };
  if (!isFirstHitOnTarget(weapon, target)) return { triggered: false, reason: 'not-first-hit' };
  if (weapon.randomSource() >= def.chance) return { triggered: false, reason: 'chance-miss' };
  const owner = ownerOf(weapon);
  // 取证偏差：铁枪 bundle:43066 为 1 个枪阵（非虎头湛金枪的 3 个）
  const damage = Number(context.damage ?? owner.attackDamage ?? 0) * def.multiplier;
  const buffManager = weapon.buffManager || owner.buffManager;
  hitTarget(target, damage, owner);
  applyBuffTo(buffManager, targetId(target), BuffType.STUN, 1, false, def.stunMs, 'ironSpearArray');
  return { attacked: true, triggered: true, attackType: 'iron-spear-array', arrays: def.arrays, multiplier: def.multiplier, hits: [{ targetId: targetId(target), damage }] };
}

function hookFall(weapon, context, def) {
  const target = context.target || context.targets?.[0];
  if (!target) return { triggered: false, reason: 'no-target' };
  if (!isFirstHitOnTarget(weapon, target)) return { triggered: false, reason: 'not-first-hit' };
  if (weapon.randomSource() >= def.chance) return { triggered: false, reason: 'chance-miss' };
  const owner = ownerOf(weapon);
  const buffManager = weapon.buffManager || owner.buffManager;
  applyBuffTo(buffManager, targetId(target), BuffType.KNOCKDOWN, 1, false, def.durationMs, 'hookFall');
  const baseDamage = Number(context.damage ?? owner.attackDamage ?? 0);
  hitTarget(target, baseDamage, owner);
  return { attacked: true, triggered: true, attackType: 'hook-fall', hits: [{ targetId: targetId(target), damage: baseDamage }] };
}

function ancientGold(weapon, context, def) {
  const target = context.target || context.targets?.[0];
  if (!target) return { triggered: false, reason: 'no-target' };
  isFirstHitOnTarget(weapon, target); // 记录首击
  const owner = ownerOf(weapon);
  const baseDamage = Number(context.damage ?? owner.attackDamage ?? 0);
  hitTarget(target, baseDamage, owner);
  let goldAwarded = 0;
  const economy = owner.battleEconomy || owner.economy;
  if (economy && typeof economy.award === 'function') {
    economy.award(owner.side, def.gold, 'weapon');
    goldAwarded = def.gold;
  }
  // DEFERRED: 若 owner 无 economy 接口，金币逻辑不阻塞，仅标注未结算
  return { attacked: true, triggered: goldAwarded > 0, attackType: 'ancient-gold', gold: goldAwarded, hits: [{ targetId: targetId(target), damage: baseDamage }] };
}

// ---- 计数触发类 ----

function tripleBlade(weapon, context, def) {
  const target = context.target || context.targets?.[0];
  const owner = ownerOf(weapon);
  const baseDamage = Number(context.damage ?? owner.attackDamage ?? 0);
  const isTrigger = weapon.attackCount > 0 && weapon.attackCount % def.interval === 0;
  if (!isTrigger) {
    hitTarget(target, baseDamage, owner);
    return { attacked: true, triggered: false, attackType: 'triple-blade', hits: [{ targetId: targetId(target), damage: baseDamage }] };
  }
  // 群体 2 倍刀气
  const center = combatCenter(owner);
  const radius = Number(owner.attackRange || 96);
  const enemies = context.targets && context.targets.length ? context.targets : queryAreaEnemies(owner, center, radius);
  const damage = baseDamage * def.multiplier;
  const hits = [];
  for (const enemy of enemies) {
    hitTarget(enemy, damage, owner);
    hits.push({ targetId: targetId(enemy), damage });
  }
  return { attacked: true, triggered: true, attackType: 'triple-blade-qi', multiplier: def.multiplier, hits };
}

function ironKnifeSpeed(weapon, context, def) {
  const target = context.target || context.targets?.[0];
  const owner = ownerOf(weapon);
  const baseDamage = Number(context.damage ?? owner.attackDamage ?? 0);
  hitTarget(target, baseDamage, owner);
  const buffManager = weapon.buffManager || owner.buffManager;
  // 同目标每次攻速 +5%，切换目标重置
  const newTargetId = targetId(target);
  if (weapon._lastIronKnifeTarget !== newTargetId) {
    weapon._lastIronKnifeTarget = newTargetId;
    weapon._ironKnifeStacks = 0;
  }
  weapon._ironKnifeStacks = (weapon._ironKnifeStacks || 0) + 1;
  const totalBonus = def.perHitBonus * weapon._ironKnifeStacks;
  applyBuffTo(buffManager, newTargetId, BuffType.ATTACK_SPEED, totalBonus, true, -1, 'ironKnifeSpeed');
  return { attacked: true, triggered: true, attackType: 'iron-knife-speed', stacks: weapon._ironKnifeStacks, bonus: totalBonus, hits: [{ targetId: newTargetId, damage: baseDamage }] };
}

function gentlemanVillain(weapon, context, def) {
  const target = context.target || context.targets?.[0];
  const owner = ownerOf(weapon);
  const baseDamage = Number(context.damage ?? owner.attackDamage ?? 0);
  const isTrigger = weapon.attackCount > 0 && weapon.attackCount % GENTLEMAN_VILLAIN_INTERVAL === 0;
  if (!isTrigger) {
    hitTarget(target, baseDamage, owner);
    return { attacked: true, triggered: false, attackType: 'gentleman-villain', hits: [{ targetId: targetId(target), damage: baseDamage }] };
  }
  // 君子/小人各 50%；刘备限君子，曹操限小人
  const generalName = owner.generalName || owner.general || null;
  let branch;
  if (generalName === '刘备') branch = 'gentleman';
  else if (generalName === '曹操') branch = 'villain';
  else branch = weapon.randomSource() < 0.5 ? 'gentleman' : 'villain';
  // PARTIAL: 君子/小人技能伤害数值 bundle:1235 未明示，以倍率承载
  const damage = baseDamage * GENTLEMAN_VILLAIN_DAMAGE_MULTIPLIER;
  hitTarget(target, damage, owner);
  return { attacked: true, triggered: true, attackType: `gentleman-villain-${branch}`, branch, multiplier: GENTLEMAN_VILLAIN_DAMAGE_MULTIPLIER, hits: [{ targetId: targetId(target), damage }] };
}

// ---- 击杀触发类 ----

function pearBlossom(weapon, context, def) {
  const target = context.target || context.targets?.[0];
  const owner = ownerOf(weapon);
  const baseDamage = Number(context.damage ?? owner.attackDamage ?? 0);
  hitTarget(target, baseDamage, owner);
  // 击杀触发：判定 target 是否死亡
  const killed = isTargetKilled(target);
  if (!killed) return { attacked: true, triggered: false, attackType: 'pear-blossom', hits: [{ targetId: targetId(target), damage: baseDamage }] };
  // 飞出 8 朵梨花随机打击 8 个敌人（可重复）
  const center = combatCenter(owner);
  const radius = Number(owner.attackRange || 96);
  const enemies = queryAreaEnemies(owner, center, radius * 2);
  const petalDamage = baseDamage;
  const hits = [{ targetId: targetId(target), damage: baseDamage }];
  for (let i = 0; i < def.petals; i += 1) {
    if (!enemies.length) break;
    const victim = enemies[Math.floor(weapon.randomSource() * enemies.length) % enemies.length];
    hitTarget(victim, petalDamage, owner);
    hits.push({ targetId: targetId(victim), damage: petalDamage, petal: true });
  }
  return { attacked: true, triggered: true, attackType: 'pear-blossom', petals: def.petals, hits };
}

function dragonBladeQi(weapon, context, def) {
  const target = context.target || context.targets?.[0];
  const owner = ownerOf(weapon);
  const baseDamage = Number(context.damage ?? owner.attackDamage ?? 0);
  hitTarget(target, baseDamage, owner);
  const killed = isTargetKilled(target);
  if (!killed) return { attacked: true, triggered: false, attackType: 'dragon-blade-qi', hits: [{ targetId: targetId(target), damage: baseDamage }] };
  // 斩杀释放刀气无差别全体，倍率 PARTIAL
  const center = combatCenter(owner);
  const radius = Number(owner.attackRange || 96);
  const enemies = queryAreaEnemies(owner, center, radius * 3);
  const qiDamage = baseDamage * DRAGON_BLADE_QI_MULTIPLIER;
  const hits = [{ targetId: targetId(target), damage: baseDamage }];
  for (const enemy of enemies) {
    hitTarget(enemy, qiDamage, owner);
    hits.push({ targetId: targetId(enemy), damage: qiDamage, bladeQi: true });
  }
  return { attacked: true, triggered: true, attackType: 'dragon-blade-qi', multiplier: DRAGON_BLADE_QI_MULTIPLIER, hits };
}

function steelTipSpeed(weapon, context, def) {
  const target = context.target || context.targets?.[0];
  const owner = ownerOf(weapon);
  const baseDamage = Number(context.damage ?? owner.attackDamage ?? 0);
  hitTarget(target, baseDamage, owner);
  const killed = isTargetKilled(target);
  if (!killed) return { attacked: true, triggered: false, attackType: 'steel-tip-speed', hits: [{ targetId: targetId(target), damage: baseDamage }] };
  const buffManager = weapon.buffManager || owner.buffManager;
  applyBuffTo(buffManager, targetId(owner), BuffType.ATTACK_SPEED, def.attackSpeedBonus, true, def.durationMs, 'steelTipSpeed');
  return { attacked: true, triggered: true, attackType: 'steel-tip-speed', attackSpeedBonus: def.attackSpeedBonus, hits: [{ targetId: targetId(target), damage: baseDamage }] };
}

// ---- 等级/概率类 ----

function skyHalberd(weapon, context, def) {
  const target = context.target || context.targets?.[0];
  const owner = ownerOf(weapon);
  const baseDamage = Number(context.damage ?? owner.attackDamage ?? 0);
  const level = Math.max(1, Math.min(5, Number(owner.level || 1)));
  const chance = def.levelChances[level - 1];
  const triggered = weapon.randomSource() < chance;
  if (!triggered) {
    hitTarget(target, baseDamage, owner);
    return { attacked: true, triggered: false, attackType: 'sky-halberd', hits: [{ targetId: targetId(target), damage: baseDamage }] };
  }
  // 挑起 5 倍伤害 + 瞬杀血量<20%
  const multiplier = def.multiplier;
  const hpRatio = targetHpRatio(target);
  let damage;
  let instantKill = false;
  if (hpRatio > 0 && hpRatio < def.instantKillThreshold) {
    damage = targetMaxHp(target) || baseDamage * multiplier; // 瞬杀：清空 HP
    instantKill = true;
  } else {
    damage = baseDamage * multiplier;
  }
  hitTarget(target, damage, owner);
  const buffManager = weapon.buffManager || owner.buffManager;
  applyBuffTo(buffManager, targetId(target), BuffType.KNOCKDOWN, 1, false, 500, 'skyHalberd');
  return { attacked: true, triggered: true, attackType: 'sky-halberd', multiplier, instantKill, level, hits: [{ targetId: targetId(target), damage }] };
}

function dragonSpearFly(weapon, context, def) {
  const owner = ownerOf(weapon);
  const generalName = owner.generalName || owner.general || null;
  const chance = generalName === def.exclusiveGeneral ? def.exclusiveChance : def.chance;
  if (weapon.randomSource() >= chance) return { triggered: false, reason: 'chance-miss' };
  // 飞枪对所有敌人 5 倍伤害
  const center = combatCenter(owner);
  const radius = Number(owner.attackRange || 96);
  const enemies = context.targets && context.targets.length ? context.targets : queryAreaEnemies(owner, center, radius * 3);
  const baseDamage = Number(context.damage ?? owner.attackDamage ?? 0);
  const damage = baseDamage * def.multiplier;
  const hits = [];
  for (const enemy of enemies) {
    hitTarget(enemy, damage, owner);
    hits.push({ targetId: targetId(enemy), damage, flySpear: true });
  }
  return { attacked: true, triggered: true, attackType: 'dragon-spear-fly', multiplier: def.multiplier, exclusive: generalName === def.exclusiveGeneral, hits };
}

function snakeSpear(weapon, context, def) {
  const owner = ownerOf(weapon);
  const level = Math.max(1, Number(owner.level || 1));
  const snakeCount = def.baseSnakes + (level - 1) * def.perLevel;
  // 灵蛇拦路：登记灵蛇计数，伤害经灵蛇实体结算（实体层 DEFERRED 至提案 ④b 弹种连接）
  const target = context.target || context.targets?.[0];
  const baseDamage = Number(context.damage ?? owner.attackDamage ?? 0);
  if (target) hitTarget(target, baseDamage, owner);
  // DEFERRED: 灵蛇实体（PikeSnakeBullet 弹种）连接在 ④b 任务 8.x；此处仅登记计数与基础伤害
  return { attacked: true, triggered: true, attackType: 'snake-spear', snakeCount, hits: target ? [{ targetId: targetId(target), damage: baseDamage }] : [] };
}

// ---- 辅助 ----

function queryAllies(owner, center, radius) {
  const reg = owner.unitRegistry || owner.allyRegistry;
  if (!reg || typeof reg.queryAllies !== 'function') return owner ? [owner] : [];
  const result = reg.queryAllies(center.x, center.y, radius, owner.side);
  return Array.isArray(result) ? result.filter(Boolean) : [owner];
}

function resolveExclusiveArrayCount(def, owner) {
  const generalName = owner.generalName || owner.general || null;
  if (def.exclusiveGeneral && generalName === def.exclusiveGeneral && def.exclusiveArrays) return def.exclusiveArrays;
  return def.arrays;
}

function isTargetKilled(target) {
  if (!target) return false;
  if (typeof target.isDead === 'function') return target.isDead();
  if (target.currentState === 4) return true; // DEAD
  if (typeof target.hp === 'number' && target.hp <= 0) return true;
  return false;
}

function targetHpRatio(target) {
  if (!target) return 1;
  const hp = Number(target.hp);
  const maxHp = Number(target.maxHp || target.maxHP);
  if (!Number.isFinite(hp) || !Number.isFinite(maxHp) || maxHp <= 0) return 1;
  return hp / maxHp;
}

function targetMaxHp(target) {
  if (!target) return 0;
  return Number(target.maxHp || target.maxHP) || 0;
}

const HANDLERS = {
  tigerRoar,
  wolfHowl,
  stunChance,
  goldenSpearArray,
  ironSpearArray,
  hookFall,
  ancientGold,
  tripleBlade,
  ironKnifeSpeed,
  gentlemanVillain,
  pearBlossom,
  dragonBladeQi,
  steelTipSpeed,
  skyHalberd,
  dragonSpearFly,
  snakeSpear,
};

module.exports = { applySpecial, DRAGON_BLADE_QI_MULTIPLIER, GENTLEMAN_VILLAIN_DAMAGE_MULTIPLIER, GENTLEMAN_VILLAIN_INTERVAL };
