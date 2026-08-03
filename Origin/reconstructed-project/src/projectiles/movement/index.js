module.exports = {
  ...require('./LineMovement'),
  ...require('./FixedTargetMovement'),
  ...require('./BezierMovement'),
  // 提案 ④b 新增 7 移动策略（覆盖 bundle 注册的 8 具体策略 + 基类）
  ...require('./DefaultBulletMovement'),
  ...require('./TargetObjectInstantaneous'),
  ...require('./TargetDirectionWaveMovement'),
  ...require('./TargetDirectionLineMovement'),
  ...require('./ForwardMovement'),
  ...require('./TargetPositionBezierMovement'),
  ...require('./TargetEnemyMovement'),
};
