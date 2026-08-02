'use strict';

class DevelopmentAudio {
  constructor() { this.calls = []; }
  init(musicVolume = 1, soundVolume = 1) { this.calls.push(['init', musicVolume, soundVolume]); }
  playMusic(name) { this.calls.push(['playMusic', name]); }
  stopMusic() { this.calls.push(['stopMusic']); }
}

module.exports = { DevelopmentAudio };
