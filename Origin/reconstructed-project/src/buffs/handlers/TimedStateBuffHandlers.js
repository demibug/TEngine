'use strict';
const { StateBuffHandler } = require('../StateBuffHandler');

class StunBuffHandler extends StateBuffHandler { label(){return '晕眩';} }
class ElectrocuteBuffHandler extends StateBuffHandler { label(){return '电击';} }
class ChaosBuffHandler extends StateBuffHandler { label(){return '混乱';} }
class LockBuffHandler extends StateBuffHandler { label(){return '封锁';} }
class FallBuffHandler extends StateBuffHandler { constructor(){ super({ mergeLayers:true, replaceDuration:true }); } label(){return '跌倒';} }
class PierceBuffHandler extends StateBuffHandler { label(){return '穿刺';} }
class KnockdownBuffHandler extends StateBuffHandler { constructor(){ super({ mergeLayers:true, replaceDuration:true }); } label(){return '跌倒';} }
class SuppressionBuffHandler extends StateBuffHandler { label(){return '压制';} }
class CharmBuffHandler extends StateBuffHandler { label(){return '魅惑';} }
class LimitBuffHandler extends StateBuffHandler { label(){return '';} }

module.exports = {
  StunBuffHandler,
  ElectrocuteBuffHandler,
  ChaosBuffHandler,
  LockBuffHandler,
  FallBuffHandler,
  PierceBuffHandler,
  KnockdownBuffHandler,
  SuppressionBuffHandler,
  CharmBuffHandler,
  LimitBuffHandler,
};
