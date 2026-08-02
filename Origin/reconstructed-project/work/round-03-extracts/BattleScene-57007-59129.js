          c = class extends tQ {
            constructor() {
              var a = hr,
                b = hu,
                c = a[0],
                d = "Point";
              var e;
              e = arguments;
              super(...e), this["rp"] = [], this["Vy"] = new Laya["Point"], this["Qy"] = new Laya["Point"], this["pm"] = !1, this["mapIndex"] = 0, this["dq"] = [], this["Lq"] = new Map, this["mq"] = 0, this["wq"] = b[123], this["vq"] = 0, this["_q"] = 0, this["kq"] = b[167], this["Sq"] = 0, this["xq"] = 0, this["bq"] = null, this["Mq"] = !0, this["Pq"] = !1, this["Aq"] = 0, this["Eq"] = 0, this["Bq"] = 0, this["Dq"] = 0, this["Iq"] = 1, this["Cq"] = !1, this["Tq"] = !1, this["Rq"] = !1, this["Uq"] = !1, this["Fq"] = new Laya["Point"], this["Oq"] = new Laya["Point"], this["ie"] = new Laya["Point"], this["ae"] = new Laya["Point"]
            }
          };
          ! function() {
            var a = hr,
              d = hu,
              e = a[0],
              f = a[3],
              g = a[6],
              h = "defineProperty",
              rcQ = "value",
              rcR = "enumerable",
              rcS = "configurable",
              rcT = "writable";
            lZ["pop"]();
            for (let i of mZ) {
              if (hl && bR) {
                gR += 3
              }
              hl = 0;
              if (-1 == i) {}
              if (0 == i) {
                Object["defineProperty"](c["prototype"], "I$", {
                  ["value"](a, b) {
                    var c = hr;
                    a["parent"] !== b && b["addChild"](a)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (1 == i) {
                Object["defineProperty"](c["prototype"], "Xq", {
                  ["value"]() {
                    var a = hr,
                      b = a[0],
                      c = a[6],
                      d = a[3],
                      e = a[2],
                      f = "h$",
                      g = "instance",
                      h = "grass",
                      i = "map",
                      j = "addChild",
                      k = "on",
                      l = "Event",
                      m = "play",
                      n = "idle",
                      o = "mapIndex",
                      p = "e$",
                      q = "zIndex",
                      r = "Vn";
                    this["h$"] = nz["instance"]()["$d"]("grass"), this["map"]["addChild"](this["h$"]), this["h$"]["on"](Laya["Event"]["STOPPED"], this, () => {
                      this["h$"]["play"]("idle" + this["mapIndex"], !0)
                    }), this["h$"]["play"]("idle" + this["mapIndex"], !0), this["e$"] = nz["instance"]()["$d"]("grass"), this["map"]["addChild"](this["e$"]), this["e$"]["on"](Laya["Event"]["STOPPED"], this, () => {
                      this["e$"]["play"]("idle" + this["mapIndex"], !0)
                    }), this["e$"]["play"]("idle" + this["mapIndex"], !0), this["h$"]["zIndex"] = t1["Vn"], this["e$"]["zIndex"] = t1["Vn"]
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (2 == i) {
                Object["defineProperty"](c["prototype"], "mH", {
                  ["value"](a) {
                    var b = hr,
                      c = b[0];
                    this["round"]["text"] = "第" + this["sw"]["au"]["li"] + "波"
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (3 == i) {
                Object["defineProperty"](c["prototype"], "L$", {
                  ["value"]() {},
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (4 == i) {
                Object["defineProperty"](c["prototype"], "g$", {
                  ["value"](a, b) {
                    var c = hr,
                      d = c[0];
                    b > 0 ? (this["Jq"](a), this["addHp"](a, b)) : this["O$"](a, Math["abs"](b))
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (5 == i) {
                Object["defineProperty"](c["prototype"], "Hq", {
                  ["value"]() {
                    var a = hr,
                      b = a[0],
                      c = a[4],
                      d = a[2],
                      e = a[3],
                      f = "refreshBox",
                      g = "width",
                      h = "sw",
                      i = "map",
                      j = "fe",
                      k = "Image",
                      l = "resources/img/map/refresh_",
                      m = "mapIndex",
                      n = "height";
                    let o;
                    o = this["refreshBox"]["width"] / this["sw"]["map"]["fe"];
                    for (let e = 0; e < this["sw"]["map"]["fe"]; e++) {
                      let j;
                      j = 0 == e ? new Laya["Image"]("resources/img/map/refresh_" + this["mapIndex"] + "_0.png") : new Laya["Image"]("resources/img/map/refresh_" + this["mapIndex"] + "_1.png"), j["name"] = "grid", this["refreshBox"]["addChild"](j), j["size"](o, this["refreshBox"]["height"]), this["sw"]["map"]["ge"] = j["width"], this["sw"]["map"]["de"] = j["height"], j["pos"](o * e, 0)
                    }
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (6 == i) {
                Object["defineProperty"](c["prototype"], "z$", {
                  ["value"](a, b, c) {},
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (7 == i) {
                Object["defineProperty"](c["prototype"], "N$", {
                  ["value"](a, b) {
                    var c = hr,
                      d = hu,
                      e = c[0],
                      f = c[4],
                      g = c[3],
                      h = c[6],
                      i = c[2],
                      j = c[1],
                      k = "Uq",
                      l = "V$",
                      m = "effectBox",
                      n = "visible",
                      o = "Point",
                      p = "TEMP",
                      q = "globalToLocal",
                      r = "pos",
                      s = "map",
                      t = "to",
                      u = "then";
                    let v, w, x, y;
                    if (this["sw"]["player"]["hasPlacedActivePropThisBattle"]) return;
                    if (this["Uq"]) return;
                    this["Uq"] = !0, this["V$"] || (this["V$"] = new Laya["Image"]("resources/img/battleUI/hand.png"), this["V$"]["size"](d[66], d[72]), this["effectBox"]["addChild"](this["V$"]), this["V$"]["zIndex"] = t1["Tr"]), this["V$"]["visible"] = !0, Laya["Point"]["TEMP"]["x"] = a, Laya["Point"]["TEMP"]["y"] = b, this["effectBox"]["globalToLocal"](Laya["Point"]["TEMP"]);
                    w = Laya["Point"]["TEMP"]["x"], y = Laya["Point"]["TEMP"]["y"];
                    this["V$"]["pos"](Laya["Point"]["TEMP"]["x"], Laya["Point"]["TEMP"]["y"]), Laya["Point"]["TEMP"]["x"] = this["map"]["width"] / 2, Laya["Point"]["TEMP"]["y"] = this["map"]["height"] * (3 / 4), this["map"]["localToGlobal"](Laya["Point"]["TEMP"]), this["effectBox"]["globalToLocal"](Laya["Point"]["TEMP"]);
                    v = Laya["Point"]["TEMP"]["x"], x = Laya["Point"]["TEMP"]["y"];
                    Laya["Tween"]["create"](this["V$"])["to"]("x", v)["to"]("y", x)["duration"](d[118])["then"](() => {
                      this["V$"]["pos"](w, y)
                    })["repeat"](2)["then"](() => {
                      this["V$"]["visible"] = !1, this["Uq"] = !1
                    })
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (8 == i) {
                Object["defineProperty"](c["prototype"], "Zq", {
                  ["value"]() {
                    var a = hr,
                      b = a[0],
                      c = a[2],
                      d = a[1],
                      e = "I$",
                      f = "end1",
                      g = "map",
                      h = "end2",
                      i = "zIndex",
                      j = "ADou",
                      k = "pos",
                      l = "sw",
                      m = "se",
                      n = "ye",
                      o = "gridHei",
                      p = "ee",
                      q = "he",
                      r = "ne";
                    this["I$"](this["end1"], this["map"]), this["I$"](this["end2"], this["map"]), this["end1"]["zIndex"] = t1["ADou"], this["end2"]["zIndex"] = t1["ADou"], this["h$"]["pos"](this["sw"]["map"]["se"]["x"] * this["sw"]["map"]["ye"] + this["sw"]["map"]["ye"] / 2, this["sw"]["map"]["se"]["y"] * this["sw"]["map"]["gridHei"] + this["sw"]["map"]["gridHei"]), this["e$"]["pos"](this["sw"]["map"]["ee"]["x"] * this["sw"]["map"]["ye"] + this["sw"]["map"]["ye"] / 2, this["sw"]["map"]["ee"]["y"] * this["sw"]["map"]["gridHei"] + this["sw"]["map"]["gridHei"]), this["end1"]["pos"](this["sw"]["map"]["he"]["x"] * this["sw"]["map"]["ye"], this["sw"]["map"]["he"]["y"] * this["sw"]["map"]["gridHei"]), this["end2"]["pos"](this["sw"]["map"]["ne"]["x"] * this["sw"]["map"]["ye"], this["sw"]["map"]["ne"]["y"] * this["sw"]["map"]["gridHei"])
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (9 == i) {
                Object["defineProperty"](c["prototype"], "P$", {
                  ["value"](a, b, c) {
                    var d = hr,
                      e = d[0];
                    let f;
                    f = c * this["sw"]["props"]["ta"] + b;
                    c < 1 ? this["q$"](a, f) : this["$$"](a, f)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (10 == i) {
                Object["defineProperty"](c["prototype"], "f$", {
                  ["value"](a, b) {
                    var c = hr,
                      d = hu,
                      e = c[1],
                      f = c[0],
                      g = c[4],
                      h = c[2],
                      i = c[3],
                      j = c[6],
                      k = d[167],
                      l = "sw",
                      m = "map",
                      n = "pe",
                      o = "length",
                      p = "instance",
                      q = "removeSelf",
                      r = "Image",
                      s = "size",
                      t = "pos",
                      u = "ye",
                      v = "gridHei",
                      w = "gameObjectBox",
                      x = "addChild",
                      y = "Vy",
                      z = "Qy",
                      A = "Tween",
                      B = "create",
                      C = "to",
                      D = "duration",
                      E = "parallel",
                      F = "chain",
                      G = "alpha",
                      H = "then",
                      I = "resources/img/gameObject/enemy/tree_",
                      J = "_0.png",
                      K = "Lq",
                      L = "get",
                      M = "skin",
                      N = "mapIndex";
                    let O, P, Q, R, S, T, U;
                    R = a ? "1_0" : "1_1", P = [];
                    for (let b = 0; b < this["sw"]["map"]["pe"]["length"]; b++)
                      for (let c = 0; c < this["sw"]["map"]["pe"][b]["length"]; c++) this["sw"]["map"]["pe"][b][c] == R && (vc["instance"]()["mE"](a, b, c) || P["push"]({
                        ["x"]: b,
                        ["y"]: c
                      }));
                    if (console["log"]("设置格子"), P["length"] <= 0) return void b["removeSelf"]();
                    T = P[np["range"](0, P["length"], !0)], U = new Laya["Image"]("resources/img/gameObject/enemy/vine2.png");
                    U["size"](d[302], d[202]), U["anchor"](.5, .5), U["pos"]((T["x"] + .5) * this["sw"]["map"]["ye"], (T["y"] + .5) * this["sw"]["map"]["gridHei"]), U["scale"](0, 0), this["gameObjectBox"]["addChild"](U);
                    O = T["x"] * this["sw"]["map"]["ye"], Q = T["y"] * this["sw"]["map"]["gridHei"];
                    this["Vy"]["x"] = b["x"], this["Vy"]["y"] = b["y"], this["Qy"]["x"] = O + this["sw"]["map"]["ye"] / 2, this["Qy"]["y"] = Q + this["sw"]["map"]["gridHei"] / 2;
                    S = np["bs"](this["Vy"], this["Qy"]);
                    Laya["Tween"]["create"](b)["to"]("x", this["Qy"]["x"])["to"]("y", this["Qy"]["y"])["duration"](S)["parallel"]()["to"]("rotation", S / 2)["chain"]()["to"]("alpha", 0)["duration"](d[81])["then"](() => {
                      b["removeSelf"]()
                    })["parallel"](U)["to"]("scaleX", 1)["to"]("scaleY", 1)["duration"](k)["chain"](U)["to"]("alpha", 0)["duration"](k)["then"](() => {
                      let b;
                      U["removeSelf"]();
                      b = new Laya["Image"]("resources/img/gameObject/enemy/tree_" + (a ? "0" : "1") + "_0.png");
                      b["name"] = "tree", b["size"](this["sw"]["map"]["ye"], this["sw"]["map"]["gridHei"]), b["pos"](T["x"] * this["sw"]["map"]["ye"], T["y"] * this["sw"]["map"]["gridHei"]), b["alpha"] = .7, this["gameObjectBox"]["addChild"](b), qs["instance"]()["Kf"](b, ["resources/img/gameObject/enemy/tree_" + (a ? "0" : "1") + "_0.png", "resources/img/gameObject/enemy/tree_" + (a ? "0" : "1") + "_1.png", "resources/img/gameObject/enemy/tree_" + (a ? "0" : "1") + "_2.png"], k, 0, 1, c => {
                        Laya["Tween"]["create"](b)["to"]("alpha", 0)["duration"](d[132])["then"](() => {
                          b["removeSelf"]()
                        }), a ? (this["Lq"]["get"](`${T["x"]}_${T["y"]}`)["skin"] = `resources/img/map/grass_${this["mapIndex"]}_0.png`, this["sw"]["map"]["pe"][T["x"]][T["y"]] = "2_0") : (this["Lq"]["get"](`${T["x"]}_${T["y"]}`)["skin"] = `resources/img/map/grass_${this["mapIndex"]}_1.png`, this["sw"]["map"]["pe"][T["x"]][T["y"]] = "2_1")
                      })
                    })
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[7] == i) {
                Object["defineProperty"](c["prototype"], "Yq", {
                  ["value"]() {
                    var a = hr,
                      b = a[1],
                      c = a[2],
                      d = a[0],
                      e = a[4],
                      f = a[5],
                      g = "sw",
                      h = "map",
                      i = "length",
                      j = "width",
                      k = "dq",
                      l = "push",
                      m = "name";
                    let n;
                    console["log"]("设置格子"), this["sw"]["Ty"] = this["map"];
                    n = this["sw"]["map"]["pe"];
                    this["map"]["size"](n["length"] * this["sw"]["map"]["ye"], n[0]["length"] * this["sw"]["map"]["gridHei"]), this["map"]["x"] = (this["width"] - this["map"]["width"]) / 2;
                    for (let c = 0; c < n["length"]; c++) {
                      this["dq"]["push"]([]);
                      for (let g = 0; g < n[c]["length"]; g++) {
                        let h;
                        h = rw["instance"]()["getItem"]("mapImg", this);
                        h["name"] = `${c}_${g}`, this["dq"][c]["push"](h), h["pos"](c * h["width"], g * h["height"]), this["Lq"]["set"](h["name"], h)
                      }
                    }
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              }
              if (d[9] == i) {
                Object["defineProperty"](c["prototype"], "hV", {
                  ["value"]() {
                    var a = hr,
                      b = a[3],
                      c = "bq";
                    Laya["timer"]["clear"](this, this["J$"]), this["bq"] && (Laya["Tween"]["killAll"](this["bq"]), this["bq"]["removeSelf"](), this["bq"] = null)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[3] == i) {
                Object["defineProperty"](c["prototype"], "T$", {
                  ["value"]() {
                    var a = hr,
                      b = a[0];
                    return this["pm"] || this["sw"]["au"]["Qi"]
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              }
              if (d[14] == i) {
                Object["defineProperty"](c["prototype"], "_$", {
                  ["value"](a, b, c) {
                    var d = hr,
                      e = hu,
                      f = d[2],
                      g = d[11],
                      h = d[29],
                      i = d[0],
                      j = d[4],
                      k = d[3],
                      l = "Vy",
                      m = "parent",
                      n = "Tween",
                      o = "to",
                      p = "U$",
                      rfb = "alpha",
                      q = "Handler",
                      r = "create",
                      s = "skin";
                    let t;
                    t = a ? this["end1"] : this["end2"];
                    this["Vy"]["x"] = t["x"], this["Vy"]["y"] = t["y"], this["Vy"] = t["parent"]["localToGlobal"](this["Vy"]), this["Vy"] = b["parent"]["globalToLocal"](this["Vy"]), a && Laya["Tween"]["to"](this["U$"], {
                      ["alpha"]: 1
                    }, e[81]), Laya["Tween"]["to"](b, {
                      ["x"]: this["Vy"]["x"],
                      ["y"]: this["Vy"]["y"],
                      ["rotation"]: e[303]
                    }, e[176], null, Laya["Handler"]["create"](this, () => {
                      this["U$"]["skin"] = "resources/img/battleUI/eat2.png", b["visible"] = !1, a && Laya["Tween"]["to"](this["U$"], {
                        ["alpha"]: 0
                      }, e[167], null, Laya["Handler"]["create"](this, () => {
                        this["U$"]["skin"] = "resources/img/battleUI/eat1.png"
                      })), c()
                    }))
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[12] == i) {
                Object["defineProperty"](c["prototype"], "F$", {
                  ["value"](a, b) {
                    var c = hr,
                      d = c[0],
                      e = c[2],
                      f = "Aq",
                      g = "Bq",
                      h = "range",
                      i = "play",
                      j = "dou",
                      k = "zhan",
                      l = "Eq",
                      m = "Dq";
                    b ? (this["Aq"] += 1, this["Aq"] > this["Bq"] ? (this["Aq"] = 0, this["Bq"] = np["range"](3, 7, !0), a["play"]("dou", !1)) : a["play"]("zhan", !1)) : (this["Eq"] += 1, this["Eq"] > this["Dq"] ? (this["Eq"] = 0, this["Dq"] = np["range"](3, 7, !0), a["play"]("dou", !1)) : a["play"]("zhan", !1))
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[11] == i) {
                Object["defineProperty"](c["prototype"], "k$", {
                  ["value"](a) {
                    var b = hr,
                      c = hu,
                      d = b[0],
                      e = b[3],
                      f = b[5],
                      g = b[6],
                      h = b[4],
                      i = c[123],
                      j = c[132],
                      k = c[175],
                      l = "Cq",
                      m = "timer",
                      n = "once",
                      o = "danger0",
                      p = "scale",
                      q = "danger1",
                      r = "instance",
                      s = "playSound",
                      t = "danger_tip",
                      u = "alpha",
                      v = "Tween",
                      w = "create",
                      x = "to",
                      y = "scaleX",
                      z = "scaleY",
                      A = "duration",
                      B = "chain",
                      C = "Tq",
                      D = "danger2",
                      E = "danger3",
                      F = "Rq",
                      G = "sw";
                    if (a) {
                      if (this["w$"](!0, 1), this["Cq"]) return;
                      this["Cq"] = !0, Laya["timer"]["once"](i, this, () => {
                        this["danger0"]["scale"](1, 1), this["danger1"]["scale"](1, 1), this["Cq"] = !1
                      }), pC["instance"]()["playSound"]("danger_tip"), this["danger0"]["alpha"] = .8, this["danger1"]["alpha"] = .8, Laya["Tween"]["create"](this["danger0"])["to"]("scaleX", 1.1)["to"]("scaleY", 1.1)["to"]("alpha", 1)["duration"](j)["chain"]()["to"]("scaleX", 1.2)["to"]("scaleY", 1.2)["duration"](j)["chain"]()["to"]("scaleX", 1.3)["to"]("scaleY", 1.3)["to"]("alpha", 0)["duration"](j), Laya["Tween"]["create"](this["danger1"])["to"]("scaleX", 1.4)["to"]("scaleY", 1.4)["to"]("alpha", .8)["duration"](j)["chain"]()["to"]("scaleX", 2.2)["to"]("scaleY", 2.2)["to"]("alpha", 0)["duration"](k)
                    } else {
                      if (this["Tq"]) return;
                      this["Tq"] = !0, Laya["timer"]["once"](i, this, () => {
                        this["danger2"]["scale"](1, 1), this["danger3"]["scale"](1, 1), this["Tq"] = !1
                      }), pC["instance"]()["playSound"]("danger_tip"), this["danger2"]["alpha"] = .8, this["danger3"]["alpha"] = .8, Laya["Tween"]["create"](this["danger2"])["to"]("scaleX", 1.1)["to"]("scaleY", 1.1)["to"]("alpha", 1)["duration"](j)["chain"]()["to"]("scaleX", 1.2)["to"]("scaleY", 1.2)["duration"](j)["chain"]()["to"]("scaleX", 1.3)["to"]("scaleY", 1.3)["to"]("alpha", 0)["duration"](j), Laya["Tween"]["create"](this["danger3"])["to"]("scaleX", 1.4)["to"]("scaleY", 1.4)["to"]("alpha", .8)["duration"](j)["chain"]()["to"]("scaleX", 2.2)["to"]("scaleY", 2.2)["to"]("alpha", 0)["duration"](k), vT["instance"]()["HG"] || this["Rq"] || vS["instance"]()["SG"] || (this["Rq"] = !0, Math["random"]() <= this["sw"]["My"]["ri"][this["sw"]["au"]["Si"]] && vS["instance"]()["XG"]())
                    }
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[13] == i) {
                Object["defineProperty"](c["prototype"], "C$", {
                  ["value"]() {
                    var a = hr,
                      c = hu,
                      d = a[0],
                      e = a[6],
                      f = a[5],
                      g = a[2],
                      h = a[3],
                      i = a[7],
                      j = a[4],
                      k = c[43],
                      l = "sw",
                      m = "map",
                      n = "ve",
                      o = "instance",
                      p = "refreshBox",
                      q = "fe",
                      r = "deckBtn",
                      s = "Tween",
                      t = "create",
                      u = "to",
                      v = "scaleX",
                      w = "scaleY",
                      x = "duration",
                      y = "chain",
                      z = "deckLight",
                      A = "then",
                      B = "timer",
                      C = "resources/img/battleUI/refreshLight0.png",
                      D = "skin";
                    let E, F, G, H, I;
                    this["sw"]["map"]["ve"] && (this["shovelAd"]["visible"] = !1, this["sw"]["map"]["ve"] = !1), this["goldNumTxt"]["text"] = this["sw"]["au"]["fi"]["toString"](), oc["instance"]["event"](sS["Dt"]), qs["instance"]()["wf"]();
                    H = this["refreshBox"]["width"] / this["sw"]["map"]["fe"], E = this["refreshBox"]["height"], I = !1, F = this["deckBtn"]["x"], G = this["deckBtn"]["y"];
                    pC["instance"]()["playSound"]("open_deck"), Laya["Tween"]["create"](this["deckBtn"])["to"]("scaleX", 1.2)["to"]("scaleY", .8)["duration"](c[1])["chain"]()["to"]("scaleX", .8)["to"]("scaleY", 1.2)["duration"](10)["chain"]()["to"]("scaleX", 1)["to"]("scaleY", 1)["duration"](k)["parallel"](this["roof"])["to"]("rotation", -c[304])["duration"](k)["onStart"](() => {
                      this["deckLight"]["scale"](0, 0), Laya["Tween"]["create"](this["deckLight"])["to"]("scaleX", 1)["to"]("scaleY", 1)["duration"](c[81])["then"](() => {
                        let b;
                        b = () => {
                          I ? Laya["timer"]["clear"](this, b) : "resources/img/battleUI/refreshLight0.png" == this["deckLight"]["skin"] ? this["deckLight"]["skin"] = "resources/img/battleUI/refreshLight1.png" : this["deckLight"]["skin"] = "resources/img/battleUI/refreshLight0.png"
                        };
                        Laya["timer"]["loop"](c[132], this, b)
                      }, this)
                    }, this)["then"](() => {
                      var a = hr,
                        c = hu;
                      for (let d = 0; d < this["sw"]["map"]["fe"]; d++) Laya["timer"]["once"](c[81] * d, this, () => {
                        var a = hr,
                          c = hu,
                          e = a[0],
                          f = a[6],
                          g = a[3],
                          h = a[5],
                          i = a[4],
                          j = a[30],
                          k = a[7],
                          l = c[81],
                          m = "sw",
                          n = "map",
                          o = "fe",
                          p = "instance",
                          q = "iH",
                          r = "timer",
                          s = "once",
                          t = "Qy",
                          u = "localToGlobal",
                          v = "Vy",
                          w = "T$",
                          x = "indexOf",
                          y = "release",
                          z = "to",
                          A = "duration";
                        let B, C, D, J, K, L, M, N, O;
                        w1_ei: for (let P of b) {
                          switch (P) {
                            case 0:
                              if (!O) return void(d == this["sw"]["map"]["fe"] - 1 && (vT["instance"]()["iH"](), Laya["timer"]["once"](l, this, () => {
                                r0["instance"]()["PY"]()
                              })));
                              break;
                            case 1:
                              this["Qy"]["x"] = H * D + H / 2, this["Qy"]["y"] = E / 2, this["refreshBox"]["localToGlobal"](this["Qy"]);
                              break;
                            case 2:
                              C = pR["instance"](), O = C["oP"](!0, d);
                              break;
                            case 3:
                              N = this["sw"]["Oc"];
                              break;
                            case 4:
                              K = vT["instance"]()["tH"](d), J = "#ffffff";
                              break;
                            case 5:
                              void({
                                ["index"]: D,
                                ["token"]: M
                              } = O);
                              break;
                            case 6:
                              if (this["Vy"]["x"] = F, this["Vy"]["y"] = G - this["sw"]["map"]["gridHei"] / 2, this["deckBtn"]["parent"]["localToGlobal"](this["Vy"]), this["T$"]()) return;
                              break;
                            case 7:
                              if (-1 == L) {
                                B = `resources/img/gameObject/soldier/generalParts_${N["lp"]["indexOf"](K)}.png`
                              } else B = `resources/img/gameObject/soldier/soldier_${L}.png`;
                              break;
                            case 8:
                              "铲" != K && "刀" != K && "弓" != K && "枪" != K && "骑" != K && (J = "#f8e37d");
                              break;
                            case 9:
                              "铲" == K && (B = uq["instance"]()["au"]["Hi"] ? "resources/img/props/shovel_2.png" : "resources/img/props/shovel_1.png"), "农" == K && (B = "resources/img/props/farmer_1.png"), pC["instance"]()["playSound"]("soldier_create"), qs["instance"]()["vg"](this["Vy"]["x"], this["Vy"]["y"], this["Qy"]["x"], this["Qy"]["y"], c[176], () => {
                                var a = hr;
                                if (this["T$"]()) C["release"](M);
                                else try {
                                  sO["instance"]()["refresh"](K, D), d == this["sw"]["map"]["fe"] - 1 && (vT["instance"]()["iH"](), Laya["timer"]["once"](l, this, () => {
                                    r0["instance"]()["PY"]()
                                  }))
                                } finally {
                                  C["release"](M)
                                }
                              }, J, B), d == this["sw"]["map"]["fe"] - 1 && (I = !0, Laya["Tween"]["create"](this["roof"])["to"]("rotation", 0)["duration"](c[167])["parallel"](this["deckLight"])["to"]("scaleX", 0)["to"]("scaleY", 0)["duration"](l));
                              break;
                            case 10:
                              L = N["op"]["indexOf"](K);
                              break;
                            default:
                              break
                          }
                        }
                      })
                    }, this)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[5] == i) {
                Object["defineProperty"](c["prototype"], "b$", {
                  ["value"](a, b, c) {
                    var d = hr,
                      e = d[0];
                    a && this["G$"](b, c) || this["W$"](a, b, c)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[4] == i) {
                Object["defineProperty"](c["prototype"], "onClosed", {
                  ["value"](a) {
                    var b = hr,
                      c = b[0],
                      d = "offAllCaller";
                    nx["instance"]()["wa"]("BattleScene"), this["a$"]["offAllCaller"](this), this["n$"]["offAllCaller"](this)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[1] == i) {
                Object["defineProperty"](c["prototype"], "X$", {
                  ["value"]() {
                    var a = hr,
                      b = hu,
                      c = a[2],
                      d = a[6],
                      e = a[3],
                      f = a[5],
                      g = a[0],
                      h = b[1],
                      i = "getChildByName",
                      j = "heartBox",
                      k = "Tween",
                      l = "killAll",
                      m = "a$",
                      n = "offAll",
                      o = "stop",
                      p = "rotation",
                      q = "numChildren",
                      r = "getChildAt",
                      s = "skin",
                      t = "resources/img/battleUI/heart1.png",
                      u = "scale",
                      v = "alpha",
                      w = "visible",
                      x = "n$",
                      y = "U$";
                    let z, A, B;
                    A = this["end1"]["getChildByName"]("heartBox");
                    Laya["Tween"]["killAll"](this["a$"]), this["a$"]["offAll"](), this["a$"]["stop"](), this["a$"]["rotation"] = 0;
                    for (let a = 0; a < A["numChildren"]; a++) {
                      z = A["getChildAt"](a), z["skin"] = "resources/img/battleUI/heart1.png", z["scale"](1, 1);
                      for (let a = 0; a < z["numChildren"]; a++) B = z["getChildAt"](a), Laya["Tween"]["killAll"](B), B["y"] = h, B["rotation"] = 0, B["alpha"] = 1, B["visible"] = !1
                    }
                    A = this["end2"]["getChildByName"]("heartBox"), Laya["Tween"]["killAll"](this["n$"]), this["n$"]["offAll"](), this["n$"]["stop"](), this["n$"]["rotation"] = 0;
                    for (let a = 0; a < A["numChildren"]; a++) {
                      z = A["getChildAt"](a), z["skin"] = "resources/img/battleUI/heart1.png", z["scale"](1, 1);
                      for (let a = 0; a < z["numChildren"]; a++) B = z["getChildAt"](a), Laya["Tween"]["killAll"](B), B["y"] = h, B["rotation"] = 0, B["alpha"] = 1, B["visible"] = !1
                    }
                    this["U$"]["skin"] = "resources/img/battleUI/eat1.png", this["U$"]["alpha"] = 0
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[10] == i) {
                Object["defineProperty"](c["prototype"], "i$", {
                  ["value"](a, b) {
                    var c = hr,
                      d = hu,
                      e = c[3],
                      f = d[260],
                      g = "numChildren",
                      h = "getChildAt",
                      rhe = "alpha",
                      i = "Tween",
                      j = "to";
                    for (let e = 0; e < a["numChildren"]; e++) Laya["timer"]["once"](d[175] * e, this, () => {
                      a["getChildAt"](e)["alpha"] = 1, e == a["numChildren"] - 1 ? Laya["Tween"]["to"](a["getChildAt"](e), {
                        ["alpha"]: 0
                      }, f, null, Laya["Handler"]["create"](this, () => {
                        (b += 1) <= 5 && this["i$"](a, b)
                      })) : Laya["Tween"]["to"](a["getChildAt"](e), {
                        ["alpha"]: 0
                      }, f)
                    })
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[2] == i) {
                Object["defineProperty"](c["prototype"], "rw", {
                  ["value"](a) {
                    var b = hr,
                      c = b[0],
                      d = "ie",
                      e = "ae",
                      f = "sw",
                      g = "map",
                      h = "Point",
                      i = "se",
                      j = "ee",
                      k = "ye",
                      l = "gridHei";
                    let m, n, o;
                    m = a ? this["Fq"] : this["Oq"], o = a ? this["ie"] : this["ae"];
                    n = this["sw"]["map"];
                    m || (m = new Laya["Point"], o = new Laya["Point"]), m["x"] = (a ? n["se"]["x"] : n["ee"]["x"]) * n["ye"] + this["sw"]["map"]["ye"] / 2, m["y"] = (a ? n["se"]["y"] : n["ee"]["y"]) * n["ye"] + this["sw"]["map"]["gridHei"] / 2, o["x"] = (a ? n["ie"]["x"] : n["ae"]["x"]) * n["ye"] + this["sw"]["map"]["ye"] / 2, o["y"] = (a ? n["ie"]["y"] : n["ae"]["y"]) * n["ye"] + this["sw"]["map"]["gridHei"]
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[8] == i) {
                Object["defineProperty"](c["prototype"], "m$", {
                  ["value"](a, b) {
                    var c = hr;
                    this["effectBox"]["addChild"](a), null != b && (a["zIndex"] = b)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[6] == i) {
                Object["defineProperty"](c["prototype"], "Jq", {
                  ["value"](a) {
                    var b = hr,
                      c = hu,
                      d = b[0],
                      e = b[2],
                      f = b[5],
                      g = b[6],
                      h = b[3],
                      i = c[1],
                      j = "sw",
                      k = "au",
                      l = "getChildByName",
                      m = "heartBox",
                      n = "numChildren",
                      o = "instance",
                      p = "heart",
                      q = "getChildAt",
                      r = "Tween",
                      s = "killAll",
                      t = "visible",
                      u = "rotation",
                      v = "pos",
                      w = "alpha";
                    let x, y, z, A, B, C, D;
                    y = a ? this["sw"]["au"]["Zi"] : this["sw"]["au"]["Ki"], z = a ? this["end1"]["getChildByName"]("heartBox") : this["end2"]["getChildByName"]("heartBox"), x = z["numChildren"];
                    if (y > x)
                      for (let a = 0; a < y - x; a++) C = rw["instance"]()["getItem"]("heart", this), z["addChild"](C);
                    else if (y < x)
                      for (let a = 0; a < x - y; a++) C = z["getChildAt"](z["numChildren"] - 1), C["removeSelf"](), C["skin"] = "resources/img/battleUI/heart1.png", B = C["getChildAt"](0), A = C["getChildAt"](1), Laya["Tween"]["killAll"](B), B["visible"] = !1, B["rotation"] = 0, B["pos"](c[14], i), B["alpha"] = 1, Laya["Tween"]["killAll"](A), A["visible"] = !1, A["rotation"] = 0, A["pos"](c[7], i), A["alpha"] = 1, rw["instance"]()["recover"]("heart", C);
                    D = Math["ceil"](z["numChildren"] / 3);
                    x = z["numChildren"];
                    for (let a = 0; a < x; a++) C = z["getChildAt"](a), C["visible"] = y > a, C["x"] = 10 + a % 3 * c[57], C["y"] = -i * (D - Math["floor"](a / 3) - 1) - c[10]
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[19] == i) {
                Object["defineProperty"](c["prototype"], "u$", {
                  ["value"](a, b, c, d, e) {
                    var f = hr,
                      g = f[0],
                      h = f[2],
                      i = f[6],
                      j = "propsBox",
                      k = "numChildren",
                      l = "propsBoxAi",
                      m = "log",
                      n = "visible",
                      o = "width",
                      p = "height",
                      q = "addChild";
                    let r, s, t;
                    let u;
                    s = a ? this["propsBox"]["numChildren"] : this["propsBoxAi"]["numChildren"], t = a ? this["propsBox"] : this["propsBoxAi"];
                    r = e * this["sw"]["props"]["ta"] + d;
                    r < s ? (u = t["getChildAt"](r), u["skin"] = b ? "resources/img/props/activePropsBgNew.png" : "resources/img/props/passivePropsBgNew.png", console["log"]("展示道具", a), u["visible"] = !0, c["pos"]((u["width"] - c["width"]) / 2, (u["height"] - c["height"]) / 2), u["addChild"](c)) : (console["log"]("道具已满", a), c["visible"] = !1, t["addChild"](c))
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[57] == i) {
                Object["defineProperty"](c["prototype"], "$q", {
                  ["value"]() {
                    var a = hr,
                      b = a[0],
                      c = a[1],
                      d = a[6],
                      e = a[4],
                      f = a[2],
                      g = a[8],
                      h = "D$",
                      i = "jq",
                      j = "mapIndex",
                      k = "skin",
                      l = "mapBgImg",
                      m = "visible",
                      n = "mapBgImgNew",
                      o = "refreshBox",
                      p = "resources/img/map/refresh_";
                    this["D$"] = this["jq"]["get"](this["mapIndex"]), this["D$"] || (this["D$"] = ut["instance"]()["zy"](us["mapBg" + this["mapIndex"]])["create"](), this["jq"]["set"](this["mapIndex"], this["D$"])), this["D$"]["zIndex"] = t1["$n"], this["box"]["addChild"](this["D$"]), this["mapTitle"]["skin"] = "resources/img/map/mapBg/mapBg" + this["mapIndex"] + "/title.png", 0 == this["sw"]["map"]["mapIndex"] ? (this["mapBgImg"]["visible"] = !0, this["mapBgImgNew"]["visible"] = !1) : (this["mapBgImg"]["visible"] = !1, this["mapBgImgNew"]["visible"] = !0, this["mapBgImgNew"]["skin"] = "resources/img/map/mapBg_" + this["mapIndex"] + ".png");
                    for (let b = 0; b < this["refreshBox"]["numChildren"]; b++) {
                      let a;
                      a = this["refreshBox"]["getChildAt"](b);
                      "grid" == a["name"] && (a["skin"] = 0 == b ? "resources/img/map/refresh_" + this["mapIndex"] + "_0.png" : "resources/img/map/refresh_" + this["mapIndex"] + "_1.png")
                    }
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[37] == i) {
                Object["defineProperty"](c["prototype"], "t$", {
                  ["value"](a) {
                    var b = hr,
                      c = hu,
                      d = b[2],
                      e = b[0],
                      f = b[4],
                      g = b[3],
                      h = b[5],
                      i = b[1],
                      j = b[6],
                      k = c[167],
                      l = c[81],
                      m = "anchorX",
                      n = "anchorY",
                      o = "pos",
                      p = "Tween",
                      q = "create",
                      r = "to",
                      s = "scaleX",
                      t = "scaleY",
                      u = "duration",
                      v = "then",
                      w = "instance",
                      x = "map",
                      y = "gridHei";
                    let z, A, B, C;
                    z = a ? this["end1"] : this["end2"], B = a ? this["a$"] : this["n$"];
                    this["S$"](a);
                    C = a ? this["Fq"] : this["Oq"], A = a ? this["ie"] : this["ae"];
                    B["play"]("attack", !0), z["anchorX"] = .5, z["anchorY"] = 1, z["scale"](0, 0), z["pos"](C["x"], C["y"]), z["visible"] = !0, Laya["Tween"]["create"](z)["to"]("scaleX", 1)["to"]("scaleY", 1)["duration"](k)["then"](() => {
                      let b;
                      b = C["y"] > A["y"] ? z["y"] - uq["instance"]()["map"]["gridHei"] / 4 : z["y"] + uq["instance"]()["map"]["gridHei"] * (3 / 4);
                      Laya["Tween"]["create"](z)["to"]("y", b)["to"]("scaleX", 1.2)["to"]("scaleY", 1.2)["duration"](k)["then"](() => {
                        let b;
                        b = C["y"] > A["y"] ? z["y"] - uq["instance"]()["map"]["gridHei"] / 4 : z["y"] + uq["instance"]()["map"]["gridHei"] * (3 / 4);
                        Laya["Tween"]["create"](z)["to"]("y", b)["to"]("scaleX", 1)["to"]("scaleY", 1)["duration"](k)["then"](() => {
                          Laya["Tween"]["create"](z)["to"]("scaleX", 1.1)["to"]("scaleY", .9)["duration"](l)["then"](() => {
                            Laya["Tween"]["create"](z)["to"]("scaleX", 1)["to"]("scaleY", 1)["duration"](l)["then"](() => {
                              z["anchorX"] = 0, z["anchorY"] = 0, z["pos"](z["x"] - z["width"] / 2, z["y"] - z["height"]), this["K$"](a)
                            })
                          })
                        })
                      })
                    })
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[23] == i) {
                Object["defineProperty"](c["prototype"], "l$", {
                  ["value"](a, b, c) {
                    var d = hr,
                      e = "sw",
                      f = "map";
                    this["road"]["addChild"](a), void 0 !== b && void 0 !== c && a["pos"](b * this["sw"]["map"]["ye"], c * this["sw"]["map"]["gridHei"])
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              }
              if (d[46] == i) {
                Object["defineProperty"](c["prototype"], "Wq", {
                  ["value"]() {
                    var a = hr,
                      b = hu,
                      c = a[3],
                      d = a[6],
                      e = a[4],
                      f = b[45],
                      g = b[81],
                      h = "shovelAd",
                      i = "to",
                      j = "scaleX",
                      k = "scaleY",
                      l = "duration",
                      m = "chain";
                    let n;
                    n = this["shovelAd"]["y"];
                    Laya["Tween"]["create"](this["shovelAd"])["to"]("scaleX", 1.2)["to"]("scaleY", .8)["duration"](f)["delay"](b[135])["chain"]()["to"]("scaleX", .8)["to"]("scaleY", 1.2)["to"]("y", n - b[22])["duration"](g)["chain"]()["to"]("scaleX", .9)["to"]("scaleY", 1.1)["to"]("y", n)["duration"](b[65])["chain"]()["to"]("scaleX", 1.1)["to"]("scaleY", .9)["duration"](f)["chain"]()["to"]("scaleX", .9)["to"]("scaleY", 1.1)["to"]("y", n - b[12])["duration"](g)["chain"]()["to"]("scaleX", .95)["to"]("scaleY", 1.15)["to"]("y", n)["duration"](b[61])["chain"]()["to"]("scaleX", 1)["to"]("scaleY", 1)["duration"](f)["then"](() => {
                      this["Wq"]()
                    }, this)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[22] == i) {
                Object["defineProperty"](c["prototype"], "H$", {
                  ["value"]() {
                    var a = hr,
                      b = "pathTip0",
                      c = "numChildren",
                      d = "getChildAt",
                      e = "alpha";
                    for (let a = 0; a < this["pathTip0"]["numChildren"]; a++) this["pathTip0"]["getChildAt"](0)["alpha"] = 0;
                    for (let f = 0; f < this["pathTip0"]["numChildren"]; f++) this["pathTip1"]["getChildAt"](0)["alpha"] = 0
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[36] == i) {
                Object["defineProperty"](c["prototype"], "Qq", {
                  ["value"]() {
                    var a = hr,
                      b = a[0],
                      c = a[1],
                      d = a[3],
                      e = a[6],
                      f = a[4],
                      g = "bound",
                      h = "graphics",
                      i = "sw",
                      j = "map",
                      k = "length",
                      l = "0_0",
                      m = "0_1",
                      n = "drawLine",
                      o = "#000000",
                      p = "width",
                      q = "height";
                    let r, s, t;
                    this["bound"]["graphics"]["clear"]();
                    r = this["sw"]["map"]["pe"], t = this["sw"]["map"]["ye"], s = this["sw"]["map"]["gridHei"];
                    for (let a = 0; a < r["length"]; a++)
                      for (let b = 0; b < r[a]["length"]; b++) {
                        let d;
                        d = r[a][b];
                        "1_0" != d && "1_1" != d && "2_0" != d && "2_1" != d || (a - 1 >= 0 && ("0_0" == r[a - 1][b] || "0_1" == r[a - 1][b]) && this["bound"]["graphics"]["drawLine"](a * t, b * s, a * t, (b + 1) * s, "#000000", 3), a + 1 < r["length"] && ("0_0" == r[a + 1][b] || "0_1" == r[a + 1][b]) && this["bound"]["graphics"]["drawLine"]((a + 1) * t, b * s, (a + 1) * t, (b + 1) * s, "#000000", 3), b - 1 >= 0 && ("0_0" == r[a][b - 1] || "0_1" == r[a][b - 1]) && this["bound"]["graphics"]["drawLine"](a * t, b * s, (a + 1) * t, b * s, "#000000", 3), b + 1 < r[0]["length"] && ("0_0" == r[a][b + 1] || "0_1" == r[a][b + 1]) && this["bound"]["graphics"]["drawLine"](a * t, (b + 1) * s, (a + 1) * t, (b + 1) * s, "#000000", 3))
                      }
                    this["bound"]["graphics"]["drawLine"](0, 0, this["bound"]["width"], 0, "#000000", 6), this["bound"]["graphics"]["drawLine"](0, this["bound"]["height"], this["bound"]["width"], this["bound"]["height"], "#000000", 6), this["bound"]["graphics"]["drawLine"](0, 0, 0, this["bound"]["height"], "#000000", 6), this["bound"]["graphics"]["drawLine"](this["bound"]["width"], 0, this["bound"]["width"], this["bound"]["height"], "#000000", 6)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[31] == i) {
                Object["defineProperty"](c["prototype"], "B$", {
                  ["value"](a) {
                    var b = hr,
                      c = hu,
                      d = "shovelAdBg",
                      e = "alpha",
                      f = "Iq",
                      g = "adLight",
                      h = "rotation";
                    this["shovelAdBg"]["alpha"] >= 1 && (this["Iq"] = -1), this["shovelAdBg"]["alpha"] <= 0 && (this["Iq"] = 1), this["shovelAdBg"]["alpha"] += this["Iq"] * a / c[167], this["adLight"]["rotation"] += 1, this["adLight"]["rotation"] >= c[97] && (this["adLight"]["rotation"] = 0)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[41] == i) {
                Object["defineProperty"](c["prototype"], "J$", {
                  ["value"]() {
                    var a = hr,
                      b = a[4],
                      c = a[0],
                      d = "bq",
                      e = "Mq",
                      f = "to";
                    let g;
                    if (!this["bq"]) return;
                    g = this["Mq"] ? 1.1 : 1;
                    this["Mq"] = !this["Mq"], Laya["Tween"]["create"](this["bq"])["to"]("scaleX", g)["to"]("scaleY", g)["duration"](hu[288])
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[15] == i) {
                Object["defineProperty"](c["prototype"], "gameOver", {
                  ["value"]() {
                    var a = hr,
                      b = a[0],
                      c = a[6],
                      d = a[3],
                      e = a[2],
                      f = a[9],
                      g = a[5],
                      h = a[8],
                      i = "instance",
                      j = "BattleScene",
                      k = "visible",
                      l = "Tween",
                      m = "killAll",
                      n = "deckBtn",
                      o = "roof",
                      p = "deckLight",
                      q = "scale",
                      r = "propsBox",
                      s = "numChildren",
                      t = "bg",
                      u = "getChildAt",
                      v = "name",
                      w = "propsBoxAi",
                      x = "sw",
                      y = "props",
                      z = "P$",
                      A = "V$";
                    this["pm"] = !0, pR["instance"]()["clear"](), nx["instance"]()["wa"]("BattleScene"), Laya["timer"]["clearAll"](this), this["X$"](), this["shovelAd"]["visible"] = !1, this["Rq"] = !1, Laya["Tween"]["killAll"](this["deckBtn"]), Laya["Tween"]["killAll"](this["roof"]), Laya["Tween"]["killAll"](this["deckLight"]), this["deckBtn"]["scale"](1, 1), this["roof"]["rotation"] = 0, this["deckLight"]["scale"](0, 0), this["H$"](), this["vq"] = 0, qs["instance"]()["wf"](), tI["clearAllDeferredTrails"](), this["D$"]["removeSelf"]();
                    for (let a = 0; a < this["propsBox"]["numChildren"]; a++) "bg" == this["propsBox"]["getChildAt"](a)["name"] && (this["propsBox"]["getChildAt"](a)["visible"] = !1);
                    for (let a = 0; a < this["propsBoxAi"]["numChildren"]; a++) "bg" == this["propsBoxAi"]["getChildAt"](a)["name"] && (this["propsBoxAi"]["getChildAt"](a)["visible"] = !1);
                    sF["instance"]()["Pn"]("BattleScene", !1);
                    for (let a = 0; a < this["sw"]["props"]["ta"]; a++) this["P$"](!0, a, 0), this["P$"](!1, a, 0);
                    for (let a = 0; a < this["sw"]["props"]["sa"]; a++) this["P$"](!0, a, 1), this["P$"](!1, a, 1);
                    this["Cq"] = !1, this["Tq"] = !1, this["V$"] && (Laya["Tween"]["killAll"](this["V$"]), this["V$"]["visible"] = !1), this["hV"](), this["end1"]["visible"] = !1, this["end2"]["visible"] = !1, this["Uq"] = !1
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[21] == i) {
                Object["defineProperty"](c["prototype"], "o$", {
                  ["value"](a, b, c) {
                    var d = hr,
                      e = "sw",
                      f = "map";
                    this["gameObjectBox"]["addChild"](a), void 0 !== b && void 0 !== c && a["pos"](b * this["sw"]["map"]["ye"], c * this["sw"]["map"]["gridHei"])
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              }
              if (d[30] == i) {
                Object["defineProperty"](c["prototype"], "s$", {
                  ["value"]() {
                    var a = hr,
                      b = hu,
                      c = a[2],
                      d = a[3],
                      e = a[4],
                      f = a[6],
                      g = a[0],
                      h = a[5],
                      i = b[81],
                      j = "sw",
                      k = "au",
                      l = "goldNum",
                      m = "text",
                      n = "to",
                      o = "scaleX",
                      p = "scaleY",
                      q = "duration",
                      r = "refreshBtnMask",
                      s = "graphics",
                      t = "maskImg",
                      u = "alpha",
                      v = "Pq";
                    let w, x, y, z, A;
                    y = this["sw"]["au"]["gold"], A = this["sw"]["au"]["fi"], z = Number(this["goldNum"]["text"]) || 0;
                    y > z && z < A && y >= A && Laya["Tween"]["create"](this["refreshBtn"])["to"]("scaleX", 1.1)["to"]("scaleY", 1.1)["duration"](i)["chain"]()["to"]("scaleX", 1)["to"]("scaleY", 1)["duration"](i), this["refreshBtnMask"]["graphics"]["clear"]();
                    w = A > 0 ? Math["min"](1, y / A) : 1;
                    this["refreshBtnMask"]["graphics"]["drawRect"](0, 0, this["maskImg"]["width"] * w, this["maskImg"]["height"], "#fff"), this["goldNum"]["text"] = y["toFixed"](0);
                    x = y >= A;
                    x ? (this["maskImg"]["alpha"] = 1, this["Pq"] || pC["instance"]()["playSound"]("soldier_buy_enable")) : this["maskImg"]["alpha"] = .6, this["Pq"] = x
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[35] == i) {
                Object["defineProperty"](c["prototype"], "c$", {
                  ["value"](a, b) {
                    var c = hr,
                      d = c[0],
                      e = c[1],
                      f = "refreshBox",
                      g = "width",
                      h = "height";
                    if (this["refreshBox"]["addChild"](a), void 0 !== b) {
                      let c;
                      c = this["refreshBox"]["width"] / this["sw"]["map"]["fe"];
                      a["pos"](c * b + (c - a["width"]) / 2, (this["refreshBox"]["height"] - a["height"]) / 2)
                    }
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[32] == i) {
                Object["defineProperty"](c["prototype"], "r$", {
                  ["value"](a, b, c) {
                    var d = hr,
                      e = "map",
                      f = "sw";
                    this["map"]["addChild"](a), void 0 !== b && void 0 !== c && a["pos"](b * this["sw"]["map"]["ye"], c * this["sw"]["map"]["gridHei"])
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              }
              if (d[60] == i) {
                Object["defineProperty"](c["prototype"], "x$", {
                  ["value"](a, b, c, d) {
                    var e = hr;
                    b["pos"](c, d), this["highGround"]["addChild"](b)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[43] == i) {
                Object["defineProperty"](c["prototype"], "onAwake", {
                  ["value"]() {
                    var a = hr,
                      b = a[0],
                      c = a[6],
                      d = a[5],
                      e = a[2],
                      f = a[3],
                      g = a[4],
                      h = a[8],
                      i = "sw",
                      j = "instance",
                      k = "deckBtn",
                      l = "zIndex",
                      m = "Br",
                      n = "ADou",
                      o = "getChildAt",
                      p = "getChildByName",
                      q = "maskSp",
                      r = "Dr",
                      s = "shovelAd",
                      t = "on",
                      u = "Event",
                      v = "CLICK",
                      w = "refreshBtn",
                      x = "xBtn",
                      y = "bind",
                      z = "skin";
                    let A;
                    this["sw"] = uq["instance"](), this["deckBtn"]["zIndex"] = t1["Br"], this["end1"]["zIndex"] = t1["ADou"], this["end2"]["zIndex"] = t1["ADou"], this["map"]["zIndex"] = t1["Vn"], this["gameObjectBox"]["zIndex"] = t1["Nn"], this["round"]["zIndex"] = t1["Br"], this["effectBox"]["zIndex"] = t1["ar"], this["danger0"]["zIndex"] = t1["Br"], this["danger1"]["zIndex"] = t1["Br"], this["danger2"]["zIndex"] = t1["Br"], this["danger3"]["zIndex"] = t1["Br"];
                    for (let d = 0; d < this["sw"]["props"]["ta"]; d++) this["propsBox"]["getChildAt"](d)["getChildByName"]("maskSp")["zIndex"] = t1["Dr"], this["propsBoxAi"]["getChildAt"](d)["getChildByName"]("maskSp")["zIndex"] = t1["Dr"];
                    this["Yq"](), this["Xq"](), this["Gq"](), this["Hq"](), this["addEvent"](), this["Wq"](), this["jq"] = new Map, this["shovelAd"]["on"](Laya["Event"]["CLICK"], this, this["zq"]), this["refreshBtn"]["on"](Laya["Event"]["CLICK"], this, this["Nq"]), this["deckBtn"]["on"](Laya["Event"]["CLICK"], this, this["qq"]), this["xBtn"]["on"](Laya["Event"]["CLICK"], this, this["pause"]), qs["instance"]()["Tf"]([this["refreshBtn"], this["deckBtn"], this["xBtn"]]);
                    A = this["shovelAd"]["getChildByName"]("adImg");
                    sJ["instance"]()["bind"](4, A["skin"]), sJ["instance"]()["bind"](5, A["skin"])
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[44] == i) {
                Object["defineProperty"](c["prototype"], "update", {
                  ["value"](a) {
                    this["B$"](a)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[28] == i) {
                "use strict";
                continue
              } else if (d[56] == i) {
                Object["defineProperty"](c["prototype"], "A$", {
                  ["value"](a) {
                    var b = hr,
                      c = hu,
                      d = b[0],
                      e = b[3],
                      f = b[4],
                      g = "bq",
                      h = "Tween",
                      i = "scale",
                      j = "create",
                      k = "to",
                      l = "scaleX",
                      m = "scaleY",
                      n = "duration",
                      o = "then",
                      p = "tV",
                      q = "alpha";
                    if (a)
                      if (this["bq"]) {
                        let f;
                        f = this["bq"]["getChildAt"](0);
                        f && (f["text"] = a), Laya["timer"]["clear"](this, this["J$"]), Laya["Tween"]["killAll"](this["bq"]), this["bq"]["scale"](.85, .85), Laya["Tween"]["create"](this["bq"])["to"]("scaleX", 1.12)["to"]("scaleY", 1.12)["duration"](c[81])["chain"]()["to"]("scaleX", 1)["to"]("scaleY", 1)["duration"](c[65])["then"](() => {
                          this["tV"]()
                        }, this)
                      } else this["bq"] = this["sV"](a), this["addChild"](this["bq"]), this["bq"]["alpha"] = 0, this["bq"]["scale"](.4, .4), Laya["Tween"]["create"](this["bq"])["to"]("alpha", 1)["to"]("scaleX", 1)["to"]("scaleY", 1)["duration"](c[174])["then"](() => {
                        this["tV"]()
                      }, this);
                    else this["iV"]()
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[62] == i) {
                Object["defineProperty"](c["prototype"], "j$", {
                  ["value"](a, b, c) {
                    var d = hr,
                      e = hu,
                      f = d[4],
                      g = d[2],
                      h = d[5],
                      i = d[0],
                      j = d[3],
                      k = d[6],
                      l = d[8],
                      m = e[97],
                      n = e[118],
                      o = e[123],
                      p = "propsBox",
                      q = "propsBoxAi",
                      r = "getChildAt",
                      s = "getChildByName",
                      t = "graphics",
                      u = "visible",
                      v = "Point",
                      w = "TEMP",
                      x = "Tween",
                      y = "create",
                      z = "to",
                      A = "rotation",
                      B = "duration",
                      C = "then",
                      D = "repeat",
                      E = "alpha";
                    let F, G, H, I, J, K;
                    G = (a ? this["propsBox"] : this["propsBoxAi"])["getChildAt"](b)["getChildByName"]("maskSp");
                    if (G["graphics"]["cmds"][0]["startAngle"] = c, G["graphics"]["repaint"](), c < m) return;
                    I = (a ? this["propsBox"] : this["propsBoxAi"])["getChildAt"](b), F = I["getChildByName"]("tip1"), K = I["getChildByName"]("tip2"), J = I["getChildByName"]("bgLight");
                    I["getChildByName"]("bg")["gray"] = !1, F["visible"] = !0, K["visible"] = !0, J["visible"] = !0, a && (Laya["Point"]["TEMP"]["x"] = I["width"] / 2, Laya["Point"]["TEMP"]["y"] = I["height"] / 2, I["localToGlobal"](Laya["Point"]["TEMP"]), this["N$"](Laya["Point"]["TEMP"]["x"], Laya["Point"]["TEMP"]["y"])), console["log"]("主动道具冷却结束", b), Laya["Tween"]["create"](F)["to"]("rotation", m)["duration"](n)["then"](() => {
                      F["rotation"] = 0
                    })["repeat"](-1), Laya["Tween"]["create"](K)["to"]("rotation", m)["duration"](n)["then"](() => {
                      K["rotation"] = 0
                    })["repeat"](-1);
                    H = () => {
                      Laya["Tween"]["create"](J)["to"]("alpha", .5)["duration"](o)["chain"]()["to"]("alpha", 1)["duration"](o)["then"](H)
                    };
                    H()
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[59] == i) {
                Object["defineProperty"](c["prototype"], "Kq", {
                  ["value"](a, b, c) {
                    var d = hr,
                      e = d[4],
                      f = d[1],
                      g = d[0],
                      h = "pos",
                      i = "sw",
                      j = "map",
                      k = "ye",
                      l = "gridHei",
                      m = "scaleY";
                    c["x"] != b["x"] && (a["pos"](b["x"] * this["sw"]["map"]["ye"] + (c["x"] - b["x"]) * (a["width"] / 2), b["y"] * this["sw"]["map"]["gridHei"] + this["sw"]["map"]["gridHei"] / 2), c["x"] > b["x"] ? (a["scaleX"] = 1, a["x"] += this["sw"]["map"]["ye"]) : a["scaleY"] = -1), c["y"] != b["y"] && (a["pos"](b["x"] * this["sw"]["map"]["ye"] + this["sw"]["map"]["ye"] / 2, b["y"] * this["sw"]["map"]["gridHei"] + (c["y"] - b["y"]) * (this["pathTip0"]["height"] / 2)), c["y"] < b["y"] ? a["scaleY"] = 1 : (a["scaleY"] = -1, a["y"] += this["sw"]["map"]["gridHei"]))
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[58] == i) {
                Object["defineProperty"](c["prototype"], "$$", {
                  ["value"](a, b) {},
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[55] == i) {
                Object["defineProperty"](c["prototype"], "q$", {
                  ["value"](a, b) {
                    var c = hr,
                      d = c[8],
                      e = c[2],
                      f = c[5],
                      g = "getChildByName",
                      h = "Tween",
                      i = "killAll",
                      j = "visible",
                      k = "rotation";
                    let l, m, n, o;
                    n = (a ? this["propsBox"] : this["propsBoxAi"])["getChildAt"](b), l = n["getChildByName"]("tip1"), o = n["getChildByName"]("tip2"), m = n["getChildByName"]("bgLight");
                    Laya["Tween"]["killAll"](l), Laya["Tween"]["killAll"](o), Laya["Tween"]["killAll"](m), l["visible"] = !1, o["visible"] = !1, m["visible"] = !1, l["rotation"] = 0, o["rotation"] = 0, n["getChildByName"]("bg")["gray"] = !0
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[17] == i) {
                Object["defineProperty"](c["prototype"], "K$", {
                  ["value"](a) {
                    var b = hr,
                      c = hu,
                      d = b[2],
                      e = b[0],
                      f = b[10],
                      g = b[3],
                      h = b[4],
                      i = b[6],
                      j = c[123],
                      k = c[118],
                      l = c[88],
                      m = c[167],
                      n = "play",
                      o = "pao",
                      p = "enable",
                      q = "random",
                      r = "timer",
                      s = "once",
                      t = "range",
                      u = "to",
                      v = "rotation",
                      w = "duration",
                      x = "then";
                    let y, z, A;
                    A = a ? this["end1"] : this["end2"], z = a ? this["a$"] : this["n$"];
                    z["play"]("pao", !0);
                    y = A["getComponent"](tm);
                    y || (y = A["addComponent"](tm)), y["lq"](c[132]), y["oq"](a), y["rq"](null), y["uq"](() => {
                      this["Q$"](A), z["on"](Laya["Event"]["STOPPED"], this, this["F$"], [z, a]), z["play"]("attack", !1)
                    }), y["enable"](), Math["random"]() < .5 && Laya["timer"]["once"](np["range"](j, k, !0), this, () => {
                      this["v$"](a, "护驾！护驾！")
                    }), Math["random"]() < .5 && Laya["timer"]["once"](np["range"](k, c[135], !0), this, () => {
                      let a;
                      a = y["yq"]();
                      z["stop"](), Laya["Tween"]["create"](z)["to"]("rotation", a["x"] > 0 ? l : -l)["duration"](m)["then"](() => {
                        y["disable"]()
                      })["chain"]()["to"]("rotation", 0)["duration"](m)["delay"](j)["then"](() => {
                        y["enable"](), z["play"]("pao", !0)
                      })
                    })
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[26] == i) {
                Object["defineProperty"](c["prototype"], "Z$", {
                  ["value"](a) {
                    var b = hr,
                      c = b[4],
                      d = b[0];
                    a["zIndex"] = t1["entityZIndexFromPixelY"](a["y"] + a["height"], this["sw"]["map"]["gridHei"], t1["zn"]["ADou"])
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[45] == i) {
                Object["defineProperty"](c["prototype"], "Y$", {
                  ["value"](a, b) {
                    var c = hr,
                      d = hu,
                      e = c[4],
                      f = c[0],
                      g = c[3],
                      h = c[2],
                      i = "v$",
                      j = "Vy",
                      k = "sw",
                      l = "au";
                    let m;
                    Math["random"]() < .5 ? this["v$"](a, "快拿去招兵买马") : this["v$"](a, "我快不行了");
                    m = a ? this["end1"] : this["end2"];
                    this["Vy"]["x"] = m["x"], this["Vy"]["y"] = m["y"], m["parent"]["localToGlobal"](this["Vy"]);
                    for (let b = 0; b < 10; b++) Laya["timer"]["once"](d[81] * b, this, () => {
                      qs["instance"]()["fg"](this["Vy"]["x"], this["Vy"]["y"]), a ? this["sw"]["au"]["gold"] += 1 : this["sw"]["au"]["Ji"] += 1
                    })
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[54] == i) {
                Object["defineProperty"](c["prototype"], "Gq", {
                  ["value"]() {
                    var a = hr,
                      b = hu,
                      c = a[0],
                      d = a[3],
                      e = a[2],
                      f = a[5],
                      g = a[6],
                      h = a[4],
                      i = b[62],
                      j = b[2],
                      k = b[59],
                      l = b[24],
                      m = "range",
                      n = "U$",
                      o = "Image",
                      p = "name",
                      q = "anchorX",
                      r = "anchorY",
                      s = "end1",
                      t = "addChild",
                      u = "alpha",
                      v = "resources/img/battleUI/deckBtn2.png",
                      w = "shadow",
                      x = "size",
                      y = "pos",
                      z = "width",
                      A = "height",
                      B = "end2",
                      C = "a$",
                      D = "instance",
                      E = "aDou",
                      F = "n$",
                      G = "sk",
                      H = "visible",
                      I = "addComponent";
                    let J, K;
                    this["Bq"] = np["range"](3, 7, !0), this["Dq"] = np["range"](3, 7, !0), this["U$"] = new Laya["Image"], this["U$"]["name"] = "eat", this["U$"]["skin"] = "resources/img/battleUI/eat1.png", this["U$"]["anchorX"] = .5, this["U$"]["anchorY"] = .5, this["end1"]["addChild"](this["U$"]), this["U$"]["alpha"] = 0;
                    K = new Laya["Image"]("resources/img/battleUI/deckBtn2.png");
                    K["name"] = "shadow", K["size"](i, j), K["anchorX"] = .5, K["anchorY"] = 1, K["alpha"] = .5, K["pos"](this["end1"]["width"] / 2, this["end1"]["height"]), this["end1"]["addChild"](K);
                    J = new Laya["Image"]("resources/img/battleUI/deckBtn2.png");
                    J["name"] = "shadow", J["size"](i, j), J["anchorX"] = .5, J["anchorY"] = 1, J["alpha"] = .5, J["pos"](this["end2"]["width"] / 2, this["end2"]["height"]), this["end2"]["addChild"](J), this["a$"] = nz["instance"]()["$d"]("aDou"), this["n$"] = nz["instance"]()["$d"]("aDou"), this["a$"]["name"] = "sk", this["n$"]["name"] = "sk", this["a$"]["anchorX"] = .5, this["a$"]["anchorY"] = 1, this["a$"]["pos"](k, l), this["n$"]["anchorX"] = .5, this["n$"]["anchorY"] = 1, this["n$"]["pos"](k, l), this["end1"]["addChild"](this["a$"]), this["end2"]["addChild"](this["n$"]), this["end1"]["visible"] = !1, this["end2"]["visible"] = !1, this["end1"]["addComponent"](tm), this["end2"]["addComponent"](tm)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[53] == i) {
                Object["defineProperty"](c["prototype"], "onOpened", {
                  ["value"]() {
                    var a = hr,
                      b = a[0],
                      c = a[6],
                      d = a[3],
                      e = a[2],
                      f = a[4],
                      g = a[14],
                      h = a[7],
                      i = a[5],
                      j = "mapIndex",
                      k = "sw",
                      l = "map",
                      m = "Kq",
                      n = "pathTip0",
                      o = "pathTip1",
                      p = "Jq",
                      q = "rw",
                      r = "t$",
                      s = "text",
                      t = "au",
                      u = "fi",
                      v = "maskImg",
                      w = "i$",
                      x = "play",
                      y = "idle",
                      z = "instance",
                      A = "propsBox",
                      B = "visible",
                      C = "propsBoxAi";
                    let D;
                    switch (this["pm"] = !1, this["mapIndex"] = this["sw"]["map"]["mapIndex"], this["$q"](), this["Vq"](), this["Qq"](), this["Zq"](), this["Kq"](this["pathTip0"], this["sw"]["map"]["se"], this["sw"]["map"]["ie"]), this["Kq"](this["pathTip1"], this["sw"]["map"]["ee"], this["sw"]["map"]["ae"]), this["Jq"](!0), this["Jq"](!1), this["rw"](!0), this["rw"](!1), Laya["timer"]["once"](hu[123], this, () => {
                        this["t$"](!0), this["t$"](!1)
                      }), this["round"]["text"] = "第1波", this["goldNumTxt"]["text"] = this["sw"]["au"]["fi"]["toString"](), this["refreshBtnMask"]["graphics"]["drawRect"](0, 0, this["maskImg"]["width"], this["maskImg"]["height"], "#fff"), this["Pq"] = this["sw"]["au"]["gold"] >= this["sw"]["au"]["fi"], this["s$"](), this["i$"](this["pathTip0"], 0), this["i$"](this["pathTip1"], 0), this["h$"]["play"]("idle" + this["mapIndex"], !0), this["e$"]["play"]("idle" + this["mapIndex"], !0), this["mapIndex"]) {
                      case 0:
                      case 2:
                      default:
                        D = "bg_battleScene_0";
                        break;
                      case 1:
                      case 3:
                        D = "bg_battleScene_3"
                    }
                    pC["instance"]()["playMusic"](D), this["sw"]["player"]["openProps"] ? (this["propsBox"]["visible"] = !0, this["propsBoxAi"]["visible"] = !0) : (this["propsBox"]["visible"] = !1, this["propsBoxAi"]["visible"] = !1), nx["instance"]()["La"]("BattleScene", this, this["update"]), vb["instance"]()["kA"](!0)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[38] == i) {
                Object["defineProperty"](c["prototype"], "d$", {
                  ["value"](a) {},
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[52] == i) {
                Object["defineProperty"](c["prototype"], "pause", {
                  ["value"]() {
                    var a = hr;
                    sF["instance"]()["Bn"]("PauseDialog")
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[33] == i) {
                Object["defineProperty"](c["prototype"], "W$", {
                  ["value"](a, b, c, d = -1, e = -1) {
                    var f = hr,
                      g = hu,
                      h = f[1],
                      i = f[0],
                      j = f[30],
                      k = "sw",
                      l = "map",
                      m = "length",
                      n = "Point",
                      o = "TEMP",
                      p = "ye",
                      q = "gridHei",
                      r = "instance",
                      s = "au";
                    let t;
                    if (d < 0 || e < 0) {
                      let b, c;
                      c = a ? "2_0" : "2_1", b = this["sw"]["map"]["pe"];
                      t: for (let a = b["length"] - 1; a >= 0; a--)
                        for (let f = b[0]["length"] - 1; f >= 0; f--)
                          if (b[a][f] == c) {
                            d = a, e = f;
                            break t
                          }
                    }
                    if (d < 0 || e < 0) return !1;
                    Laya["Point"]["TEMP"]["x"] = this["sw"]["map"]["ye"] * d + this["sw"]["map"]["ye"] / 2, Laya["Point"]["TEMP"]["y"] = this["sw"]["map"]["gridHei"] * e + this["sw"]["map"]["gridHei"] / 2, this["map"]["localToGlobal"](Laya["Point"]["TEMP"]);
                    t = (a ? uq["instance"]()["au"]["Hi"] : uq["instance"]()["au"]["Wi"]) ? "resources/img/props/shovel_2.png" : "resources/img/props/shovel_1.png";
                    return qs["instance"]()["vg"](b, c, Laya["Point"]["TEMP"]["x"], Laya["Point"]["TEMP"]["y"], g[176], () => {
                      if (this["T$"]()) return !1;
                      this["p$"](a, d, e)
                    }, "#ffffff", t), !0
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[42] == i) {
                Object["defineProperty"](c["prototype"], "w$", {
                  ["value"](a, b, c) {
                    var d = hr,
                      e = d[0],
                      f = d[6],
                      g = d[5],
                      h = "sw",
                      i = "map",
                      j = "Se",
                      k = "shovelAd",
                      l = "visible",
                      m = "getChildByName",
                      n = "adImg",
                      o = "player",
                      p = "instance",
                      q = "setIcon",
                      r = "shovelAdImg",
                      s = "skin",
                      t = "shovelNum",
                      u = "ve";
                    if (0 == b) {
                      let b;
                      if (this["sw"]["map"]["we"]) return;
                      if (this["sw"]["map"]["Se"]) return;
                      this["shovelAd"]["visible"] = a;
                      b = this["shovelAd"]["getChildByName"]("adImg");
                      b["visible"] = this["sw"]["player"]["hasUsedFreeShovel"], b["visible"] && sJ["instance"]()["setIcon"](4, b), a && (this["shovelAdImg"]["skin"] = "resources/img/battleUI/ad/shovel.png", this["shovelNum"]["text"] = `x${c}`, this["shovelNum"]["visible"] = !0), this["sw"]["map"]["ve"] = a
                    } else {
                      let b;
                      if (this["sw"]["map"]["ke"]) return;
                      if (a && this["sw"]["map"]["Se"]) return;
                      this["shovelAd"]["visible"] = a;
                      b = this["shovelAd"]["getChildByName"]("adImg");
                      b["visible"] = this["sw"]["player"]["hasUsedFreeBulldozer"], b["visible"] && sJ["instance"]()["setIcon"](5, b), a && (this["shovelAdImg"]["skin"] = "resources/img/battleUI/ad/bulldozer.png", this["shovelNum"]["visible"] = !1), this["sw"]["map"]["Se"] = a, this["sw"]["map"]["ve"] && (this["sw"]["map"]["ve"] = !1)
                    }
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[25] == i) {
                Object["defineProperty"](c["prototype"], "tV", {
                  ["value"]() {
                    var a = hr,
                      b = a[0];
                    this["Mq"] = !0, Laya["timer"]["loop"](hu[216], this, this["J$"])
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[18] == i) {
                Object["defineProperty"](c["prototype"], "iV", {
                  ["value"]() {
                    var a = hr,
                      b = a[3],
                      c = a[4],
                      d = a[0],
                      e = "bq",
                      f = "Tween",
                      g = "to";
                    let h;
                    if (!this["bq"]) return;
                    Laya["timer"]["clear"](this, this["J$"]), Laya["Tween"]["killAll"](this["bq"]);
                    h = this["bq"];
                    this["bq"] = null, Laya["Tween"]["create"](h)["to"]("alpha", 0)["to"]("scaleX", .5)["to"]("scaleY", .5)["duration"](hu[95])["then"](() => {
                      h["removeSelf"]()
                    }, this)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[50] == i) {
                Object["defineProperty"](c["prototype"], "Q$", {
                  ["value"](a) {
                    var b = hr,
                      c = b[0];
                    this["I$"](a, this["gameObjectBox"]), this["Z$"](a)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[61] == i) {
                Object["defineProperty"](c["prototype"], "G$", {
                  ["value"](a, b, c = -1) {
                    var d = hr,
                      e = d[0],
                      f = d[1],
                      g = d[3],
                      h = d[30],
                      i = "instance",
                      j = "refreshBox",
                      k = "Qy",
                      l = "release";
                    let m, n, o, p, q, r, s;
                    s = pR["instance"](), n = s["oP"](!0, c);
                    if (!n) return console["log"]("没有可以放置铲子的位置"), !1;
                    void({
                      ["index"]: p,
                      ["token"]: m
                    } = n);
                    o = this["refreshBox"]["width"] / this["sw"]["map"]["fe"], r = this["refreshBox"]["height"];
                    this["Qy"]["x"] = o * p + o / 2, this["Qy"]["y"] = r / 2, this["refreshBox"]["localToGlobal"](this["Qy"]);
                    q = uq["instance"]()["au"]["Hi"] ? "resources/img/props/shovel_2.png" : "resources/img/props/shovel_1.png";
                    return qs["instance"]()["vg"](a, b, this["Qy"]["x"], this["Qy"]["y"], hu[176], () => {
                      if (this["T$"]()) return s["release"](m), !1;
                      try {
                        vb["instance"]()["_A"](!0, 0, 3, p)
                      } finally {
                        s["release"](m)
                      }
                    }, "#ffffff", q), !0
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[39] == i) {
                Object["defineProperty"](c["prototype"], "R$", {
                  ["value"](a) {
                    var b = hr,
                      c = hu,
                      d = b[3],
                      e = b[4],
                      f = c[176],
                      g = "to",
                      h = "scaleX",
                      i = "scaleY",
                      j = "duration";
                    Laya["Tween"]["create"](a)["to"]("scaleX", .9)["to"]("scaleY", .9)["duration"](f)["chain"]()["to"]("scaleX", 1)["to"]("scaleY", 1)["duration"](f)["then"](() => this["R$"](a), this)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[27] == i) {
                Object["defineProperty"](c["prototype"], "Vq", {
                  ["value"]() {
                    var a = hr,
                      b = hu,
                      c = a[0],
                      d = a[1],
                      e = a[2],
                      f = a[6],
                      g = a[4],
                      h = b[221],
                      i = "skin",
                      j = "mapIndex",
                      k = "divide",
                      l = "pos",
                      m = "size",
                      n = "length",
                      o = "addChild",
                      p = "highGround";
                    let q, r;
                    r = this["sw"]["map"]["pe"];
                    switch (this["bg"]["skin"] = `resources/img/map/bg_${this["mapIndex"]}.png`, this["divide"]["skin"] = `resources/img/map/divide_${this["mapIndex"]}.png`, this["mapIndex"]) {
                      case 0:
                        this["divide"]["pos"](0, b[305]), this["divide"]["size"](h, b[306]);
                        break;
                      case 1:
                        this["divide"]["pos"](0, b[307]), this["divide"]["size"](b[308], b[62]);
                        break;
                      case 2:
                        this["divide"]["pos"](0, b[309]), this["divide"]["size"](h, b[8]);
                        break;
                      case 3:
                        this["divide"]["pos"](0, b[310]), this["divide"]["size"](b[311], b[45])
                    }
                    q = null;
                    for (let b = 0; b < r["length"]; b++)
                      for (let e = 0; e < r[b]["length"]; e++) q = this["dq"][b][e], "0_0" == r[b][e] || "0_1" == r[b][e] ? (q["skin"] = `resources/img/map/road_${this["mapIndex"]}.png`, this["road"]["addChild"](q)) : "1_0" == r[b][e] || "1_1" == r[b][e] ? (q["skin"] = `resources/img/map/space_${this["mapIndex"]}.png`, this["highGround"]["addChild"](q)) : "2_0" == r[b][e] ? (q["skin"] = `resources/img/map/grass_${this["mapIndex"]}_0.png`, this["highGround"]["addChild"](q)) : "2_1" == r[b][e] && (q["skin"] = `resources/img/map/grass_${this["mapIndex"]}_1.png`, this["highGround"]["addChild"](q))
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[47] == i) {
                Object["defineProperty"](c["prototype"], "M$", {
                  ["value"](a, b, c, d) {
                    var e = hr,
                      f = e[0];
                    let g;
                    g = c * this["sw"]["props"]["ta"] + b;
                    c < 1 ? this["j$"](a, g, d) : this["z$"](a, g, d)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[51] == i) {
                Object["defineProperty"](c["prototype"], "v$", {
                  ["value"](a, b) {
                    var c = hr,
                      d = c[2],
                      e = "Vy";
                    let f;
                    f = a ? this["end1"] : this["end2"];
                    this["Vy"]["x"] = f["x"] < hu[216] ? f["width"] : 0, this["Vy"]["y"] = 0, this["Vy"] = f["localToGlobal"](this["Vy"]), qs["instance"]()["xg"](this["Vy"]["x"], this["Vy"]["y"], b, f)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[49] == i) {
                Object["defineProperty"](c["prototype"], "p$", {
                  ["value"](a, b, c) {
                    var d = hr,
                      e = d[0],
                      f = d[1],
                      g = d[2],
                      h = "sw",
                      i = "map",
                      j = "mapIndex";
                    this["sw"]["map"]["pe"][b][c] = a ? "1_0" : "1_1", this["Lq"]["get"](`${b}_${c}`)["skin"] = `resources/img/map/space_${this["mapIndex"]}.png`, qs["instance"]()["dg"](this["map"], a, "resources/img/map/grass_" + this["sw"]["map"]["mapIndex"] + "_" + (a ? 0 : 1) + ".png", b * this["sw"]["map"]["ye"], c * this["sw"]["map"]["gridHei"])
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[48] == i) {
                Object["defineProperty"](c["prototype"], "O$", {
                  ["value"](a, b) {
                    var c = hr,
                      d = hu,
                      e = c[6],
                      f = c[2],
                      g = c[0],
                      h = c[3],
                      i = c[4],
                      j = d[81],
                      k = d[45],
                      l = d[88],
                      m = d[132],
                      n = "getChildByName",
                      o = "sw",
                      p = "au",
                      q = "Tween",
                      r = "create",
                      s = "to",
                      t = "rotation",
                      u = "duration",
                      v = "chain",
                      w = "Event",
                      x = "play",
                      y = "Gi",
                      z = "getChildAt",
                      A = "visible",
                      B = "scaleX",
                      C = "scaleY",
                      D = "alpha";
                    let E, F, G, H, I, J, K, L;
                    pC["instance"]()["playSound"]("adou_hit");
                    K = a ? this["end1"] : this["end2"], L = K["getChildByName"]("sk"), G = K["getChildByName"]("heartBox"), H = a ? this["sw"]["au"]["Zi"] : this["sw"]["au"]["Ki"];
                    Laya["Tween"]["create"](L)["to"]("rotation", -10 * b)["duration"](j)["chain"]()["to"]("rotation", 10 * b)["duration"](j)["chain"]()["to"]("rotation", 0)["duration"](j);
                    I = () => {
                      L["off"](Laya["Event"]["STOPPED"], I), L["play"]("zhan", !0)
                    };
                    L["on"](Laya["Event"]["STOPPED"], this, I), this["sw"]["au"]["Gi"] ? L["play"]("attack", !1) : L["play"]("tu", !1);
                    for (let e = 0; e < b; e++) F = G["getChildAt"](H + e), F["skin"] = "resources/img/battleUI/heart2.png", E = F["getChildAt"](0), J = F["getChildAt"](1), E["visible"] = !0, J["visible"] = !0, Laya["Tween"]["create"](F)["to"]("scaleX", 1.5)["to"]("scaleY", 1.5)["duration"](k)["chain"]()["to"]("scaleX", 1)["to"]("scaleY", 1)["duration"](k)["delay"](d[113]), Laya["Tween"]["create"](E)["to"]("y", E["y"] - 10)["duration"](k)["chain"]()["to"]("rotation", -l)["duration"](j)["chain"]()["to"]("y", E["y"] + k)["to"]("alpha", 0)["duration"](m), Laya["Tween"]["create"](J)["to"]("y", J["y"] - 10)["duration"](k)["chain"]()["to"]("rotation", l)["duration"](j)["chain"]()["to"]("y", J["y"] + k)["to"]("alpha", 0)["duration"](m)["then"](() => {
                      this["Jq"](a)
                    }, this);
                    this["sw"]["au"]["Gi"] && (this["Y$"](a, b), this["sw"]["au"]["Gi"] = !1)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[20] == i) {
                Object["defineProperty"](c["prototype"], "Nq", {
                  ["value"]() {
                    var a = hr,
                      b = a[0],
                      c = "now",
                      d = "mq";
                    if (Date["now"]() - this["mq"] < this["wq"]) return;
                    this["mq"] = Date["now"]();
                    r0["instance"]()["_Y"]({
                      ["type"]: 2,
                      ["nm"]: !0,
                      ["onComplete"]: () => {}
                    })["success"] && this["C$"]()
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              }
              if (d[40] == i) {
                Object["defineProperty"](c["prototype"], "addHp", {
                  ["value"](a, b) {
                    var c = hr,
                      d = hu,
                      e = c[2],
                      f = c[6],
                      g = c[0],
                      h = c[4],
                      i = c[11],
                      j = "sw",
                      k = "au",
                      l = "Tween",
                      m = "to",
                      ro4 = "scaleX",
                      ro5 = "scaleY",
                      n = "Vy";
                    let o, p;
                    o = (a ? this["end1"] : this["end2"])["getChildByName"]("heartBox"), p = a ? this["sw"]["au"]["Zi"] : this["sw"]["au"]["Ki"];
                    for (let a = 0; a < b; a++) {
                      let b;
                      b = o["getChildAt"](p - a - 1);
                      b["skin"] = "resources/img/battleUI/heart1.png", Laya["Tween"]["to"](b, {
                        ["scaleX"]: 1.5,
                        ["scaleY"]: 1.5
                      }, d[132], null, Laya["Handler"]["create"](this, () => {
                        Laya["Tween"]["to"](b, {
                          ["scaleX"]: 1,
                          ["scaleY"]: 1
                        }, d[45])
                      })), this["Vy"]["x"] = b["x"], this["Vy"]["y"] = b["y"], this["Vy"] = b["parent"]["localToGlobal"](this["Vy"]), this["Vy"] = this["effectBox"]["globalToLocal"](this["Vy"]), qs["instance"]()["redPoint"](this["Vy"]["x"], this["Vy"]["y"])
                    }
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              }
              if (d[29] == i) {
                Object["defineProperty"](c["prototype"], "qq", {
                  ["value"]() {
                    var a = hr,
                      b = "instance",
                      c = "vO",
                      d = "Point",
                      e = "TEMP";
                    vN["instance"]()["vO"] || (sF["instance"]()["Bn"]("DeckDialog"), vN["instance"]()["vO"] = !0), Laya["Point"]["TEMP"]["x"] = 0, Laya["Point"]["TEMP"]["y"] = hu[176]
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[24] == i) {
                Object["defineProperty"](c["prototype"], "S$", {
                  ["value"](a, b = !1) {
                    var c = hr,
                      d = c[0],
                      e = "play",
                      f = "mapIndex";
                    let g;
                    g = a ? this["h$"] : this["e$"];
                    b ? g["play"]("boss" + this["mapIndex"], !1) : g["play"]("mob" + this["mapIndex"], !1)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[16] == i) {
                Object["defineProperty"](c["prototype"], "sV", {
                  ["value"](a) {
                    var b = hr,
                      c = hu,
                      d = b[3],
                      e = b[4],
                      f = b[2],
                      g = b[5],
                      h = b[6],
                      i = c[43],
                      j = "stage",
                      k = "width",
                      l = "height",
                      m = "pos";
                    let n, o, p, q, r, s;
                    q = Laya["stage"]["width"], n = Laya["stage"]["height"], r = q - i, s = new Laya["Text"];
                    s["text"] = a, s["color"] = "#FFD700", s["bold"] = !0, s["fontSize"] = i, s["stroke"] = 5, s["strokeColor"] = "#7B2800", s["align"] = "center", s["wordWrap"] = !1, s["width"] = r, s["height"] = c[61], s["pos"](-r / 2, -c[22]);
                    p = new Laya["Sprite"];
                    p["zIndex"] = t1["Ur"], p["addChild"](s);
                    o = new Laya["Point"](q / 2, .25 * n);
                    return this["globalToLocal"](o), p["pos"](o["x"], o["y"]), p
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[34] == i) {
                Object["defineProperty"](c["prototype"], "zq", {
                  ["value"]() {
                    var a = hr,
                      b = hu,
                      c = a[0],
                      d = a[4],
                      e = a[6],
                      f = a[5],
                      g = a[3],
                      h = "sw",
                      i = "map",
                      j = "ve",
                      k = "rp",
                      l = "length",
                      m = "instance",
                      n = "showTip",
                      o = "Point",
                      p = "TEMP",
                      q = "shovelAd",
                      r = "width",
                      s = "height",
                      t = "localToGlobal",
                      u = "visible",
                      v = "player",
                      w = "hasUsedFreeShovel",
                      x = "go",
                      y = "Se",
                      z = "ye",
                      A = "gridHei",
                      B = "hasUsedFreeBulldozer";
                    if (this["sw"]["map"]["ve"]) {
                      let b, d, e, f;
                      this["rp"]["length"] = 0;
                      f = pR["instance"]();
                      e = na["instance"]()["ub"](3)["sb"];
                      for (let b = 0; b < e["length"]; b++) f["nP"](!0, b) && this["rp"]["push"](b);
                      if (this["rp"]["length"] <= 0) return void pp["instance"]()["showTip"]("当前刷新栏无空格");
                      d = this["rp"]["length"] >= 2 ? 2 : this["rp"]["length"];
                      b = () => {
                        var a = hr,
                          b = a[0],
                          e = "Point",
                          f = "TEMP";
                        Laya["Point"]["TEMP"]["x"] = this["shovelAd"]["width"] / 2, Laya["Point"]["TEMP"]["y"] = this["shovelAd"]["height"] / 2, this["shovelAd"]["localToGlobal"](Laya["Point"]["TEMP"]);
                        for (let a = 0; a < d; a++) this["G$"](Laya["Point"]["TEMP"]["x"], Laya["Point"]["TEMP"]["y"], this["rp"][a]);
                        this["shovelAd"]["visible"] = !1, this["sw"]["map"]["ve"] = !1, this["sw"]["map"]["we"] = !0, pp["instance"]()["showTip"](`恭喜您获得了${d}把铲子`)
                      };
                      this["sw"]["player"]["hasUsedFreeShovel"] ? sJ["instance"]()["go"](4, b, null, rt) : (this["sw"]["player"]["hasUsedFreeShovel"] = !0, b())
                    }
                    if (this["sw"]["map"]["Se"]) {
                      let d;
                      d = () => {
                        let d, e, f, g, j, k, v, w;
                        Laya["Point"]["TEMP"]["x"] = this["shovelAd"]["width"] / 2, Laya["Point"]["TEMP"]["y"] = this["shovelAd"]["height"] / 2, this["shovelAd"]["localToGlobal"](Laya["Point"]["TEMP"]);
                        j = Laya["Point"]["TEMP"]["x"], k = Laya["Point"]["TEMP"]["y"], g = this["sw"]["map"]["Le"], d = g["length"] - 2, f = g[d]["x"], v = g[d]["y"];
                        Laya["Point"]["TEMP"]["x"] = f * this["sw"]["map"]["ye"] + this["sw"]["map"]["ye"] / 2, Laya["Point"]["TEMP"]["y"] = v * this["sw"]["map"]["gridHei"] + this["sw"]["map"]["gridHei"] / 2, this["map"]["localToGlobal"](Laya["Point"]["TEMP"]);
                        w = Laya["Point"]["TEMP"]["x"], e = Laya["Point"]["TEMP"]["y"];
                        qs["instance"]()["vg"](j, k, w, e, b[176], () => {
                          if (this["T$"]()) return;
                          vb["instance"]()["_A"](!0, 1)["Yb"](null)
                        }, "#ffffff", "resources/img/battleUI/ad/bulldozer.png"), this["shovelAd"]["visible"] = !1, this["sw"]["map"]["Se"] = !1, this["sw"]["map"]["ke"] = !0, pp["instance"]()["showTip"]("阿斗已经高枕无忧了")
                      };
                      if (this["sw"]["player"]["hasUsedFreeBulldozer"]) {
                        this["shovelAd"]["getChildByName"]("adImg");
                        sJ["instance"]()["go"](5, d, null, ot)
                      } else this["sw"]["player"]["hasUsedFreeBulldozer"] = !0, d()
                    }
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[69] == i) {
                Object["defineProperty"](c["prototype"], "y$", {
                  ["value"](a, b, c) {
                    var d = hr,
                      e = d[0],
                      f = d[1],
                      g = d[22],
                      h = "sw",
                      i = "map",
                      j = "mapIndex",
                      k = ".png";
                    this["sw"]["map"]["pe"][b][c] = a ? "2_0" : "2_1", this["Lq"]["get"](`${b}_${c}`)["skin"] = "resources/img/map/grass_" + this["sw"]["map"]["mapIndex"] + "_" + (a ? 0 : 1) + ".png", qs["instance"]()["mg"](this["highGround"], "resources/img/map/space_" + this["mapIndex"] + ".png", b * this["sw"]["map"]["ye"], c * this["sw"]["map"]["gridHei"])
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[77] == i) {
                Object["defineProperty"](c["prototype"], "addEvent", {
                  ["value"]() {
                    var a = hr,
                      b = a[0],
                      c = "instance",
                      d = "on";
                    oc["instance"]["on"](sS["xt"], this, this["r$"]), oc["instance"]["on"](sS["bt"], this, this["o$"]), oc["instance"]["on"](sS["St"], this, this["l$"]), oc["instance"]["on"](sS["Mt"], this, this["c$"]), oc["instance"]["on"](sS["Pt"], this, this["u$"]), oc["instance"]["on"](sS["Dt"], this, this["s$"]), oc["instance"]["on"](sS["At"], this, this["p$"]), oc["instance"]["on"](sS["Et"], this, this["y$"]), oc["instance"]["on"](sS["Bt"], this, this["f$"]), oc["instance"]["on"](sS["It"], this, this["gameOver"]), oc["instance"]["on"](sS["Ct"], this, this["g$"]), oc["instance"]["on"](sS["Tt"], this, this["d$"]), oc["instance"]["on"](sS["Rt"], this, this["L$"]), oc["instance"]["on"](sS["Ut"], this, this["m$"]), oc["instance"]["on"](sS["Ft"], this, this["mH"]), oc["instance"]["on"](sS["Ot"], this, this["w$"]), oc["instance"]["on"](sS["Yt"], this, this["v$"]), oc["instance"]["on"](sS["Xt"], this, this["_$"]), oc["instance"]["on"](sS["Gt"], this, this["k$"]), oc["instance"]["on"](sS["Ht"], this, this["S$"]), oc["instance"]["on"](sS["Wt"], this, this["x$"]), oc["instance"]["on"](sS["jt"], this, this["b$"]), oc["instance"]["on"](sS["zt"], this, this["M$"]), oc["instance"]["on"](sS["Nt"], this, this["P$"]), oc["instance"]["on"](sS["ds"], this, this["A$"]), oc["instance"]["on"](sS["qt"], this, this["E$"])
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (d[66] == i) {
                Object["defineProperty"](c["prototype"], "E$", {
                  ["value"]() {
                    var a = hr,
                      b = a[5],
                      c = "V$";
                    this["V$"] && (Laya["Tween"]["killAll"](this["V$"]), this["V$"]["destroy"](), this["V$"] = null, this["sw"]["player"]["hasPlacedActivePropThisBattle"] = !0)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              }
            }
          } ["bind"](c)();
          return c
        } ["bind"](this)["apply"]();
