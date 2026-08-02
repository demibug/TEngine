        } = Laya), s3 = function() {
          var a = hr;
          let b;
          b = class extends sL {
            constructor() {
              var a = hr,
                b = a[0];
              var c;
              c = arguments;
              super(...c), this["rQ"] = "", this["oQ"] = 0, this["lQ"] = 0, this["cQ"] = "资源加载中", this["uQ"] = 0
            }
          };
          ! function() {
            var a = hr,
              c = hu,
              d = a[0],
              e = a[8],
              f = "defineProperty",
              qKd = "value",
              qKe = "enumerable",
              qKf = "configurable",
              qKg = "writable";
            w1_ep: for (let g of mS) {
              switch (g) {
                case 0:
                  Object["defineProperty"](b["prototype"], "kQ", {
                    ["value"]() {
                      return p2(this, null, function*() {
                        var a = hr,
                          b = a[0];
                        this["cQ"] = "登录中", this["uQ"] = 0, this["yQ"]();
                        (yield qK["Ia"](r2["instance"]()["en"]())) || console["warn"]("[LoadScene] 登录超时或失败，将使用本地数据继续")
                      })
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case 1:
                  Object["defineProperty"](b["prototype"], "onAwake", {
                    ["value"]() {
                      var a = hr,
                        b = a[6],
                        c = a[2],
                        d = a[0],
                        e = a[4],
                        f = a[25],
                        g = "instance",
                        h = "init",
                        i = "pQ",
                        j = "progressBar",
                        k = "yQ";
                      r2["instance"]()["init"](), nx["instance"]()["init"](), qs["instance"]()["init"](), qK["instance"]()["init"](r2["instance"]()["Zl"]()), this["pQ"] = new Laya["Sprite"], this["progressBar"]["mask"] = this["pQ"], this["pQ"]["graphics"]["drawRect"](0, 0, 0, this["progressBar"]["height"], "#fff"), qs["instance"]()["Kf"](this["zhao"], ["resources/loading/zhao0.png", "resources/loading/zhao1.png", "resources/loading/zhao2.png"], hu[81]), this["yQ"](), this["cQ"] = "分包加载中", this["uQ"] = 0, this["yQ"](), this["fQ"](), this["gQ"]()
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case 2:
                  Object["defineProperty"](b["prototype"], "onComplete", {
                    ["value"]() {
                      return p2(this, null, function*() {
                        var a = hr,
                          b = a[0],
                          c = a[7],
                          d = "instance";
                        yield n6["instance"]()["loadShareConfig"](), sE["instance"]()["init"](), this["bQ"]() || sF["instance"]()["bn"]("MainScene", !0, null, () => {
                          sF["instance"]()["Un"]("LoadScene")
                        })
                      })
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case 3:
                  Object["defineProperty"](b["prototype"], "SQ", {
                    ["value"]() {
                      var a = hr,
                        b = a[0];
                      this["cQ"] = "同步游戏数据", this["uQ"] = 0, this["yQ"](), qK["instance"]()["Ra"]()
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case 4:
                  Object["defineProperty"](b["prototype"], "fQ", {
                    ["value"]() {
                      var a = hr,
                        b = "uQ";
                      Laya["timer"]["loop"](hu[176], this, () => {
                        this["uQ"] = (this["uQ"] + 1) % 4, this["yQ"]()
                      })
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case 5:
                  "use strict";
                  break;
                case 6:
                  Object["defineProperty"](b["prototype"], "_Q", {
                    ["value"]() {
                      var a = hr,
                        b = "lQ";
                      this["lQ"] = Math["min"](.95, this["lQ"] + .02), this["dQ"]()
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case 7:
                  Object["defineProperty"](b["prototype"], "vQ", {
                    ["value"]() {},
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case 8:
                  Object["defineProperty"](b["prototype"], "yQ", {
                    ["value"]() {
                      var a = hr,
                        b = a[0];
                      this["loadingTxt"]["text"] = this["cQ"] + "."["repeat"](this["uQ"])
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case 9:
                  Object["defineProperty"](b["prototype"], "mQ", {
                    ["value"]() {
                      return p2(this, null, function*() {
                        var a = hr,
                          b = a[0],
                          c = a[2],
                          d = "lQ",
                          e = "dQ",
                          f = "timer",
                          g = "_Q";
                        this["oQ"] = 1, this["lQ"] = 0, this["cQ"] = "平台初始化中", this["uQ"] = 0, this["yQ"](), this["dQ"](), uq["instance"]()["init"](), Laya["timer"]["loop"](hu[65], this, this["_Q"]);
                        try {
                          yield this["kQ"](), this["SQ"](), this["xQ"]()
                        } catch (b) {
                          console["warn"]("[LoadScene] startup platform tasks failed", b)
                        } finally {
                          Laya["timer"]["clear"](this, this["_Q"])
                        }
                        this["lQ"] = 1, this["dQ"](), this["onComplete"]()
                      })
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case 10:
                  Object["defineProperty"](b["prototype"], "xQ", {
                    ["value"]() {
                      var a = hr,
                        b = a[0],
                        c = "instance";
                      uq["instance"]()["player"]["finalizeRankAfterLoad"]() && qK["instance"]()["Aa"]() > 0 && qK["instance"]()["Fa"](!0)
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[7]:
                  Object["defineProperty"](b["prototype"], "LQ", {
                    ["value"](a, b) {
                      var c = hr,
                        d = c[0];
                      this["lQ"] = b <= 0 ? 1 : Math["min"](1, a / b), this["dQ"]()
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[9]:
                  Object["defineProperty"](b["prototype"], "gQ", {
                    ["value"]() {
                      return p2(this, null, function*() {
                        var a = hr,
                          b = a[3],
                          c = a[0],
                          d = a[24],
                          e = a[2],
                          f = "log",
                          g = "lQ",
                          h = "dQ",
                          i = "instance",
                          j = "Handler",
                          k = "create";
                        console["log"]("启动主流程"), this["lQ"] = 0, this["dQ"]();
                        try {
                          console["log"]("[LoadScene] startLoadFlow1"), yield r2["instance"]()["hn"]((a, b) => {
                            this["LQ"](a, b)
                          }), console["log"]("[LoadScene] startLoadFlow2")
                        } catch (b) {
                          console["warn"]("[LoadScene] preload platform tasks failed", b)
                        }
                        this["lQ"] = 1, this["dQ"](), this["cQ"] = "资源加载中", this["uQ"] = 0, this["yQ"](), this["oQ"] = 0, this["lQ"] = 0, this["dQ"](), Laya["loader"]["load"](pi["instance"]()["SH"], Laya["Handler"]["create"](this, this["mQ"]), Laya["Handler"]["create"](this, this["wQ"], null, !1))
                      })
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[3]:
                  Object["defineProperty"](b["prototype"], "wQ", {
                    ["value"](a) {
                      var b = hr,
                        c = b[0];
                      this["oQ"] = a, this["dQ"]()
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[14]:
                  Object["defineProperty"](b["prototype"], "dQ", {
                    ["value"]() {
                      var a = hr,
                        b = a[0],
                        c = a[2],
                        d = "pQ",
                        e = "graphics",
                        f = "progressBar",
                        g = "width";
                      let h;
                      h = .85 * this["oQ"] + .15 * this["lQ"];
                      this["pQ"]["graphics"]["clear"](), this["pQ"]["graphics"]["drawRect"](0, 0, this["progressBar"]["width"] * h, this["progressBar"]["height"], "#fff"), this["zhao"]["x"] = this["progressBar"]["width"] * h
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[12]:
                  Object["defineProperty"](b["prototype"], "bQ", {
                    ["value"]() {
                      var a = hr,
                        b = a[0],
                        c = "instance";
                      return !!r2["instance"]()["ac"]() && (qZ["instance"]()["Ou"](), vb["instance"]()["xA"](), sF["instance"]()["bn"]("MatchScene", !0, null, () => {
                        sF["instance"]()["Un"]("LoadScene")
                      }), !0)
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                default:
                  break
              }
            }
          } ["bind"](b)();
          return b
        } ["bind"](this)["apply"]();
