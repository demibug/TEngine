'use strict';

/**
 * 重建模块：ENEMY-RUNTIME-01 地图路径
 * 原始范围：bundle.strings-decoded.js:12194-12847
 * 原始符号：tl（A*）、ru（节点）、oS（格网）、s4（MapData）
 * 重建状态：COMPLETE_FOR_MOB0_PATHING
 */

class AStarNode {
  constructor(x, y) {
    this.walkable = true;      // Yh
    this.costMultiplier = 1;   // Xh
    this.x = x;
    this.y = y;
    this.f = 0;
    this.g = 0;
    this.h = 0;
    this.parentNode = null;
  }
}

class AStarGrid {
  constructor(width, height) {
    this.width = width;   // Nh/Uh
    this.height = height; // qh/Fh
    this.nodes = new Array(width);
    for (let x = 0; x < width; x += 1) {
      this.nodes[x] = new Array(height);
      for (let y = 0; y < height; y += 1) this.nodes[x][y] = new AStarNode(x, y);
    }
    this.startNode = null;
    this.endNode = null;
  }
  nodeAt(x, y) { return this.nodes[x][y]; }
  setStart(x, y) { this.startNode = this.nodes[x][y]; }
  setEnd(x, y) { this.endNode = this.nodes[x][y]; }
  setWalkable(x, y, walkable) { this.nodes[x][y].walkable = Boolean(walkable); }
}

class AStarPathfinder {
  constructor() {
    this.straightCost = 1;       // bh
    this.diagonalCost = 1.4;     // Mh; diagonal expansion is excluded by original search condition
    this.heuristic = this.diagonal.bind(this); // Ph = Ah
    this.path = null;
  }

  find(grid) {
    this.grid = grid;
    this.open = [];
    this.closed = [];
    this.start = grid.startNode;
    this.end = grid.endNode;
    this.start.g = 0;
    this.start.h = this.heuristic(this.start);
    this.start.f = this.start.g + this.start.h;
    return this.search();
  }

  search() {
    let current = this.start;
    while (current !== this.end) {
      const minX = Math.max(0, current.x - 1);
      const maxX = Math.min(this.grid.width - 1, current.x + 1);
      const minY = Math.max(0, current.y - 1);
      const maxY = Math.min(this.grid.height - 1, current.y + 1);
      for (let x = minX; x <= maxX; x += 1) {
        for (let y = minY; y <= maxY; y += 1) {
          // CONFIRMED：原条件排除所有对角邻居，因此本工程路线是四方向 A*。
          if (x !== current.x && y !== current.y) continue;
          const neighbor = this.grid.nodeAt(x, y);
          if (neighbor === current || !neighbor.walkable ||
              !this.grid.nodeAt(current.x, neighbor.y).walkable ||
              !this.grid.nodeAt(neighbor.x, current.y).walkable) continue;
          let stepCost = this.straightCost;
          if (current.x !== neighbor.x && current.y !== neighbor.y) stepCost = this.diagonalCost;
          const g = current.g + stepCost * neighbor.costMultiplier;
          const h = this.heuristic(neighbor);
          const f = g + h;
          if (this.open.includes(neighbor) || this.closed.includes(neighbor)) {
            if (neighbor.f > f) {
              neighbor.f = f; neighbor.g = g; neighbor.h = h; neighbor.parentNode = current;
            }
          } else {
            neighbor.f = f; neighbor.g = g; neighbor.h = h; neighbor.parentNode = current;
            this.open.push(neighbor);
          }
        }
      }
      this.closed.push(current);
      if (this.open.length <= 0) return false;
      // CONFIRMED：原代码使用稳定的双层交换排序，再 shift 最小 f。
      for (let i = 0; i < this.open.length; i += 1) {
        for (let j = i + 1; j < this.open.length; j += 1) {
          if (this.open[i].f > this.open[j].f) [this.open[i], this.open[j]] = [this.open[j], this.open[i]];
        }
      }
      current = this.open.shift();
    }
    this.buildPath();
    return true;
  }

  buildPath() {
    this.path = [this.end];
    let node = this.end;
    while (node !== this.start) {
      node = node.parentNode;
      this.path.unshift(node);
    }
  }

  diagonal(node) {
    const dx = Math.abs(node.x - this.end.x);
    const dy = Math.abs(node.y - this.end.y);
    const diagonal = Math.min(dx, dy);
    return this.diagonalCost * diagonal + this.straightCost * (dx + dy - 2 * diagonal);
  }
}

const MAP_BLOCKS = Object.freeze([
  Object.freeze({
    map: Object.freeze([
      Object.freeze(['0_1','0_1','0_1','0_1','0_1','0_1','0_0','0_0','0_0','0_0']),
      Object.freeze(['2_1','2_1','2_1','2_1','2_1','0_1','0_0','2_0','2_0','2_0']),
      Object.freeze(['2_1','2_1','2_1','2_1','2_1','0_1','0_0','1_0','1_0','2_0']),
      Object.freeze(['2_1','1_1','1_1','0_1','0_1','0_1','0_0','1_0','1_0','2_0']),
      Object.freeze(['2_1','1_1','1_1','0_1','0_0','0_0','0_0','1_0','1_0','2_0']),
      Object.freeze(['2_1','1_1','1_1','0_1','0_0','2_0','2_0','2_0','2_0','2_0']),
      Object.freeze(['2_1','2_1','2_1','0_1','0_0','2_0','2_0','2_0','2_0','2_0']),
      Object.freeze(['0_1','0_1','0_1','0_1','0_0','0_0','0_0','0_0','0_0','0_0']),
    ]),
    playerEntry: Object.freeze({ x: 0, y: 9 }), playerStart: Object.freeze({ x: 0, y: 8 }), playerEnd: Object.freeze({ x: 7, y: 9 }),
    opponentEntry: Object.freeze({ x: 7, y: 0 }), opponentStart: Object.freeze({ x: 7, y: 1 }), opponentEnd: Object.freeze({ x: 0, y: 0 }),
    routeMarkers: Object.freeze([{ x: 0, y: 6 }, { x: 4, y: 6 }, { x: 4, y: 4 }, { x: 8, y: 4 }]), enemyTypeIndex: 0,
  }),
  Object.freeze({
    map: Object.freeze([
      Object.freeze(['0_1','0_1','0_1','0_1','0_1','2_0','0_0','0_0','0_0','0_0']),
      Object.freeze(['2_1','2_1','2_1','2_1','0_1','2_0','0_0','2_0','2_0','2_0']),
      Object.freeze(['2_1','2_1','2_1','2_1','0_1','2_0','0_0','1_0','1_0','2_0']),
      Object.freeze(['2_1','1_1','1_1','0_1','0_1','2_0','0_0','1_0','1_0','2_0']),
      Object.freeze(['2_1','1_1','1_1','0_1','2_1','0_0','0_0','1_0','1_0','2_0']),
      Object.freeze(['2_1','1_1','1_1','0_1','2_1','0_0','2_0','2_0','2_0','2_0']),
      Object.freeze(['2_1','2_1','2_1','0_1','2_1','0_0','2_0','2_0','2_0','2_0']),
      Object.freeze(['0_1','0_1','0_1','0_1','2_1','0_0','0_0','0_0','0_0','0_0']),
    ]),
    playerEntry: Object.freeze({ x: 0, y: 9 }), playerStart: Object.freeze({ x: 0, y: 8 }), playerEnd: Object.freeze({ x: 7, y: 9 }),
    opponentEntry: Object.freeze({ x: 7, y: 0 }), opponentStart: Object.freeze({ x: 7, y: 1 }), opponentEnd: Object.freeze({ x: 0, y: 0 }),
    routeMarkers: Object.freeze([{ x: 0, y: 5 }, { x: 8, y: 5 }]), enemyTypeIndex: 1,
  }),
  Object.freeze({
    map: Object.freeze([
      Object.freeze(['2_1','0_1','0_1','0_1','0_1','0_0','0_0','0_0','0_0','2_0']),
      Object.freeze(['2_1','0_1','2_1','2_1','2_1','2_0','2_0','2_0','0_0','2_0']),
      Object.freeze(['0_1','0_1','2_1','2_1','2_1','2_0','1_0','1_0','0_0','2_0']),
      Object.freeze(['0_1','2_1','1_1','1_1','2_1','2_0','1_0','1_0','0_0','0_0']),
      Object.freeze(['0_1','0_1','1_1','1_1','2_1','2_0','1_0','1_0','2_0','0_0']),
      Object.freeze(['2_1','0_1','1_1','1_1','2_1','2_0','2_0','2_0','0_0','0_0']),
      Object.freeze(['2_1','0_1','2_1','2_1','2_1','2_0','2_0','2_0','0_0','2_0']),
      Object.freeze(['2_1','0_1','0_1','0_1','0_1','0_0','0_0','0_0','0_0','2_0']),
    ]),
    playerEntry: Object.freeze({ x: 0, y: 5 }), playerStart: Object.freeze({ x: 0, y: 6 }), playerEnd: Object.freeze({ x: 7, y: 5 }),
    opponentEntry: Object.freeze({ x: 7, y: 4 }), opponentStart: Object.freeze({ x: 7, y: 3 }), opponentEnd: Object.freeze({ x: 0, y: 4 }),
    routeMarkers: Object.freeze([{ x: 0, y: 5 }, { x: 8, y: 5 }]), enemyTypeIndex: 2,
  }),
  Object.freeze({
    map: Object.freeze([
      Object.freeze(['2_1','0_1','0_1','0_1','0_1','0_0','0_0','0_0','0_0','2_0']),
      Object.freeze(['2_1','0_1','2_1','2_1','2_1','2_0','2_0','2_0','0_0','2_0']),
      Object.freeze(['2_1','0_1','0_1','0_1','0_1','0_0','0_0','0_0','0_0','2_0']),
      Object.freeze(['1_1','1_1','1_1','2_1','0_1','0_0','2_0','1_0','1_0','1_0']),
      Object.freeze(['1_1','1_1','1_1','2_1','0_1','0_0','2_0','1_0','1_0','1_0']),
      Object.freeze(['2_1','0_1','0_1','0_1','0_1','0_0','0_0','0_0','0_0','2_0']),
      Object.freeze(['2_1','0_1','2_1','2_1','2_1','2_0','2_0','2_0','0_0','2_0']),
      Object.freeze(['2_1','0_1','0_1','0_1','0_1','0_0','0_0','0_0','0_0','2_0']),
    ]),
    playerEntry: Object.freeze({ x: 0, y: 5 }), playerStart: Object.freeze({ x: 0, y: 6 }), playerEnd: Object.freeze({ x: 7, y: 5 }),
    opponentEntry: Object.freeze({ x: 7, y: 4 }), opponentStart: Object.freeze({ x: 7, y: 3 }), opponentEnd: Object.freeze({ x: 0, y: 4 }),
    routeMarkers: Object.freeze([{ x: 0, y: 5 }, { x: 8, y: 5 }]), enemyTypeIndex: 3,
  }),
]);

function clonePoint(point) { return { x: point.x, y: point.y }; }
function cloneBlock(block) {
  return {
    map: block.map.map(column => column.slice()),
    playerEntry: clonePoint(block.playerEntry), playerStart: clonePoint(block.playerStart), playerEnd: clonePoint(block.playerEnd),
    opponentEntry: clonePoint(block.opponentEntry), opponentStart: clonePoint(block.opponentStart), opponentEnd: clonePoint(block.opponentEnd),
    routeMarkers: block.routeMarkers.map(clonePoint), enemyTypeIndex: block.enemyTypeIndex,
  };
}

class MapData {
  constructor() {
    this.mapIndex = 0;
    this.gridWidth = 80;  // ye
    this.gridHeight = 80; // gridHei
    this.cellWidth = 80;
    this.cellHeight = 80;
    this.playerExpansionComplete = false;
    this.opponentExpansionComplete = false;
    this.playerGuideComplete = false;
    this.opponentGuideComplete = false;
    this._opponentRouteCache = new Map(); // 原 s4.Ce：只缓存 b=false 分支
    this.changeMap(0);
  }

  initialize(mapIndex) { this.changeMap(mapIndex); }
  startGame(mapIndex) { this.changeMap(mapIndex); }
  blockByIndex(mapIndex) {
    const block = MAP_BLOCKS[mapIndex];
    if (!block) throw new Error(`MapData.mapDataBlockByIndex: invalid mapIndex ${mapIndex}`);
    return block;
  }
  mapByIndex(mapIndex) { return this.blockByIndex(mapIndex).map; }

  static findPath(map, start, end) {
    if (!Array.isArray(map) || !map.length || !Array.isArray(map[0])) throw new TypeError('MapData.findPath requires a rectangular map array');
    const grid = new AStarGrid(map.length, map[0].length);
    grid.setStart(start.x, start.y);
    grid.setEnd(end.x, end.y);
    for (let x = 0; x < map.length; x += 1) {
      for (let y = 0; y < map[x].length; y += 1) {
        if (map[x][y] !== '0_0' && map[x][y] !== '0_1') grid.setWalkable(x, y, false);
      }
    }
    const finder = new AStarPathfinder();
    return finder.find(grid) ? finder.path.map(node => ({ x: node.x, y: node.y })) : null;
  }

  get width() { return this.map ? this.map.length : 0; }
  get height() { return this.map && this.map[0] ? this.map[0].length : 0; }
  blockAt(x, y) { return this.map && this.map[x] ? this.map[x][y] : undefined; }
  isBuildableForSide(side, x, y) {
    const code = this.blockAt(x, y);
    if (typeof code !== 'string') return false;
    const [kind, lane] = code.split('_');
    return kind === '1' && Number(lane) === (side ? 1 : 0);
  }

  pathForSide(playerSide) {
    return playerSide ? this.playerRoute : this.opponentRoute;
  }

  computeActivePath(playerSide) {
    return MapData.findPath(
      this.map,
      playerSide ? this.playerStart : this.opponentStart,
      playerSide ? this.playerEnd : this.opponentEnd,
    );
  }

  pathByMapIndex(mapIndex, playerSide) {
    // CONFIRMED：原缓存仅用于 false/opponent 分支；true 分支每次计算。
    if (!playerSide && this._opponentRouteCache.has(mapIndex)) return this._opponentRouteCache.get(mapIndex);
    const block = this.blockByIndex(mapIndex);
    const path = MapData.findPath(
      block.map,
      playerSide ? block.playerStart : block.opponentStart,
      playerSide ? block.playerEnd : block.opponentEnd,
    ) || [];
    if (!playerSide) this._opponentRouteCache.set(mapIndex, path);
    return path;
  }

  pathPointsWithinRadius(point, radius, playerSide) {
    const radiusSquared = radius * radius;
    return this.pathForSide(playerSide).filter(node => {
      const dx = node.x * this.gridWidth - point.x;
      const dy = node.y * this.gridHeight - point.y;
      return dx * dx + dy * dy <= radiusSquared;
    });
  }

  changeMap(mapIndex) {
    const active = cloneBlock(this.blockByIndex(mapIndex));
    this.mapIndex = mapIndex;
    this.map = active.map;
    this.playerEntry = active.playerEntry;
    this.playerStart = active.playerStart;
    this.playerEnd = active.playerEnd;
    this.opponentEntry = active.opponentEntry;
    this.opponentStart = active.opponentStart;
    this.opponentEnd = active.opponentEnd;
    this.routeMarkers = active.routeMarkers;
    this.enemyTypeIndex = active.enemyTypeIndex;
    this.enemyRouteType = active.enemyTypeIndex;
    this.playerRoute = this.computeActivePath(true);
    this.opponentRoute = this.computeActivePath(false);
    // Trace aliases matching original s4 fields.
    this.Le = this.playerRoute;
    this.me = this.opponentRoute;
    this.ye = this.gridWidth;
    this.gridHei = this.gridHeight;
  }

  gameOver() {
    this.playerExpansionComplete = false;
    this.opponentExpansionComplete = false;
    this.playerGuideComplete = false;
    this.opponentGuideComplete = false;
  }
}

module.exports = { AStarNode, AStarGrid, AStarPathfinder, MAP_BLOCKS, MapData };
