const Trail2DAdapter = require('../Trail2DAdapter');
class DevelopmentTrail2DAdapter extends Trail2DAdapter {
  create() { this.enabled = true; return this; }
}
module.exports = DevelopmentTrail2DAdapter;
