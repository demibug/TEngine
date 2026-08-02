'use strict';
const { BuffTimeMode } = require('./BuffTimeMode');
function createBuffData(num, multiplicative, time = BuffTimeMode.PERMANENT, custom = null) {
  return { num, Nw: Boolean(multiplicative), time, qw: custom };
}
module.exports = { createBuffData };
