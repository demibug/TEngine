'use strict';
const { ConfiguredEnemy } = require('./ConfiguredEnemy');
class ZombieEnemy extends ConfiguredEnemy {
  constructor(){ super({ typeKey:'Zombie', typeIndex:4, resourcePath:'resources/img/gameObject/enemy/zombie.png' }); this.bubbleElapsedMs=0; this.bubbleEffectDeferred=true; }
  update(deltaMs){ super.update(deltaMs); this.bubbleElapsedMs += deltaMs; }
  gameOver(){ this.bubbleElapsedMs=0; return super.gameOver(); }
}
ZombieEnemy.originalSymbol='sQ'; ZombieEnemy.sourceRange='31932-32135';
module.exports={ ZombieEnemy };
