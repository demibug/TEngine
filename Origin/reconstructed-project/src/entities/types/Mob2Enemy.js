'use strict';
const { ConfiguredEnemy } = require('./ConfiguredEnemy');
class Mob2Enemy extends ConfiguredEnemy { constructor(){ super({ typeKey:'Mob2', typeIndex:2, resourcePath:'resources/img/gameObject/enemy/mob_2.png' }); } }
Mob2Enemy.originalSymbol='qa'; Mob2Enemy.sourceRange='32811-32864';
module.exports={ Mob2Enemy };
