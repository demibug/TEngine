class WeaponBase {
  constructor(){
    this.weaponId=-1; this.type=-1; this.txt=""; this.rarity=0; this.intro=""; this.addAttackPower=0;
    this.owner=null; this.active=false; this.attackCount=0; this.projectileFactory=null; this.buffService=null; this.skillService=null;
  }
  init(id,type){
    this.weaponId=id; this.type=type;
    const cfg=this.constructor.config || {};
    this.txt=cfg.name||this.txt; this.rarity=cfg.rarity||0; this.intro=cfg.desc||"";
    this.addAttackPower=cfg.addAttackPower||0;
  }
  attach(owner){this.owner=owner; this.active=true;}
  update(dt){}
  startGame(){}
  attack(ctx){ this.attackCount++; }
  selectTarget(){ return null; }
  gameOver(){this.active=false; this.owner=null;}
  reset(){this.weaponId=-1;this.type=-1;this.owner=null;this.active=false;this.attackCount=0;}
}
module.exports=WeaponBase;
