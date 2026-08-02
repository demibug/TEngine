'use strict';
const { ConfiguredEnemy } = require('./ConfiguredEnemy');
class Mob1Enemy extends ConfiguredEnemy { constructor(){ super({ typeKey:'Mob1', typeIndex:1, resourcePath:'resources/img/gameObject/enemy/mob_1.png' }); } }
Mob1Enemy.originalSymbol='pm'; Mob1Enemy.sourceRange='32574-32631';
module.exports={ Mob1Enemy };
