'use strict';
const { ConfiguredEnemy } = require('./ConfiguredEnemy');
class CavalryEnemy extends ConfiguredEnemy {
  constructor(){ super({ typeKey:'Cavalry', typeIndex:5, resourcePath:'resources/img/gameObject/soldier/soldier_3.png', baseSpeedOverride:80 }); this.auraResource='resources/img/gameObject/enemy/yellowCircle.png'; }
}
CavalryEnemy.originalSymbol='qT'; CavalryEnemy.sourceRange='32390-32467';
module.exports={ CavalryEnemy };
