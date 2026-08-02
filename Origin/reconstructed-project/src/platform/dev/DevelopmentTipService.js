'use strict';

class DevelopmentTipService {
  constructor() { this.messages = []; }
  showTip(message) { this.messages.push(message); }
}

module.exports = { DevelopmentTipService };
