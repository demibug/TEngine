'use strict';
class Trail2DWeaponRenderer {
  constructor(adapter){if(!adapter)throw new TypeError('Trail2DWeaponRenderer requires adapter');this.adapter=adapter;}
  attachTrail(config){this.adapter.configure(config||{});return this.adapter.create();}
  update(position){this.adapter.sync(position);}
  pause(){this.adapter.pause();}
  resume(){this.adapter.resume();}
  release(delay){this.adapter.fade(delay==null?((this.adapter.config&&this.adapter.config.time)||0)*1000:delay);}
  clear(){this.adapter.clear();}
}
module.exports=Trail2DWeaponRenderer;
