'use strict';

const { createDevelopmentConfig } = require('../config/DevelopmentConfig');
const { GameBootstrap } = require('./GameBootstrap');
const { registerCriticalPathClasses } = require('../core/ClassRegistry');
const { EventBus } = require('../core/EventBus');
const { GameLoop } = require('../core/GameLoop');
const { SceneManager } = require('../core/SceneManager');
const { MathRandom } = require('../core/MathRandom');
const { PlacementReservationRegistry } = require('../core/PlacementReservationRegistry');
const { AnimationEntityPool } = require('../core/AnimationEntityPool');
const { ObjectPool } = require('../core/ObjectPool');
const { GameDataCore } = require('../data/CriticalGameState');
const { EnemyFactory } = require('../battle/EnemyFactory');
const { EnemyManager } = require('../battle/EnemyManager');
const { UnitRegistry } = require('../battle/UnitRegistry');
const { UnitFactory } = require('../units/UnitFactory');
const { UnitLevelService } = require('../units/UnitLevelService');
const { UnitMergeService } = require('../units/UnitMergeService');
const { BattleEconomy } = require('../battle/BattleEconomy');
const { DeckManager } = require('../deck/DeckManager');
const { BattleInputController } = require('../input/BattleInputController');
const { BattleInputCommand, BattleInputCommandType } = require('../input/BattleInputCommand');
const { AIController } = require('../ai/AIController');
const WeaponManager = require('../weapons/WeaponManager');
const { KnifeAttackTimeline } = require('../combat/KnifeAttackTimeline');
const { DevelopmentAnimationDriver } = require('../combat/dev/DevelopmentAnimationDriver');
const { ProjectileFactory } = require('../projectiles/ProjectileFactory');
const { ProjectileManager } = require('../projectiles/ProjectileManager');
const { BuffManager } = require('../buffs/BuffManager');
const { SkillFactory, SkillManager, SkillEffectPort } = require('../skills');
const { SkillAudioRegistry, SkillVfxRegistry, DevelopmentSkillPresentation } = require('../skills/presentation');
const { DeadEntityRegistry } = require('../battle/DeadEntityRegistry');
const { MapTileManager } = require('../battle/MapTileManager');
const { BossFactory, BossManager } = require('../bosses');
const { WaveManager } = require('../battle/WaveManager');
const { BattleManager } = require('../battle/BattleManager');
const { BattleFlowCoordinator } = require('../battle/BattleFlowCoordinator');
const {
  DevelopmentLifecycleService,
  DevelopmentSpecialSpawnPolicy,
  DevelopmentMatchPreparation,
  DevelopmentLoadingEffects,
  DevelopmentTutorialController,
  DevelopmentBattleTimingOverride,
} = require('../battle/dev/DevelopmentBattleServices');
const { BattleTarget } = require('../entities/BattleTarget');
const { Mob0Enemy } = require('../entities/Mob0Enemy');
const { Mob1Enemy, Mob2Enemy, Mob3Enemy, ZombieEnemy, CavalryEnemy, PuppetEnemy } = require('../entities/types');
const { DevelopmentPlatform } = require('../platform/dev/DevelopmentPlatform');
const { DevelopmentStartupPolicy } = require('../platform/dev/DevelopmentStartupPolicy');
const { DevelopmentAudio } = require('../platform/dev/DevelopmentAudio');
const { DevelopmentTipService } = require('../platform/dev/DevelopmentTipService');
const { DevelopmentNetworkData } = require('../network/dev/DevelopmentNetworkData');
const { DevelopmentResourceLoader, DevelopmentSceneTransition } = require('./DevelopmentServices');
const { DevelopmentUnitSpawner } = require('../battle/dev/DevelopmentUnitSpawner');
const {
  DevelopmentUnitPresentation,
  DevelopmentUnitAudio,
  DevelopmentKnifeEffects,
} = require('../battle/dev/DevelopmentUnitServices');
const {
  DevelopmentEnemyPresentation,
  DevelopmentEnemyAudio,
  DevelopmentEnemyEffects,
  DevelopmentEnemyRewardService,
} = require('../battle/dev/DevelopmentCombatServices');
const { DevelopmentProjectileEffects } = require('../battle/dev/DevelopmentRangedBattleServices');

/**
 * 第三轮开发启动器。
 *
 * DEVELOPMENT_ONLY：隔离微信、字节、真实网络、广告、分享、授权和云存档。
 * directBattle 通过临时 window.$_main_ 绕过 LoadScene，但仍执行真实的
 * GameDataCore → BattleFlowCoordinator → BattleManager → BattleSceneController 链路。
 */
class DevelopmentBootstrap {
  constructor(options = {}) {
    if (!options.Laya) throw new TypeError('DevelopmentBootstrap requires an explicit Laya runtime or mock');
    this.Laya = options.Laya;
    this.windowRef = options.windowRef || globalThis;
    this.config = createDevelopmentConfig(options.config || {}, true);
    this.platformOptions = {
      directMatch: Boolean(this.config.forceMatchLaunch),
      ...(options.platformOptions || {}),
    };
    this.networkOptions = options.networkOptions || {};
    this.resourceLoaderOptions = options.resourceLoaderOptions || {};
    this.randomSource = options.random || (() => 0);
    this.now = options.now || (() => this.Laya.timer.currTimer + 5001);
    this.logger = options.logger || console;
    this.sceneFactoryRegistrar = options.sceneFactoryRegistrar || ((path, factory) => {
      if (!this.Laya.Scene.registerFactory) throw new Error('Development Laya runtime must expose Scene.registerFactory');
      this.Laya.Scene.registerFactory(path, factory);
    });
    this.nodeFactory = options.nodeFactory || null;
    this.lastBattleScene = null;
    this.context = null;
  }

  createContext() {
    globalThis.Laya = this.Laya;

    // 场景类必须在 Laya 全局安装后载入，以保留 extends Laya.Scene。
    const { LoadSceneController } = require('../scenes/LoadSceneController');
    const { MainSceneController } = require('../scenes/MainSceneController');
    const { MatchSceneController } = require('../scenes/MatchSceneController');
    const { BattleSceneController } = require('../scenes/BattleSceneController');
    const { GameOverSceneController } = require('../scenes/GameOverSceneController');

    const eventBus = new EventBus();
    const gameLoop = GameLoop.instance().configure({ laya: this.Laya });
    const sceneManager = SceneManager.instance().configure({ laya: this.Laya });
    sceneManager.init();
    const random = new MathRandom(this.randomSource);

    const gameData = GameDataCore.instance().configure({
      eventBus,
      developmentSample: true,
      playerOverrides: this.config.playerOverrides,
      random: this.randomSource,
      now: this.now,
    });

    const placementReservations = PlacementReservationRegistry.instance();
    placementReservations.clear();

    const unitManager = UnitRegistry.instance();
    unitManager.resetForTests();

    const animationEntityPool = AnimationEntityPool.instance().configure({
      laya: this.Laya,
      // DEVELOPMENT_SAMPLE：aDou 在原代码中走骨骼动画分支。
      isFrameAnimation: () => false,
      createFrameAnimation: animationId => {
        throw new Error(`Frame animation ${animationId} is not part of the BOOT-TO-BATTLE slice`);
      },
      createSkeletonAnimation: (_resourcePath, animationId) => {
        if (animationId !== 'aDou') {
          throw new Error(`Skeleton animation ${animationId} is not reconstructed in round 03`);
        }
        return new BattleTarget({ laya: this.Laya });
      },
    });
    const objectPool = new ObjectPool({ laya: this.Laya });
    const buffManager = new BuffManager({ gameLoop, eventBus, objectPool, logger: this.logger });
    const economy = new BattleEconomy({ battleState: gameData.battle, eventBus, logger: this.logger });
    const deckManager = new DeckManager({ gameData, economy, randomSource: this.randomSource, logger: this.logger });
    const weaponManager = new WeaponManager({ buffManager, logger: this.logger });
    const enemyPresentation = new DevelopmentEnemyPresentation({
      laya: this.Laya,
      spawnDurationMs: this.config.enemySpawnDurationMs,
      deathDurationMs: this.config.enemyDeathDurationMs,
    });
    objectPool.registerKey('mob', () => enemyPresentation.createMobVisual(), visual => enemyPresentation.resetMobVisual(visual));
    objectPool.registerKey('boss', () => enemyPresentation.createMobVisual(), visual => enemyPresentation.resetMobVisual(visual));

    const enemyFactory = EnemyFactory.instance();
    enemyFactory.resetForTests();
    enemyFactory.configure({ objectPool });

    const enemyManager = EnemyManager.instance().configure({
      gameLoop,
      gameData,
      eventBus,
      factory: enemyFactory,
      laya: this.Laya,
      buffManager,
      logger: this.logger,
    });
    enemyManager.setRandomSource(this.randomSource);

    const deadEntityRegistry = new DeadEntityRegistry({ eventBus, logger: this.logger });
    const enemyAudio = new DevelopmentEnemyAudio();
    const enemyEffects = new DevelopmentEnemyEffects();
    const enemyRewards = new DevelopmentEnemyRewardService(gameData.battle, economy);
    const enemyDependencies = {
      laya: this.Laya,
      eventBus,
      gameData,
      enemyFactory,
      objectPool,
      parentResolver: () => this.lastBattleScene && this.lastBattleScene.gameObjectBox,
      presentation: enemyPresentation,
      audio: enemyAudio,
      effects: enemyEffects,
      rewardService: enemyRewards,
      buffManager,
      deadEntityRegistry,
      targetResolver: playerLane => {
        const scene = this.lastBattleScene;
        if (!scene) throw new Error('BattleScene is unavailable while resolving aDou target');
        return playerLane ? scene.playerTarget : scene.opponentTarget;
      },
      logger: this.logger,
    };
    enemyFactory.registerPooledClass('Mob0', Mob0Enemy, enemy => enemy.configure(enemyDependencies));
    enemyFactory.registerPooledClass('Mob1', Mob1Enemy, enemy => enemy.configure(enemyDependencies));
    enemyFactory.registerPooledClass('Mob2', Mob2Enemy, enemy => enemy.configure(enemyDependencies));
    enemyFactory.registerPooledClass('Mob3', Mob3Enemy, enemy => enemy.configure(enemyDependencies));
    enemyFactory.registerPooledClass('Zombie', ZombieEnemy, enemy => enemy.configure(enemyDependencies));
    enemyFactory.registerPooledClass('Cavalry', CavalryEnemy, enemy => enemy.configure(enemyDependencies));
    enemyFactory.registerPooledClass('Puppet', PuppetEnemy, enemy => enemy.configure(enemyDependencies));

    const skillAudioRegistry = new SkillAudioRegistry();
    const skillVfxRegistry = new SkillVfxRegistry();
    let skillPresentation = null;
    const mapTileManager = new MapTileManager({ gameData, placementReservations, randomSource: this.randomSource, logger: this.logger });
    const skillEffectPort = new SkillEffectPort({ buffManager, enemyManager, unitRegistry: unitManager, eventBus, deadEntityRegistry, mapTileManager, presentation: null, audioRegistry: skillAudioRegistry, logger: this.logger });
    const skillFactory = new SkillFactory({ objectPool });
    const skillManager = SkillManager.instance().configure({ gameLoop, factory: skillFactory, effectPort: skillEffectPort, presentation: null, logger: this.logger });
    skillManager.init();

    let bossManager = null;
    const bossFactory = new BossFactory({
      objectPool,
      dependencyResolver: () => ({
        ...enemyDependencies,
        enemyFactory: bossFactory,
        skillManager,
      }),
    });
    bossManager = BossManager.instance().configure({ factory: bossFactory, eventBus, enemyManager, logger: this.logger });
    bossManager.init();
    const waveManager = WaveManager.instance().configure({ gameData, enemyManager, bossManager, eventBus, randomSource: this.randomSource, logger: this.logger });
    waveManager.init();

    const specialSpawnPolicy = new DevelopmentSpecialSpawnPolicy();
    const battleManager = BattleManager.instance().configure({
      gameData,
      enemyManager,
      eventBus,
      gameLoop,
      unitManager,
      placementReservations,
      random,
      specialSpawnPolicy,
      waveManager,
      bossManager,
      skillManager,
      weaponManager,
      economy,
      laya: this.Laya,
      now: this.now,
      logger: this.logger,
    });

    const projectileEffects = new DevelopmentProjectileEffects();
    const projectileFactory = new ProjectileFactory({
      laya: this.Laya,
      objectPool,
      enemyManager,
      gameData,
      parentResolver: () => this.lastBattleScene && this.lastBattleScene.gameObjectBox,
      effects: projectileEffects,
      logger: this.logger,
    });
    const projectileManager = ProjectileManager.instance().configure({
      gameLoop,
      enemyManager,
      gameData,
      projectileFactory,
      laya: this.Laya,
      logger: this.logger,
    });
    const animationDriver = new DevelopmentAnimationDriver({
      gameLoop,
      stoppedEvent: this.Laya.Event.STOPPED,
      logger: this.logger,
    });

    const unitPresentation = new DevelopmentUnitPresentation({
      laya: this.Laya,
      animationDriver,
    });
    const unitAudio = new DevelopmentUnitAudio();
    const knifeEffects = new DevelopmentKnifeEffects();
    objectPool.registerKey(
      'soldier',
      () => unitPresentation.createSoldierVisual(),
      visual => unitPresentation.resetSoldierVisual(visual),
    );
    const knifeAttackTimeline = new KnifeAttackTimeline({
      laya: this.Laya,
      enemyManager,
      effects: knifeEffects,
      logger: this.logger,
    });
    const unitFactory = UnitFactory.instance();
    unitFactory.resetForTests();
    unitFactory.configure({
      objectPool,
      dependencyResolver: () => ({
        laya: this.Laya,
        gameData,
        gameLoop,
        eventBus,
        objectPool,
        presentation: unitPresentation,
        audio: unitAudio,
        enemyManager,
        attackTimeline: knifeAttackTimeline,
        projectileManager,
        buffManager,
        logger: this.logger,
      }),
    });
    unitManager.configure({
      unitFactory,
      gameData,
      eventBus,
      placementReservations,
      parentResolver: () => this.lastBattleScene && this.lastBattleScene.gameObjectBox,
      buffManager,
      mapTileManager,
      weaponManager,
      logger: this.logger,
    });
    buffManager.configure({ enemyManager, unitRegistry: unitManager, gameLoop, eventBus, objectPool, logger: this.logger });
    const developmentUnitSpawner = new DevelopmentUnitSpawner({ unitRegistry: unitManager, placementReservations, gameData });

    const platform = new DevelopmentPlatform(this.platformOptions);
    const startupPolicy = new DevelopmentStartupPolicy(this.config);
    const networkData = new DevelopmentNetworkData(this.networkOptions);
    const resourceLoader = new DevelopmentResourceLoader(this.resourceLoaderOptions);
    const loadingEffects = new DevelopmentLoadingEffects();
    const audio = new DevelopmentAudio();
    skillAudioRegistry.configure(audio);
    skillPresentation = new DevelopmentSkillPresentation({ laya: this.Laya, audioRegistry: skillAudioRegistry, vfxRegistry: skillVfxRegistry, animationEntityPool, layerResolver: () => this.lastBattleScene && this.lastBattleScene.getPresentationLayers(), logger: this.logger });
    mapTileManager.configure({ presentation: skillPresentation });
    skillEffectPort.configure({ presentation: skillPresentation, audioRegistry: skillAudioRegistry, deadEntityRegistry, mapTileManager });
    skillManager.configure({ gameLoop, factory: skillFactory, effectPort: skillEffectPort, presentation: skillPresentation, logger: this.logger });
    const tipService = new DevelopmentTipService();
    const matchPreparation = new DevelopmentMatchPreparation();
    const sceneTransition = new DevelopmentSceneTransition();
    const tutorialController = new DevelopmentTutorialController();
    const telemetry = new DevelopmentLifecycleService('Telemetry');
    const visualEffects = loadingEffects;
    const levelService = new UnitLevelService({ maxLevel: gameData.friendlyUnits.maxLevel });
    const mergeService = new UnitMergeService({ unitRegistry: unitManager, levelService, logger: this.logger });
    const inputController = new BattleInputController({ deckManager, economy, unitRegistry: unitManager, mergeService, mapTileManager, logger: this.logger });
    const aiController = new AIController({ gameLoop, gameData, deckManager, inputController, randomSource: this.randomSource, logger: this.logger, decisionIntervalMs: 800, initialUnitTarget: 1 });
    const focusController = new DevelopmentLifecycleService('BattleFocusController');
    const preBattleService = new DevelopmentBattleTimingOverride(
      gameData,
      this.config.developmentBattleStartDelayMs,
    );

    const battleFlow = BattleFlowCoordinator.instance().configure({
      network: networkData,
      gameData,
      telemetry,
      deckManager,
      economy,
      weaponManager,
      preBattleService,
      battleManager,
      projectileManager,
      animationDriver,
      enemyManager,
      unitManager,
      visualEffects,
      buffManager,
      skillManager,
      bossManager,
      waveManager,
      platform,
      sceneManager,
      aiController,
      inputController,
      focusController,
      tutorialController,
      matchPreparation,
      eventBus,
      skillPresentation,
      mapTileManager,
      deadEntityRegistry,
      logger: this.logger,
      now: this.now,
    });

    LoadSceneController.configureDependencies({
      laya: this.Laya,
      platform,
      gameLoop,
      loadingEffects,
      networkData,
      gameData,
      battleFlow,
      sceneManager,
      resourceLoader,
      resourceManifest: this.config.resourceManifest,
      startupPolicy,
      matchPreparation,
      logger: this.logger,
    });

    MainSceneController.configureDependencies({
      laya: this.Laya,
      gameData,
      sceneManager,
      audio,
      tipService,
      sceneTransition,
      matchPreparation,
      now: this.now,
    });

    MatchSceneController.configureDependencies({
      laya: this.Laya,
      gameLoop,
      sceneManager,
      battleFlow,
      gameData,
      matchPreparation,
      sceneTransition,
      tutorialEnabled: false,
    });

    BattleSceneController.configureDependencies({
      laya: this.Laya,
      gameLoop,
      sceneManager,
      eventBus,
      gameData,
      audio,
      placementReservations,
      matchPreparation,
      animationEntityPool,
      skillPresentation,
      mapTileManager,
      onBattleSceneOpened: scene => { this.lastBattleScene = scene; },
      shovelAction: null,
      refreshAction: () => inputController.execute(new BattleInputCommand(BattleInputCommandType.REFRESH, { side: true })),
      deckAction: () => deckManager.snapshot(),
      inputController,
      deckManager,
      economy,
    });

    GameOverSceneController.configureDependencies({ laya: this.Laya, sceneManager, audio, platformResultPort: null, animationEntityPool });

    const classes = { LoadSceneController, MainSceneController, MatchSceneController, BattleSceneController, GameOverSceneController };
    registerCriticalPathClasses(this.Laya, classes);

    this.context = {
      config: this.config,
      eventBus,
      gameLoop,
      fixedUpdate: gameLoop, // 兼容早期第三轮命名。
      sceneManager,
      random,
      gameData,
      gameState: gameData,
      placementReservations,
      animationEntityPool,
      entityFactory: animationEntityPool, // 兼容早期第三轮上下文命名。
      objectPool,
      unitFactory,
      unitPresentation,
      unitAudio,
      knifeEffects,
      knifeAttackTimeline,
      projectileEffects,
      projectileFactory,
      projectileManager,
      animationDriver,
      developmentUnitSpawner,
      enemyPresentation,
      enemyAudio,
      enemyEffects,
      enemyRewards,
      unitManager,
      unitRegistry: unitManager,
      enemyFactory,
      enemyManager,
      specialSpawnPolicy,
      battleManager,
      battleFlow,
      platform,
      startupPolicy,
      networkData,
      network: networkData,
      resourceLoader,
      loadingEffects,
      audio,
      tipService,
      matchPreparation,
      sceneTransition,
      tutorialController,
      telemetry,
      deckManager,
      economy,
      weaponManager,
      levelService,
      mergeService,
      aiController,
      inputController,
      buffManager,
      skillFactory,
      skillManager,
      skillEffectPort,
      skillPresentation,
      skillAudioRegistry,
      skillVfxRegistry,
      deadEntityRegistry,
      mapTileManager,
      bossFactory,
      bossManager,
      waveManager,
      focusController,
      preBattleService,
      classes,
    };

    this.installDevelopmentSceneFactories();
    return this.context;
  }

  installDevelopmentSceneFactories() {
    const { classes } = this.context;

    this.sceneFactoryRegistrar('scene/LoadScene.ls', () => {
      const scene = new classes.LoadSceneController();
      scene.name = 'LoadScene';
      scene.progressBar = this.createNode('progressBar', { width: 400, height: 20 });
      scene.loadingTxt = this.createNode('loadingTxt', { text: '' });
      scene.zhao = this.createNode('zhao', { x: 0 });
      scene.addChild(scene.progressBar);
      scene.addChild(scene.loadingTxt);
      scene.addChild(scene.zhao);
      return scene;
    });

    this.sceneFactoryRegistrar('scene/MainScene.ls', () => {
      const scene = new classes.MainSceneController();
      scene.name = 'MainScene';
      scene.playBtn = this.createNode('playBtn');
      scene.addChild(scene.playBtn);
      return scene;
    });

    this.sceneFactoryRegistrar('scene/MatchScene.ls', () => {
      const scene = new classes.MatchSceneController();
      scene.name = 'MatchScene';
      scene.title = this.createNode('title', { text: '' });
      scene.xBtn = this.createNode('xBtn');
      scene.addChild(scene.title);
      scene.addChild(scene.xBtn);
      return scene;
    });

    this.sceneFactoryRegistrar('scene/BattleScene.ls', () => {
      const scene = new classes.BattleSceneController();
      scene.name = 'BattleScene';
      scene.map = this.createNode('map');
      scene.gameObjectBox = this.createNode('gameObjectBox');
      scene.effectBox = this.createNode('effectBox');
      // DEVELOPMENT_SCENE_STUB：真实尺寸与序列化组件必须由缺失的 BattleScene.ls 恢复。
      scene.end1 = this.createNode('end1', { visible: false, width: 90, height: 70 });
      scene.end2 = this.createNode('end2', { visible: false, width: 90, height: 70 });
      scene.round = this.createNode('round', { text: '' });
      scene.goldNumTxt = this.createNode('goldNumTxt', { text: '' });
      scene.shovelAdBg = this.createNode('shovelAdBg', { alpha: 0 });
      scene.adLight = this.createNode('adLight', { rotation: 0 });
      for (const node of [
        scene.map,
        scene.gameObjectBox,
        scene.effectBox,
        scene.end1,
        scene.end2,
        scene.round,
        scene.goldNumTxt,
        scene.shovelAdBg,
        scene.adLight,
      ]) scene.addChild(node);
      return scene;
    });

    this.sceneFactoryRegistrar('scene/GameOverScene.ls', () => {
      const scene = new classes.GameOverSceneController();
      scene.name = 'GameOverScene';
      for (const name of ['title','goldText','roundText','starText','continueBtn','homeBtn']) {
        const node = this.createNode(name, { text: '' }); scene[name] = node; scene.addChild(node);
      }
      return scene;
    });
  }

  createNode(name, properties = {}) {
    if (this.nodeFactory) return this.nodeFactory(name, properties);
    const node = new this.Laya.Sprite();
    node.name = name;
    Object.assign(node, properties);
    return node;
  }

  async start() {
    if (!this.context) this.createContext();
    const previousMain = this.windowRef.$_main_;

    if (this.config.directBattle) {
      this.windowRef.$_main_ = async () => {
        const { platform, gameLoop, loadingEffects, networkData, gameData, battleFlow } = this.context;
        platform.initialize();
        gameLoop.init();
        loadingEffects.init();
        networkData.init(platform.getChannelAppId());
        gameData.init();
        battleFlow.init();
        return battleFlow.startBattle();
      };
    }

    try {
      this.gameBootstrap = new GameBootstrap({ Laya: this.Laya, windowRef: this.windowRef });
      this.startupScene = await this.gameBootstrap.start();
      if (this.gameBootstrap.initializationError) throw this.gameBootstrap.initializationError;
      if (this.startupScene && this.startupScene.startupPromise) await this.startupScene.startupPromise;
      return this.context;
    } finally {
      if (this.config.directBattle) {
        if (previousMain === undefined) delete this.windowRef.$_main_;
        else this.windowRef.$_main_ = previousMain;
      }
    }
  }

  static resetSingletonsForTests() {
    for (const Type of [
      GameLoop,
      SceneManager,
      GameDataCore,
      PlacementReservationRegistry,
      AnimationEntityPool,
      UnitRegistry,
      UnitFactory,
      EnemyFactory,
      EnemyManager,
      BattleManager,
      ProjectileManager,
      SkillManager,
      BossManager,
      WaveManager,
      BattleFlowCoordinator,
    ]) Type.resetInstanceForTests();
    Mob0Enemy.resetIdsForTests();
  }
}

module.exports = { DevelopmentBootstrap };
