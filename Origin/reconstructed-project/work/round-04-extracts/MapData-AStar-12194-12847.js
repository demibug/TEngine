        } ["bind"](this)["apply"](), oG = (a => (a[a["JuLu"] = 0] = "JuLu", a[a["YunMengZe"] = 1] = "YunMengZe", a[a["HuLaoGuan"] = 2] = "HuLaoGuan", a[a["ChiBi"] = 3] = "ChiBi", a))(oG || {}), tl = function() {
          var a = hr;
          let b;
          b = class {
            constructor() {
              var a = hr,
                b = a[0];
              this["bh"] = 1, this["Mh"] = 1.4, this["Ph"] = this["Ah"]
            }
          };
          ! function() {
            var a = hr,
              c = a[4],
              d = a[0],
              e = "defineProperty",
              nGK = "value",
              nGL = "enumerable",
              nGM = "configurable",
              nGN = "writable";
            for (let f of mp) {
              if (-1 == f) {} else if (0 == f) {
                Object["defineProperty"](b["prototype"], "search", {
                  ["value"]() {
                    var a = hr,
                      b = a[0],
                      c = a[3],
                      d = a[1],
                      e = a[2],
                      f = "max",
                      g = "min",
                      h = "grid",
                      i = "Oh",
                      j = "Yh",
                      k = "parentNode",
                      l = "Ih",
                      m = "push",
                      n = "length";
                    let o;
                    o = this["Rh"];
                    for (; o != this["Eh"];) {
                      let d, e, p, q;
                      q = Math["max"](0, o["x"] - 1), d = Math["min"](this["grid"]["Uh"] - 1, o["x"] + 1), e = Math["max"](0, o["y"] - 1), p = Math["min"](this["grid"]["Fh"] - 1, o["y"] + 1);
                      for (let c = q; c <= d; c++)
                        for (let d = e; d <= p; d++) {
                          let e, f, g, n, p;
                          if (c != o["x"] && d != o["y"]) continue;
                          f = this["grid"]["Oh"](c, d);
                          if (f == o || !f["Yh"] || !this["grid"]["Oh"](o["x"], f["y"])["Yh"] || !this["grid"]["Oh"](f["x"], o["y"])["Yh"]) continue;
                          n = this["bh"];
                          o["x"] != f["x"] && o["y"] != f["y"] && (n = this["Mh"]);
                          e = o["g"] + n * f["Xh"], p = this["Ph"](f), g = e + p;
                          this["isOpen"](f) || this["Ch"](f) ? f["f"] > g && (f["f"] = g, f["g"] = e, f["h"] = p, f["parentNode"] = o) : (f["f"] = g, f["g"] = e, f["h"] = p, f["parentNode"] = o, this["Ih"]["push"](f))
                        }
                      if (this["Th"]["push"](o), this["Ih"]["length"] <= 0) return console["error"]("AStar can`t find path"), !1;
                      for (let a = 0; a < this["Ih"]["length"]; a++)
                        for (let b = a + 1; b < this["Ih"]["length"]; b++)
                          if (this["Ih"][a]["f"] > this["Ih"][b]["f"]) {
                            let c;
                            c = this["Ih"][a];
                            this["Ih"][a] = this["Ih"][b], this["Ih"][b] = c
                          } o = this["Ih"]["shift"]()
                    }
                    return this["Gh"](), !0
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (1 == f) {
                Object["defineProperty"](b["prototype"], "Bh", {
                  ["value"](a) {
                    var b = hr,
                      c = "abs",
                      d = "Eh";
                    let e, f;
                    f = Math["abs"](a["x"] - this["Eh"]["x"]), e = Math["abs"](a["y"] - this["Eh"]["y"]);
                    return Math["sqrt"](f * f + e * e) * this["Mh"]
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              }
              if (2 == f) {
                Object["defineProperty"](b["prototype"], "Gh", {
                  ["value"]() {
                    var a = hr,
                      b = a[0],
                      c = "Hh";
                    let d;
                    this["Hh"] = new Array;
                    d = this["Eh"];
                    for (this["Hh"]["push"](d); d != this["Rh"];) d = d["parentNode"], this["Hh"]["unshift"](d)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (3 == f) {
                Object["defineProperty"](b["prototype"], "path", {
                  ["get"]() {
                    return this["Hh"]
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true
                });
                continue
              } else if (4 == f) {
                Object["defineProperty"](b["prototype"], "Ch", {
                  ["value"](a) {
                    var b = hr,
                      c = "Th";
                    for (let d = 0; d < this["Th"]["length"]; d++)
                      if (this["Th"][d] == a) return !0;
                    return !1
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (5 == f) {
                Object["defineProperty"](b["prototype"], "Ah", {
                  ["value"](a) {
                    var b = hr,
                      c = b[0],
                      d = "abs",
                      e = "Eh";
                    let f, g, h, i;
                    h = Math["abs"](a["x"] - this["Eh"]["x"]), f = Math["abs"](a["y"] - this["Eh"]["y"]), i = Math["min"](h, f), g = h + f;
                    return this["Mh"] * i + this["bh"] * (g - 2 * i)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (6 == f) {
                "use strict";
                continue
              } else if (7 == f) {
                Object["defineProperty"](b["prototype"], "Dh", {
                  ["value"](a) {
                    var b = hr,
                      c = "abs",
                      d = "Eh";
                    return (Math["abs"](a["x"] - this["Eh"]["x"]) + Math["abs"](a["y"] - this["Eh"]["y"])) * this["bh"]
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (8 == f) {
                Object["defineProperty"](b["prototype"], "isOpen", {
                  ["value"](a) {
                    var b = hr,
                      c = "Ih";
                    for (let d = 0; d < this["Ih"]["length"]; d++)
                      if (this["Ih"][d] == a) return !0;
                    return !1
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (9 == f) {
                Object["defineProperty"](b["prototype"], "Wh", {
                  ["value"](a) {
                    var b = hr,
                      c = b[0],
                      d = "grid",
                      e = "Rh";
                    return this["grid"] = a, this["Ih"] = new Array, this["Th"] = new Array, this["Rh"] = this["grid"]["jh"], this["Eh"] = this["grid"]["zh"], this["Rh"]["g"] = 0, this["Rh"]["h"] = this["Ph"](this["Rh"]), this["Rh"]["f"] = this["Rh"]["g"] + this["Rh"]["h"], this["search"]()
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              }
            }
          } ["bind"](b)();
          return b
        } ["bind"](this)["apply"](), ru = class {
          constructor(a, b) {
            var c = hr,
              d = c[0];
            this["Yh"] = !0, this["Xh"] = 1, this["x"] = a, this["y"] = b
          }
        }, oS = function() {
          var a = hr;
          let b;
          b = class {
            constructor(a, b) {
              var c = hr,
                d = c[0],
                e = "Nh",
                f = "qh",
                g = "$h";
              this["Nh"] = a, this["qh"] = b, this["$h"] = new Array;
              for (let a = 0; a < this["Nh"]; a++) {
                this["$h"][a] = new Array;
                for (let b = 0; b < this["qh"]; b++) this["$h"][a][b] = new ru(a, b)
              }
            }
          };
          ! function() {
            "use strict";
            var a = hr,
              c = a[0],
              d = "defineProperty",
              nHy = "value",
              nHz = "enumerable",
              nHA = "configurable",
              nHB = "writable",
              nHC = "get";
            Object["defineProperty"](b["prototype"], "Oh", {
              ["value"](a, b) {
                return this["$h"][a][b]
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            });
            Object["defineProperty"](b["prototype"], "Vh", {
              ["value"](a, b) {
                var c = hr,
                  d = c[0];
                this["Qh"] = this["$h"][a][b]
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            });
            Object["defineProperty"](b["prototype"], "Zh", {
              ["value"](a, b) {
                var c = hr,
                  d = c[0];
                this["Kh"] = this["$h"][a][b]
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            });
            Object["defineProperty"](b["prototype"], "Jh", {
              ["value"](a, b, c) {
                var d = hr,
                  e = d[0];
                this["$h"][a][b]["Yh"] = c
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            });
            Object["defineProperty"](b["prototype"], "Uh", {
              ["get"]() {
                return this["Nh"]
              },
              ["enumerable"]: false,
              ["configurable"]: true
            });
            Object["defineProperty"](b["prototype"], "Fh", {
              ["get"]() {
                return this["qh"]
              },
              ["enumerable"]: false,
              ["configurable"]: true
            });
            Object["defineProperty"](b["prototype"], "jh", {
              ["get"]() {
                return this["Qh"]
              },
              ["enumerable"]: false,
              ["configurable"]: true
            });
            Object["defineProperty"](b["prototype"], "zh", {
              ["get"]() {
                return this["Kh"]
              },
              ["enumerable"]: false,
              ["configurable"]: true
            })
          } ["bind"](b)();
          return b
        } ["bind"](this)["apply"](), s4 = function() {
          var a = hr;
          let b;
          b = class c {
            constructor() {
              var a = hr,
                b = hu,
                c = a[0],
                d = a[1],
                e = b[65],
                nHN = "map",
                f = "0_1",
                g = "0_0",
                h = "2_1",
                i = "2_0",
                j = "1_0",
                k = "1_1",
                nHU = "se",
                nHV = "ie",
                nHW = "he",
                nHX = "ee",
                nHY = "ae",
                nHZ = "ne",
                nH0 = "re",
                nH1 = "oe";
              this["mapIndex"] = 0, this["te"] = {
                ["map"]: [
                  ["0_1", "0_1", "0_1", "0_1", "0_1", "0_1", "0_0", "0_0", "0_0", "0_0"],
                  ["2_1", "2_1", "2_1", "2_1", "2_1", "0_1", "0_0", "2_0", "2_0", "2_0"],
                  ["2_1", "2_1", "2_1", "2_1", "2_1", "0_1", "0_0", "1_0", "1_0", "2_0"],
                  ["2_1", "1_1", "1_1", "0_1", "0_1", "0_1", "0_0", "1_0", "1_0", "2_0"],
                  ["2_1", "1_1", "1_1", "0_1", "0_0", "0_0", "0_0", "1_0", "1_0", "2_0"],
                  ["2_1", "1_1", "1_1", "0_1", "0_0", "2_0", "2_0", "2_0", "2_0", "2_0"],
                  ["2_1", "2_1", "2_1", "0_1", "0_0", "2_0", "2_0", "2_0", "2_0", "2_0"],
                  ["0_1", "0_1", "0_1", "0_1", "0_0", "0_0", "0_0", "0_0", "0_0", "0_0"]
                ],
                ["se"]: {
                  ["x"]: 0,
                  ["y"]: 9
                },
                ["ie"]: {
                  ["x"]: 0,
                  ["y"]: 8
                },
                ["he"]: {
                  ["x"]: 7,
                  ["y"]: 9
                },
                ["ee"]: {
                  ["x"]: 7,
                  ["y"]: 0
                },
                ["ae"]: {
                  ["x"]: 7,
                  ["y"]: 1
                },
                ["ne"]: {
                  ["x"]: 0,
                  ["y"]: 0
                },
                ["re"]: [{
                  ["x"]: 0,
                  ["y"]: 6
                }, {
                  ["x"]: 4,
                  ["y"]: 6
                }, {
                  ["x"]: 4,
                  ["y"]: 4
                }, {
                  ["x"]: 8,
                  ["y"]: 4
                }],
                ["oe"]: 0
              }, this["le"] = {
                ["map"]: [
                  ["0_1", "0_1", "0_1", "0_1", "0_1", "2_0", "0_0", "0_0", "0_0", "0_0"],
                  ["2_1", "2_1", "2_1", "2_1", "0_1", "2_0", "0_0", "2_0", "2_0", "2_0"],
                  ["2_1", "2_1", "2_1", "2_1", "0_1", "2_0", "0_0", "1_0", "1_0", "2_0"],
                  ["2_1", "1_1", "1_1", "0_1", "0_1", "2_0", "0_0", "1_0", "1_0", "2_0"],
                  ["2_1", "1_1", "1_1", "0_1", "2_1", "0_0", "0_0", "1_0", "1_0", "2_0"],
                  ["2_1", "1_1", "1_1", "0_1", "2_1", "0_0", "2_0", "2_0", "2_0", "2_0"],
                  ["2_1", "2_1", "2_1", "0_1", "2_1", "0_0", "2_0", "2_0", "2_0", "2_0"],
                  ["0_1", "0_1", "0_1", "0_1", "2_1", "0_0", "0_0", "0_0", "0_0", "0_0"]
                ],
                ["se"]: {
                  ["x"]: 0,
                  ["y"]: 9
                },
                ["ie"]: {
                  ["x"]: 0,
                  ["y"]: 8
                },
                ["he"]: {
                  ["x"]: 7,
                  ["y"]: 9
                },
                ["ee"]: {
                  ["x"]: 7,
                  ["y"]: 0
                },
                ["ae"]: {
                  ["x"]: 7,
                  ["y"]: 1
                },
                ["ne"]: {
                  ["x"]: 0,
                  ["y"]: 0
                },
                ["re"]: [{
                  ["x"]: 0,
                  ["y"]: 5
                }, {
                  ["x"]: 8,
                  ["y"]: 5
                }],
                ["oe"]: 1
              }, this["ce"] = {
                ["map"]: [
                  ["2_1", "0_1", "0_1", "0_1", "0_1", "0_0", "0_0", "0_0", "0_0", "2_0"],
                  ["2_1", "0_1", "2_1", "2_1", "2_1", "2_0", "2_0", "2_0", "0_0", "2_0"],
                  ["0_1", "0_1", "2_1", "2_1", "2_1", "2_0", "1_0", "1_0", "0_0", "2_0"],
                  ["0_1", "2_1", "1_1", "1_1", "2_1", "2_0", "1_0", "1_0", "0_0", "0_0"],
                  ["0_1", "0_1", "1_1", "1_1", "2_1", "2_0", "1_0", "1_0", "2_0", "0_0"],
                  ["2_1", "0_1", "1_1", "1_1", "2_1", "2_0", "2_0", "2_0", "0_0", "0_0"],
                  ["2_1", "0_1", "2_1", "2_1", "2_1", "2_0", "2_0", "2_0", "0_0", "2_0"],
                  ["2_1", "0_1", "0_1", "0_1", "0_1", "0_0", "0_0", "0_0", "0_0", "2_0"]
                ],
                ["se"]: {
                  ["x"]: 0,
                  ["y"]: 5
                },
                ["ie"]: {
                  ["x"]: 0,
                  ["y"]: 6
                },
                ["he"]: {
                  ["x"]: 7,
                  ["y"]: 5
                },
                ["ee"]: {
                  ["x"]: 7,
                  ["y"]: 4
                },
                ["ae"]: {
                  ["x"]: 7,
                  ["y"]: 3
                },
                ["ne"]: {
                  ["x"]: 0,
                  ["y"]: 4
                },
                ["re"]: [{
                  ["x"]: 0,
                  ["y"]: 5
                }, {
                  ["x"]: 8,
                  ["y"]: 5
                }],
                ["oe"]: 2
              }, this["ue"] = {
                ["map"]: [
                  ["2_1", "0_1", "0_1", "0_1", "0_1", "0_0", "0_0", "0_0", "0_0", "2_0"],
                  ["2_1", "0_1", "2_1", "2_1", "2_1", "2_0", "2_0", "2_0", "0_0", "2_0"],
                  ["2_1", "0_1", "0_1", "0_1", "0_1", "0_0", "0_0", "0_0", "0_0", "2_0"],
                  ["1_1", "1_1", "1_1", "2_1", "0_1", "0_0", "2_0", "1_0", "1_0", "1_0"],
                  ["1_1", "1_1", "1_1", "2_1", "0_1", "0_0", "2_0", "1_0", "1_0", "1_0"],
                  ["2_1", "0_1", "0_1", "0_1", "0_1", "0_0", "0_0", "0_0", "0_0", "2_0"],
                  ["2_1", "0_1", "2_1", "2_1", "2_1", "2_0", "2_0", "2_0", "0_0", "2_0"],
                  ["2_1", "0_1", "0_1", "0_1", "0_1", "0_0", "0_0", "0_0", "0_0", "2_0"]
                ],
                ["se"]: {
                  ["x"]: 0,
                  ["y"]: 5
                },
                ["ie"]: {
                  ["x"]: 0,
                  ["y"]: 6
                },
                ["he"]: {
                  ["x"]: 7,
                  ["y"]: 5
                },
                ["ee"]: {
                  ["x"]: 7,
                  ["y"]: 4
                },
                ["ae"]: {
                  ["x"]: 7,
                  ["y"]: 3
                },
                ["ne"]: {
                  ["x"]: 0,
                  ["y"]: 4
                },
                ["re"]: [{
                  ["x"]: 0,
                  ["y"]: 5
                }, {
                  ["x"]: 8,
                  ["y"]: 5
                }],
                ["oe"]: 3
              }, this["pe"] = [
                ["0_1", "0_1", "0_1", "0_1", "0_1", "0_1", "0_0", "0_0", "0_0", "0_0"],
                ["2_1", "2_1", "2_1", "2_1", "2_1", "0_1", "0_0", "2_0", "2_0", "2_0"],
                ["2_1", "2_1", "2_1", "2_1", "2_1", "0_1", "0_0", "1_0", "1_0", "2_0"],
                ["2_1", "1_1", "1_1", "0_1", "0_1", "0_1", "0_0", "1_0", "1_0", "2_0"],
                ["2_1", "1_1", "1_1", "0_1", "0_0", "0_0", "0_0", "1_0", "1_0", "2_0"],
                ["2_1", "1_1", "1_1", "0_1", "0_0", "2_0", "2_0", "2_0", "2_0", "2_0"],
                ["2_1", "2_1", "2_1", "0_1", "0_0", "2_0", "2_0", "2_0", "2_0", "2_0"],
                ["0_1", "0_1", "0_1", "0_1", "0_0", "0_0", "0_0", "0_0", "0_0", "0_0"]
              ], this["se"] = null, this["ie"] = null, this["he"] = null, this["ee"] = null, this["ae"] = null, this["ne"] = null, this["re"] = null, this["ye"] = e, this["gridHei"] = e, this["fe"] = 5, this["ge"] = e, this["de"] = e, this["oe"] = 0, this["Le"] = null, this["me"] = null, this["we"] = !1, this["ve"] = !1, this["ke"] = !1, this["Se"] = !1, this["xe"] = [{
                ["x"]: 0,
                ["y"]: 10
              }, {
                ["x"]: 0,
                ["y"]: 6
              }, {
                ["x"]: 4,
                ["y"]: 6
              }, {
                ["x"]: 4,
                ["y"]: 4
              }, {
                ["x"]: 8,
                ["y"]: 4
              }, {
                ["x"]: 8,
                ["y"]: 10
              }], this["be"] = 0, this["Me"] = 0
            }
            init(a) {
              this["changeMap"](a)
            }
            startGame(a) {
              this["changeMap"](a)
            }
            Pe(a) {
              var b = hr,
                c = b[0];
              switch (a) {
                case 0:
                  return this["te"];
                case 1:
                  return this["le"];
                case 2:
                  return this["ce"];
                case 3:
                  return this["ue"];
                default:
                  throw new Error("MapData.mapDataBlockByIndex: invalid mapIndex " + a)
              }
            }
            Ae(a) {
              var b = hr;
              return this["Pe"](a)["map"]
            }
            Ee(a, b, c) {
              var d = hr,
                e = d[0],
                f = "Point",
                g = "TEMP";
              return (c ? this["Le"] : this["me"])["filter"](c => (Laya["Point"]["TEMP"]["setTo"](c["x"] * this["ye"], c["y"] * this["gridHei"]), np["Ms"](Laya["Point"]["TEMP"], a) <= b * b))
            }
            Be(a) {
              var b = hr,
                d = b[0];
              return c["De"](this["pe"], a ? this["ie"] : this["ae"], a ? this["he"] : this["ne"])
            }
            Ie(a, b) {
              var d = hr,
                e = d[0],
                f = d[1],
                g = "Ce";
              let h, i, j, k;
              var l;
              if (!b) {
                let b;
                b = c["Ce"]["get"](a);
                if (b) return b
              }
              j = this["Pe"](a), i = b ? j["ie"] : j["ae"], k = b ? j["he"] : j["ne"], h = null != (l = c["De"](j["map"], i, k)) ? l : [];
              return b || c["Ce"]["set"](a, h), h
            }
            static De(a, b, c) {
              var d = hr,
                e = d[0],
                f = d[1],
                g = "length";
              let h, i;
              i = new oS(a["length"], a[0]["length"]);
              i["Vh"](b["x"], b["y"]), i["Zh"](c["x"], c["y"]);
              for (let b = 0; b < a["length"]; b++)
                for (let c = 0; c < a[b]["length"]; c++) "0_0" !== a[b][c] && "0_1" !== a[b][c] && i["Jh"](b, c, !1);
              h = new tl;
              return h["Wh"](i) ? h["path"] : null
            }
            changeMap(a) {
              var b = hr,
                c = b[0],
                d = "mapIndex",
                e = "map",
                f = "length",
                g = "se",
                h = "ie",
                i = "he",
                j = "ee",
                k = "ae",
                l = "ne",
                m = "re",
                n = "oe",
                o = "Be";
              let p;
              this["mapIndex"] = a;
              p = this["Pe"](this["mapIndex"]);
              for (let a = 0; a < p["map"]["length"]; a++)
                for (let b = 0; b < p["map"][a]["length"]; b++) this["pe"][a][b] = p["map"][a][b];
              this["se"] = p["se"], this["ie"] = p["ie"], this["he"] = p["he"], this["ee"] = p["ee"], this["ae"] = p["ae"], this["ne"] = p["ne"], this["re"] = p["re"], this["oe"] = p["oe"], this["Le"] = this["Be"](!0), this["me"] = this["Be"](!1)
            }
            Te(a, b) {
              var c = hr,
                d = hu,
                e = c[0],
                f = "Me",
                g = "be";
              let h, i, j;
              j = Date["now"]();
              j > this["Me"] && (this["be"] = 0), this["Me"] = j + d[190];
              i = np["Zs"](this["be"]), h = np["Js"](i);
              return a !== h["x"] || b !== h["y"] ? (this["be"] = 0, !1) : (this["be"]++, this["be"] >= d[7] && (this["be"] = 0, this["Me"] = 0, !0))
            }
          };
          ! function() {
            "use strict";
            var a = hr,
              c = "defineProperty",
              nID = "value",
              nIE = "enumerable",
              nIF = "configurable",
              nIG = "writable";
            Object["defineProperty"](b["prototype"], "gameOver", {
              ["value"]() {
                var a = hr,
                  b = a[0];
                this["we"] = !1, this["ve"] = !1, this["ke"] = !1, this["Se"] = !1
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            });
            Object["defineProperty"](b["prototype"], "Re", {
              ["value"]() {
                var a = hr,
                  b = a[0];
                this["be"] = 0, this["Me"] = 0
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            })
          } ["bind"](b)();
          return b
        } ["bind"](this)["apply"]();
        break;
