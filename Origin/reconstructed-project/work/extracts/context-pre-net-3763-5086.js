        } ["bind"](this)["apply"](), qU = class {
          static instance() {
            var a = hr,
              b = "Instance";
            return this["Instance"] || (this["Instance"] = new this), this["Instance"]
          }
        }, pV = function() {
          var a = hr;
          let b;
          b = class c extends qU {
            constructor() {
              var a = hr,
                b = a[0];
              var c;
              c = arguments;
              super(...c), this["ya"] = !1, this["delta"] = 0, this["fa"] = 0, this["ga"] = new Map, this["da"] = 0, this["serverTime"] = 0
            }
            La(a, b, c) {
              var d = hr,
                e = d[1],
                f = "ga";
              this["ga"]["has"](a) && this["ga"]["delete"](a), this["ga"]["set"](a, function() {
                var a = hr,
                  m87 = "ma",
                  m88 = "caller";
                let d;
                d = {
                  ["ma"]: 0,
                  ["caller"]: 0
                };
                d["ma"] = c;
                d["caller"] = b;
                return d
              } ["apply"]())
            }
            wa(a) {
              var b = hr;
              this["ga"]["delete"](a)
            }
            update() {
              var a = hr,
                b = a[0],
                d = a[3],
                e = "da",
                f = "min";
              let g, h;
              if (this["ya"]) return;
              h = Laya["timer"]["currTimer"];
              g = h - this["da"];
              if (!(g <= 0)) {
                for (g = Math["min"](g, c["va"]), this["delta"] = g; g > 0;) {
                  let d;
                  d = Math["min"](c["_a"], g);
                  for (let c of this["ga"]) c[1]["ma"]["call"](c[1]["caller"], d);
                  this["fa"] += d, g -= d
                }
                this["da"] = h
              }
            }
            pause(a = !0) {
              var b = hr,
                c = b[3];
              this["ya"] = !0, a && Laya["timer"]["pause"]()
            }
          };
          ! function() {
            "use strict";
            var a = hr,
              c = a[0],
              d = "defineProperty",
              m9k = "value",
              m9l = "enumerable",
              m9m = "configurable",
              m9n = "writable";
            Object["defineProperty"](b["prototype"], "init", {
              ["value"]() {
                var a = hr;
                Laya["timer"]["frameLoop"](1, this, this["update"]), this["da"] = 0
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            });
            Object["defineProperty"](b["prototype"], "resume", {
              ["value"]() {
                var a = hr;
                Laya["timer"]["resume"](), this["ya"] = !1
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            });
            Object["defineProperty"](b["prototype"], "ka", {
              ["value"]() {
                var a = hr;
                return np["Gs"](uq["instance"]()["player"]["registerTime"], Date["now"]()) + 1
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            });
            Object["defineProperty"](b["prototype"], "Sa", {
              ["value"]() {
                return (new Date)["getDay"]()
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            })
          } ["bind"](b)();
          return b
        } ["bind"](this)["apply"]();
        break;
      case 2:
        qB["Po"] = !1, qB["Co"] = ["resources/anim", "resources/music", "resources/sound"];
        break;
      case 3:
        p6["pu"] = ["军士", "校尉", "少将", "中将", "上将", "大将", "都督", "诸侯", "霸主", "君主", "大帝"];
        break;
      case 4:
        sG = qN, ht = "stamina_video", et = "gameover_double_gold", at = "shop_props_ad", nt = "shop_lottery_ad", rt = "battle_shovel_ad", ot = "battle_bulldozer_ad", qS = function() {
          var a = hr,
            m9t = "stamina",
            m9u = "gameoverDouble",
            m9v = "shopProps",
            m9w = "shopLottery",
            m9x = "battleShovel",
            m9y = "battleBulldozer",
            b = "apply";
          let c;
          c = {
            ["stamina"]: 0,
            ["gameoverDouble"]: 0,
            ["shopProps"]: 0,
            ["shopLottery"]: 0,
            ["battleShovel"]: 0,
            ["battleBulldozer"]: 0
          };
          c["stamina"] = function() {
            var a = hr,
              b = "apply";
            let c;
            c = function() {
              var a = hr,
                m9B = "click",
                m9C = "ok",
                m9D = "fail";
              let b;
              b = {
                ["click"]: 0,
                ["ok"]: 0,
                ["fail"]: 0
              };
              b["click"] = 0;
              b["ok"] = 0;
              b["fail"] = 0;
              return b
            } ["apply"]();
            c["click"] = function() {
              var a = hr,
                b = a[1];
              let c;
              c = function() {
                var a = hr,
                  b = a[1];
                let c;
                c = function() {
                  var a = hr,
                    b = a[1],
                    m9I = "old",
                    m9J = "new";
                  let c;
                  c = {
                    ["old"]: 0,
                    ["new"]: 0
                  };
                  c["old"] = 0;
                  c["new"] = 0;
                  return c
                } ["apply"]();
                c["old"] = 0;
                c["new"] = 0;
                return c
              } ["apply"]();
              c["old"] = 1;
              c["new"] = 2;
              return c
            } ["apply"]();
            c["ok"] = function() {
              var a = hr,
                b = a[1];
              let c;
              c = function() {
                var a = hr,
                  b = a[1];
                let c;
                c = function() {
                  var a = hr,
                    b = a[1],
                    m9Q = "old",
                    m9R = "new";
                  let c;
                  c = {
                    ["old"]: 0,
                    ["new"]: 0
                  };
                  c["old"] = 0;
                  c["new"] = 0;
                  return c
                } ["apply"]();
                c["old"] = 0;
                c["new"] = 0;
                return c
              } ["apply"]();
              c["old"] = 3;
              c["new"] = 4;
              return c
            } ["apply"]();
            c["fail"] = function() {
              var a = hr,
                b = a[1];
              let c;
              c = function() {
                var a = hr,
                  b = a[1];
                let c;
                c = function() {
                  var a = hr,
                    b = a[1],
                    m9Y = "old",
                    m9Z = "new";
                  let c;
                  c = {
                    ["old"]: 0,
                    ["new"]: 0
                  };
                  c["old"] = 0;
                  c["new"] = 0;
                  return c
                } ["apply"]();
                c["old"] = 0;
                c["new"] = 0;
                return c
              } ["apply"]();
              c["old"] = 5;
              c["new"] = 6;
              return c
            } ["apply"]();
            return c
          } ["apply"]();
          c["gameoverDouble"] = function() {
            var a = hr,
              b = "apply";
            let c;
            c = function() {
              var a = hr,
                m95 = "click",
                m96 = "ok",
                m97 = "fail";
              let b;
              b = {
                ["click"]: 0,
                ["ok"]: 0,
                ["fail"]: 0
              };
              b["click"] = 0;
              b["ok"] = 0;
              b["fail"] = 0;
              return b
            } ["apply"]();
            c["click"] = function() {
              var a = hr,
                b = a[1];
              let c;
              c = function() {
                var a = hr,
                  b = a[1];
                let c;
                c = function() {
                  var a = hr,
                    b = a[1],
                    nac = "old",
                    nad = "new";
                  let c;
                  c = {
                    ["old"]: 0,
                    ["new"]: 0
                  };
                  c["old"] = 0;
                  c["new"] = 0;
                  return c
                } ["apply"]();
                c["old"] = 0;
                c["new"] = 0;
                return c
              } ["apply"]();
              c["old"] = 7;
              c["new"] = 8;
              return c
            } ["apply"]();
            c["ok"] = function() {
              var a = hr,
                b = a[1];
              let c;
              c = function() {
                var a = hr,
                  b = a[1];
                let c;
                c = function() {
                  var a = hr,
                    b = a[1],
                    nak = "old",
                    nal = "new";
                  let c;
                  c = {
                    ["old"]: 0,
                    ["new"]: 0
                  };
                  c["old"] = 0;
                  c["new"] = 0;
                  return c
                } ["apply"]();
                c["old"] = 0;
                c["new"] = 0;
                return c
              } ["apply"]();
              c["old"] = 9;
              c["new"] = 10;
              return c
            } ["apply"]();
            c["fail"] = function() {
              var a = hr,
                b = hu,
                c = a[1];
              let d;
              d = function() {
                var a = hr,
                  b = a[1];
                let c;
                c = function() {
                  var a = hr,
                    b = a[1],
                    nas = "old",
                    nat = "new";
                  let c;
                  c = {
                    ["old"]: 0,
                    ["new"]: 0
                  };
                  c["old"] = 0;
                  c["new"] = 0;
                  return c
                } ["apply"]();
                c["old"] = 0;
                c["new"] = 0;
                return c
              } ["apply"]();
              d["old"] = b[7];
              d["new"] = b[9];
              return d
            } ["apply"]();
            return c
          } ["apply"]();
          c["shopProps"] = function() {
            var a = hr,
              b = "apply";
            let c;
            c = function() {
              var a = hr,
                naz = "click",
                naA = "ok",
                naB = "fail";
              let b;
              b = {
                ["click"]: 0,
                ["ok"]: 0,
                ["fail"]: 0
              };
              b["click"] = 0;
              b["ok"] = 0;
              b["fail"] = 0;
              return b
            } ["apply"]();
            c["click"] = function() {
              var a = hr,
                b = hu,
                c = a[1];
              let d;
              d = function() {
                var a = hr,
                  b = a[1];
                let c;
                c = function() {
                  var a = hr,
                    b = a[1],
                    naG = "old",
                    naH = "new";
                  let c;
                  c = {
                    ["old"]: 0,
                    ["new"]: 0
                  };
                  c["old"] = 0;
                  c["new"] = 0;
                  return c
                } ["apply"]();
                c["old"] = 0;
                c["new"] = 0;
                return c
              } ["apply"]();
              d["old"] = b[3];
              d["new"] = b[14];
              return d
            } ["apply"]();
            c["ok"] = function() {
              var a = hr,
                b = hu,
                c = a[1];
              let d;
              d = function() {
                var a = hr,
                  b = a[1];
                let c;
                c = function() {
                  var a = hr,
                    b = a[1],
                    naO = "old",
                    naP = "new";
                  let c;
                  c = {
                    ["old"]: 0,
                    ["new"]: 0
                  };
                  c["old"] = 0;
                  c["new"] = 0;
                  return c
                } ["apply"]();
                c["old"] = 0;
                c["new"] = 0;
                return c
              } ["apply"]();
              d["old"] = b[12];
              d["new"] = b[11];
              return d
            } ["apply"]();
            c["fail"] = function() {
              var a = hr,
                b = hu,
                c = a[1];
              let d;
              d = function() {
                var a = hr,
                  b = a[1];
                let c;
                c = function() {
                  var a = hr,
                    b = a[1],
                    naW = "old",
                    naX = "new";
                  let c;
                  c = {
                    ["old"]: 0,
                    ["new"]: 0
                  };
                  c["old"] = 0;
                  c["new"] = 0;
                  return c
                } ["apply"]();
                c["old"] = 0;
                c["new"] = 0;
                return c
              } ["apply"]();
              d["old"] = b[13];
              d["new"] = b[5];
              return d
            } ["apply"]();
            return c
          } ["apply"]();
          c["shopLottery"] = function() {
            var a = hr,
              b = "apply";
            let c;
            c = function() {
              var a = hr,
                na3 = "click",
                na4 = "ok",
                na5 = "fail";
              let b;
              b = {
                ["click"]: 0,
                ["ok"]: 0,
                ["fail"]: 0
              };
              b["click"] = 0;
              b["ok"] = 0;
              b["fail"] = 0;
              return b
            } ["apply"]();
            c["click"] = function() {
              var a = hr,
                b = hu,
                c = a[1];
              let d;
              d = function() {
                var a = hr,
                  b = a[1];
                let c;
                c = function() {
                  var a = hr,
                    b = a[1],
                    nba = "old",
                    nbb = "new";
                  let c;
                  c = {
                    ["old"]: 0,
                    ["new"]: 0
                  };
                  c["old"] = 0;
                  c["new"] = 0;
                  return c
                } ["apply"]();
                c["old"] = 0;
                c["new"] = 0;
                return c
              } ["apply"]();
              d["old"] = b[4];
              d["new"] = b[1];
              return d
            } ["apply"]();
            c["ok"] = function() {
              var a = hr,
                b = hu,
                c = a[1];
              let d;
              d = function() {
                var a = hr,
                  b = a[1];
                let c;
                c = function() {
                  var a = hr,
                    b = a[1],
                    nbi = "old",
                    nbj = "new";
                  let c;
                  c = {
                    ["old"]: 0,
                    ["new"]: 0
                  };
                  c["old"] = 0;
                  c["new"] = 0;
                  return c
                } ["apply"]();
                c["old"] = 0;
                c["new"] = 0;
                return c
              } ["apply"]();
              d["old"] = b[10];
              d["new"] = b[2];
              return d
            } ["apply"]();
            c["fail"] = function() {
              var a = hr,
                b = hu,
                c = a[1];
              let d;
              d = function() {
                var a = hr,
                  b = a[1];
                let c;
                c = function() {
                  var a = hr,
                    b = a[1],
                    nbq = "old",
                    nbr = "new";
                  let c;
                  c = {
                    ["old"]: 0,
                    ["new"]: 0
                  };
                  c["old"] = 0;
                  c["new"] = 0;
                  return c
                } ["apply"]();
                c["old"] = 0;
                c["new"] = 0;
                return c
              } ["apply"]();
              d["old"] = b[8];
              d["new"] = b[6];
              return d
            } ["apply"]();
            return c
          } ["apply"]();
          c["battleShovel"] = function() {
            var a = hr,
              b = "apply";
            let c;
            c = function() {
              var a = hr,
                nbx = "click",
                nby = "ok",
                nbz = "fail";
              let b;
              b = {
                ["click"]: 0,
                ["ok"]: 0,
                ["fail"]: 0
              };
              b["click"] = 0;
              b["ok"] = 0;
              b["fail"] = 0;
              return b
            } ["apply"]();
            c["click"] = function() {
              var a = hr,
                b = hu,
                c = a[1];
              let d;
              d = function() {
                var a = hr,
                  b = a[1];
                let c;
                c = function() {
                  var a = hr,
                    b = a[1],
                    nbE = "old",
                    nbF = "new";
                  let c;
                  c = {
                    ["old"]: 0,
                    ["new"]: 0
                  };
                  c["old"] = 0;
                  c["new"] = 0;
                  return c
                } ["apply"]();
                c["old"] = 0;
                c["new"] = 0;
                return c
              } ["apply"]();
              d["old"] = b[19];
              d["new"] = b[57];
              return d
            } ["apply"]();
            c["ok"] = function() {
              var a = hr,
                b = hu,
                c = a[1];
              let d;
              d = function() {
                var a = hr,
                  b = a[1];
                let c;
                c = function() {
                  var a = hr,
                    b = a[1],
                    nbM = "old",
                    nbN = "new";
                  let c;
                  c = {
                    ["old"]: 0,
                    ["new"]: 0
                  };
                  c["old"] = 0;
                  c["new"] = 0;
                  return c
                } ["apply"]();
                c["old"] = 0;
                c["new"] = 0;
                return c
              } ["apply"]();
              d["old"] = b[37];
              d["new"] = b[23];
              return d
            } ["apply"]();
            c["fail"] = function() {
              var a = hr,
                b = hu,
                c = a[1];
              let d;
              d = function() {
                var a = hr,
                  b = a[1];
                let c;
                c = function() {
                  var a = hr,
                    b = a[1],
                    nbU = "old",
                    nbV = "new";
                  let c;
                  c = {
                    ["old"]: 0,
                    ["new"]: 0
                  };
                  c["old"] = 0;
                  c["new"] = 0;
                  return c
                } ["apply"]();
                c["old"] = 0;
                c["new"] = 0;
                return c
              } ["apply"]();
              d["old"] = b[46];
              d["new"] = b[22];
              return d
            } ["apply"]();
            return c
          } ["apply"]();
          c["battleBulldozer"] = function() {
            var a = hr,
              b = "apply";
            let c;
            c = function() {
              var a = hr,
                nb1 = "click",
                nb2 = "ok",
                nb3 = "fail";
              let b;
              b = {
                ["click"]: 0,
                ["ok"]: 0,
                ["fail"]: 0
              };
              b["click"] = 0;
              b["ok"] = 0;
              b["fail"] = 0;
              return b
            } ["apply"]();
            c["click"] = function() {
              var a = hr,
                b = hu,
                c = a[1];
              let d;
              d = function() {
                var a = hr,
                  b = a[1];
                let c;
                c = function() {
                  var a = hr,
                    b = a[1],
                    nb8 = "old",
                    nb9 = "new";
                  let c;
                  c = {
                    ["old"]: 0,
                    ["new"]: 0
                  };
                  c["old"] = 0;
                  c["new"] = 0;
                  return c
                } ["apply"]();
                c["old"] = 0;
                c["new"] = 0;
                return c
              } ["apply"]();
              d["old"] = b[36];
              d["new"] = b[31];
              return d
            } ["apply"]();
            c["ok"] = function() {
              var a = hr,
                b = hu,
                c = a[1];
              let d;
              d = function() {
                var a = hr,
                  b = a[1];
                let c;
                c = function() {
                  var a = hr,
                    b = a[1],
                    ncg = "old",
                    nch = "new";
                  let c;
                  c = {
                    ["old"]: 0,
                    ["new"]: 0
                  };
                  c["old"] = 0;
                  c["new"] = 0;
                  return c
                } ["apply"]();
                c["old"] = 0;
                c["new"] = 0;
                return c
              } ["apply"]();
              d["old"] = b[41];
              d["new"] = b[15];
              return d
            } ["apply"]();
            c["fail"] = function() {
              var a = hr,
                b = hu,
                c = a[1];
              let d;
              d = function() {
                var a = hr,
                  b = a[1];
                let c;
                c = function() {
                  var a = hr,
                    b = a[1],
                    nco = "old",
                    ncp = "new";
                  let c;
                  c = {
                    ["old"]: 0,
                    ["new"]: 0
                  };
                  c["old"] = 0;
                  c["new"] = 0;
                  return c
                } ["apply"]();
                c["old"] = 0;
                c["new"] = 0;
                return c
              } ["apply"]();
              d["old"] = b[21];
              d["new"] = b[30];
              return d
            } ["apply"]();
            return c
          } ["apply"]();
          return c
        } ["apply"](), sU = function() {
          var a = hr,
            b = hu,
            c = a[1],
            ncw = "old",
            ncx = "new";
          let d;
          d = {
            ["old"]: 0,
            ["new"]: 0
          };
          d["old"] = b[35];
          d["new"] = b[32];
          return d
        } ["apply"](), pn = function() {
          var a = hr,
            b = hu,
            c = a[1],
            ncA = "old",
            ncB = "new";
          let d;
          d = {
            ["old"]: 0,
            ["new"]: 0
          };
          d["old"] = b[60];
          d["new"] = b[43];
          return d
        } ["apply"](), qh = function() {
          var a = hr,
            b = hu,
            c = a[1],
            ncE = "old",
            ncF = "new";
          let d;
          d = {
            ["old"]: 0,
            ["new"]: 0
          };
          d["old"] = b[44];
          d["new"] = b[28];
          return d
        } ["apply"](), qO = function() {
          var a = hr,
            b = hu,
            c = a[1],
            ncI = "old",
            ncJ = "new";
          let d;
          d = {
            ["old"]: 0,
            ["new"]: 0
          };
          d["old"] = b[56];
          d["new"] = b[62];
          return d
        } ["apply"](), tN = function() {
          var a = hr,
            b = hu,
            c = a[1],
            ncM = "old",
            ncN = "new";
          let d;
          d = {
            ["old"]: 0,
            ["new"]: 0
          };
          d["old"] = b[59];
          d["new"] = b[58];
          return d
        } ["apply"](), rA = function() {
          var a = hr;
          let b;
          b = {
            [ht]: 0,
            [et]: 0,
            [at]: 0,
            [nt]: 0,
            [rt]: 0,
            [ot]: 0
          };
          b[ht] = "stamina";
          b[et] = "gameoverDouble";
          b[at] = "shopProps";
          b[nt] = "shopLottery";
          b[rt] = "battleShovel";
          b[ot] = "battleBulldozer";
          return b
        } ["apply"](), tV = function() {
          let a;
          a = class b extends qU {
            constructor() {
              var a;
              a = arguments;
              super(...a), this["Ul"] = !1
            }
            Ol(a) {
              var b = hr,
                c = b[1];
              return this["Fl"]() ? a["new"] : a["old"]
            }
            Yl() {
              var a = hr,
                c = a[3],
                d = "Ul";
              let e, f;
              if (this["Ul"]) return;
              f = this["Xl"]();
              if (0 === f["length"]) return;
              e = f["slice"]();
              this["Ul"] = !0, qK["instance"]()["track"](e, function() {
                var a = hr,
                  ncV = "success",
                  ncW = "fail",
                  c = "Ul";
                let d;
                d = {
                  ["success"]: 0,
                  ["fail"]: 0
                };
                d["success"] = () => {
                  Laya["LocalStorage"]["removeItem"](b["Gl"]), this["Ul"] = !1
                };
                d["fail"] = a => {
                  this["Ul"] = !1
                };
                return d
              } ["bind"](this)["apply"]())
            }
            enqueue(a) {
              var b = hr,
                c = b[0];
              let d;
              d = this["Xl"]();
              d["push"](a), this["Hl"](d)
            }
            Xl() {
              var a = hr,
                c = a[5];
              let d;
              d = Laya["LocalStorage"]["getItem"](b["Gl"]);
              if (!d) return [];
              try {
                let b;
                b = JSON["parse"](d);
                return Array["isArray"](b) ? b : []
              } catch (a) {
                return []
              }
            }
            Hl(a) {
              var c = hr;
              Laya["LocalStorage"]["setItem"](b["Gl"], JSON["stringify"](a))
            }
            jl(a) {
              var b = hr;
              this["enqueue"](this["Ol"](pn))
            }
            zl(a) {
              var b = hr;
              this["enqueue"](this["Ol"](qh))
            }
            Nl(a) {
              var b = hr;
              this["enqueue"](this["Ol"](qO))
            }
            ql(a) {
              var b = hr;
              let c;
              c = qS[rA[a]];
              this["enqueue"](this["Ol"](c["click"]))
            }
            $l(a, b) {
              var c = hr,
                d = c[0];
              let e;
              e = qS[rA[a]];
              this["enqueue"](this["Ol"](b ? e["ok"] : e["fail"]))
            }
          };
          ! function() {
            "use strict";
            var b = hr,
              c = b[0],
              d = "defineProperty",
              ndc = "value",
              ndd = "enumerable",
              nde = "configurable",
              ndf = "writable";
            Object["defineProperty"](a["prototype"], "Fl", {
              ["value"]() {
                var a = hr;
                return 1 === nx["instance"]()["ka"]()
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            });
            Object["defineProperty"](a["prototype"], "Wl", {
              ["value"]() {
                var a = hr;
                this["enqueue"](this["Ol"](sU))
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            });
            Object["defineProperty"](a["prototype"], "Vl", {
              ["value"]() {
                var a = hr;
                this["enqueue"](this["Ol"](tN))
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            })
          } ["bind"](a)();
          return a
        } ["bind"](this)["apply"]();
        break;
      case 5:
        qx = nP, p6 = class a extends qU {
          constructor() {
            var a = hr,
              b = hu,
              c = a[0],
              d = a[2],
              e = a[1],
              f = b[7],
              g = b[9],
              h = b[3],
              i = b[14],
              j = b[12],
              k = b[11];
            var l;
            l = arguments;
            super(...l), this["nu"] = !0, this["ru"] = 1, this["ou"] = new Map([
              [1, "射雕老汉"],
              [2, "老朽"],
              [3, "和尚"],
              [4, "书生"],
              [5, "采花女"],
              [6, "钓鱼老翁"],
              [7, "琵琶女"],
              [8, "诗人"],
              [9, "提灯老头"],
              [10, "布衣丁诗人"],
              [f, "将军"],
              [g, "女娃"],
              [h, "老射手"],
              [i, "张飞"],
              [j, "神赵云"],
              [k, "红面刘备"]
            ]), this["lu"] = [], this["cu"] = [1, 2, 4, 8, g, 3, 5, 9, 10, 7, f, 6, h, i, j, k]
          }
          get uu() {
            var b = hr,
              c = hu,
              d = b[0],
              e = "instance",
              f = "apply";
            let g, h, i, j, k;
            j = this["player"], k = uq["instance"]()["rank"], i = qx["instance"](), h = uq["instance"]()["bc"], g = c => a["pu"]["indexOf"](c["split"](".")[0]);
            return new Map([
              [9, function() {
                var a = hr,
                  nds = "check",
                  ndt = "desc";
                let b;
                b = {
                  ["check"]: 0,
                  ["desc"]: 0
                };
                b["check"] = () => j["winStreak"] >= 5;
                b["desc"] = "连胜5局解锁";
                return b
              } ["apply"]()],
              [3, function() {
                var a = hr,
                  ndv = "check",
                  ndw = "desc";
                let b;
                b = {
                  ["check"]: 0,
                  ["desc"]: 0
                };
                b["check"] = () => j["consecutiveLoginDays"] >= 3;
                b["desc"] = "连续登录3天解锁";
                return b
              } ["apply"]()],
              [5, function() {
                var a = hr,
                  ndy = "check",
                  ndz = "desc";
                let b;
                b = {
                  ["check"]: 0,
                  ["desc"]: 0
                };
                b["check"] = () => j["consecutiveLoginDays"] >= 7;
                b["desc"] = "连续登录7天解锁";
                return b
              } ["apply"]()],
              [6, function() {
                var b = hr,
                  c = b[0],
                  ndC = "check",
                  ndD = "desc";
                let d;
                d = {
                  ["check"]: 0,
                  ["desc"]: 0
                };
                d["check"] = () => g(k["yu"]["rank"]) >= a["pu"]["indexOf"]("大帝");
                d["desc"] = "军衔达到大帝解锁";
                return d
              } ["apply"]()],
              [7, function() {
                var a = hr,
                  ndF = "check",
                  ndG = "desc";
                let b;
                b = {
                  ["check"]: 0,
                  ["desc"]: 0
                };
                b["check"] = () => j["getPropsData"]()["length"] >= 8;
                b["desc"] = "集齐8个道具解锁";
                return b
              } ["apply"]()],
              [10, function() {
                var a = hr,
                  ndI = "check",
                  ndJ = "desc";
                let b;
                b = {
                  ["check"]: 0,
                  ["desc"]: 0
                };
                b["check"] = () => j["winStreak"] >= 9;
                b["desc"] = "连胜9局解锁";
                return b
              } ["apply"]()],
              [c[7], function() {
                var b = hr,
                  c = b[0],
                  ndM = "check",
                  ndN = "desc";
                let d;
                d = {
                  ["check"]: 0,
                  ["desc"]: 0
                };
                d["check"] = () => g(k["yu"]["rank"]) >= a["pu"]["indexOf"]("少将");
                d["desc"] = "军衔达到少将解锁";
                return d
              } ["apply"]()],
              [c[3], function() {
                var a = hr,
                  b = a[0],
                  ndQ = "check",
                  ndR = "desc";
                let c;
                c = {
                  ["check"]: 0,
                  ["desc"]: 0
                };
                c["check"] = () => i["jc"](h["fu"]("铁胎弓"));
                c["desc"] = "获得铁胎弓解锁";
                return c
              } ["apply"]()],
              [c[14], function() {
                var a = hr,
                  b = a[0],
                  ndU = "check",
                  ndV = "desc";
                let c;
                c = {
                  ["check"]: 0,
                  ["desc"]: 0
                };
                c["check"] = () => i["jc"](h["fu"]("丈八蛇矛"));
                c["desc"] = "获得丈八蛇矛解锁";
                return c
              } ["apply"]()],
              [c[12], function() {
                var a = hr,
                  b = a[0],
                  ndY = "check",
                  ndZ = "desc";
                let c;
                c = {
                  ["check"]: 0,
                  ["desc"]: 0
                };
                c["check"] = () => i["jc"](h["fu"]("龙胆亮银枪"));
                c["desc"] = "获得龙胆枪解锁";
                return c
              } ["apply"]()],
              [c[11], function() {
                var a = hr,
                  b = a[0],
                  nd2 = "check",
                  nd3 = "desc";
                let c;
                c = {
                  ["check"]: 0,
                  ["desc"]: 0
                };
                c["check"] = () => i["jc"](h["fu"]("青龙偃月刀"));
                c["desc"] = "获得青龙偃月刀解锁";
                return c
              } ["apply"]()]
            ])
          }
          gu() {
            var a = hr,
              b = hu,
              c = "player";
            let d;
            d = this["uu"];
            for (let e = 1; e <= b[11]; e++) {
              let b, f;
              b = e;
              if (this["player"]["isAvatarUnlocked"](b)) continue;
              f = d["get"](b);
              f && !f["check"]() || this["player"]["setAvatarUnlocked"](b)
            }
          }
          isAvatarUnlocked(a) {
            var b = hr;
            return this["player"]["isAvatarUnlocked"](a)
          }
          du(a) {
            var b = hr;
            var c, d;
            return this["isAvatarUnlocked"](a) ? "" : null != (d = null == (c = this["uu"]["get"](a)) ? void 0 : c["desc"]) ? d : ""
          }
          init() {
            var a = hr,
              b = a[0],
              c = "player";
            var d;
            this["player"] = uq["instance"]()["player"], this["ru"] = null != (d = this["player"]["gameAvatar"]) ? d : 1, this["gu"]()
          }
          Lu(a) {
            var b = hr;
            this["lu"]["push"](a)
          }
          mu(a) {
            var b = hr,
              c = b[0];
            this["ru"] = a, this["player"]["gameAvatar"] = a, this["lu"]["forEach"](b => b(a))
          }
          wu(b) {
            var c = hr,
              d = c[0],
              e = c[3],
              f = c[4],
              g = c[1],
              h = "pu",
              i = "indexOf",
              j = "filter";
            let k, l, m, n, o;
            l = a["pu"]["indexOf"](b["split"](".")[0]), k = [
              [hu[7], a["pu"]["indexOf"]("少将")],
              [6, a["pu"]["indexOf"]("大帝")]
            ], m = new Set(k["filter"](([, a]) => l < a)["map"](([a]) => a)), n = this["cu"]["filter"](a => !m["has"](a)), o = n[Math["floor"](Math["random"]() * n["length"])];
            return this["vu"](o)
          }
          vu(a) {
            var b = hr,
              c = b[4],
              d = b[2];
            return Laya["loader"]["load"]("resources/img/mainUI/avatar/avatar" + a + ".png", Laya["Loader"]["IMAGE"])
          }
          _u(a) {
            var b = hr,
              c = b[2];
            a["skin"] = "resources/img/mainUI/avatar/avatar" + this["player"]["gameAvatar"] + ".png"
          }
          ku(a) {
            var b = hr;
            return "resources/img/mainUI/avatar/avatar" + (null != a ? a : this["player"]["gameAvatar"]) + ".png"
          }
          Su(a) {
            var b = hr,
              c = "ku";
            let d;
            d = a["match"](/avatar(\d+)\.png$/);
            return d ? this["ku"](Number(d[1])) : this["ku"](1)
          }
          xu(a, b, c) {
            var d = hr,
              e = d[0];
            a["skin"] = c ? this["ku"]() : this["Su"](b)
          }
        };
        break;
      case 6:
