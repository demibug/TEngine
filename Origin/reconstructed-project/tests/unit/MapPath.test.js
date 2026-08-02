'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { MapData, AStarGrid, AStarPathfinder } = require('../../src/battle/MapData');

test('MapData restores the confirmed four-direction paths for both sides', () => {
  const map = new MapData();
  assert.deepEqual(map.playerRoute, [
    {x:0,y:8},{x:0,y:7},{x:0,y:6},{x:1,y:6},{x:2,y:6},{x:3,y:6},{x:4,y:6},
    {x:4,y:5},{x:4,y:4},{x:5,y:4},{x:6,y:4},{x:7,y:4},{x:7,y:5},{x:7,y:6},
    {x:7,y:7},{x:7,y:8},{x:7,y:9},
  ]);
  assert.deepEqual(map.opponentRoute, [
    {x:7,y:1},{x:7,y:2},{x:7,y:3},{x:6,y:3},{x:5,y:3},{x:4,y:3},{x:3,y:3},
    {x:3,y:4},{x:3,y:5},{x:2,y:5},{x:1,y:5},{x:0,y:5},{x:0,y:4},{x:0,y:3},
    {x:0,y:2},{x:0,y:1},{x:0,y:0},
  ]);
  for (const route of [map.playerRoute, map.opponentRoute]) {
    for (let i = 1; i < route.length; i += 1) {
      const dx = Math.abs(route[i].x - route[i - 1].x);
      const dy = Math.abs(route[i].y - route[i - 1].y);
      assert.equal(dx + dy, 1, 'A* route must not contain diagonal steps');
    }
  }
});

test('A* fails explicitly when no walkable route exists', () => {
  const grid = new AStarGrid(3, 3);
  grid.setStart(0, 0);
  grid.setEnd(2, 2);
  grid.setWalkable(0, 1, false);
  grid.setWalkable(1, 0, false);
  const finder = new AStarPathfinder();
  assert.equal(finder.find(grid), false);
  assert.equal(finder.path, null);
});
