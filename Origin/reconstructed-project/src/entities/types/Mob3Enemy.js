'use strict';
const { ConfiguredEnemy } = require('./ConfiguredEnemy');
class Mob3Enemy extends ConfiguredEnemy { constructor(){ super({ typeKey:'Mob3', typeIndex:3, resourcePath:'resources/img/gameObject/enemy/mob_3.png' }); } }
Mob3Enemy.originalSymbol='q6'; Mob3Enemy.sourceRange='32698-32752';
module.exports={ Mob3Enemy };
