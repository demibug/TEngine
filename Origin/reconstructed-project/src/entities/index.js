'use strict';
module.exports = {
  ...require('./EnemyEventProxy'),
  ...require('./EnemyBase'),
  ...require('./NormalEnemyBase'),
  ...require('./Mob0Enemy'),
  ...require('./BattleTarget'),
};

module.exports = { ...module.exports, ...require('./types') };
