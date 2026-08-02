'use strict';
const { BuffDefinitions } = require('./BuffDefinitions');
class BuffRegistry { static entries(){ return [...BuffDefinitions.values()].map(item => ({...item, channels:[...item.channels]})); } }
module.exports = { BuffRegistry };
