        } ["bind"](this)["apply"](), sE = function() {
          var a = hr;
          let b;
          b = class extends qU {
            constructor() {
              var a;
              a = arguments;
              super(...a), this["_j"] = !1
            }
          };
          ! function() {
            var a = hr,
              c = a[2],
              d = a[0],
              e = "defineProperty",
              q3S = "value",
              q3T = "enumerable",
              q3U = "configurable",
              q3V = "writable";
            let f = 0,
              g = lV;
            w1_dW: while (f < 10) {
              ++f;
              switch (g) {
                case 9:
                  Object["defineProperty"](b["prototype"], "test", {
                    ["value"]() {
                      var a = hr;
                      Laya["Scene"]["open"]("scene/SoldierRangeScene.ls")
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  g = 5;
                  break;
                case 2:
                  Object["defineProperty"](b["prototype"], "init", {
                    ["value"]() {
                      var a = hr,
                        b = hu,
                        c = a[10],
                        d = a[0],
                        e = a[9],
                        f = a[3],
                        g = a[1],
                        h = a[4],
                        i = a[6],
                        j = b[45],
                        k = "instance",
                        l = "init",
                        m = "Ry",
                        n = "opened",
                        o = "GMDialog";
                      if (Laya["InputManager"]["multiTouchEnabled"] = !1, this["addEventListener"](), qR["tj"](), ry["instance"]()["init"](), pi["instance"]()["init"](), pp["instance"]()["init"](), qi["instance"]()["init"](), nz["instance"]()["init"](), tR["instance"]()["init"](), ut["instance"]()["init"](), rw["instance"]()["init"](), sF["instance"]()["init"](), vN["instance"]()["init"](), na["instance"]()["init"](), vi["instance"]()["init"](), vc["instance"]()["init"](), sO["instance"]()["init"](), vS["instance"]()["init"](), r0["instance"]()["init"](), ph["instance"]()["init"](), vd["instance"]()["init"](), vU["instance"]()["init"](), qZ["instance"]()["init"](), vb["instance"]()["init"](), sA["instance"]()["init"](), qx["instance"]()["init"](), vA["instance"]()["init"](), r7["instance"]()["init"](), p0["instance"]()["init"](), sJ["instance"]()["init"](), pC["instance"]()["init"](uq["instance"]()["Ry"]()["musicVolume"], uq["instance"]()["Ry"]()["soundVolume"]), vT["instance"]()["init"](), this["_j"]) {
                        let c;
                        c = new Laya["Image"]("resources/img/commonUI/tipBg.png");
                        c["size"](j, j), c["pos"](b[294], b[167]), c["alpha"] = .8, Laya["stage"]["addChild"](c), c["on"](Laya["Event"]["CLICK"], this, () => {
                          c["opened"] ? (c["opened"] = !1, sF["instance"]()["Tn"]("GMDialog")) : (c["opened"] = !0, sF["instance"]()["Bn"]("GMDialog", !1))
                        })
                      }
                      console["log"]("当前玩家天数", np["Gs"](uq["instance"]()["player"]["registerTime"], Date["now"]()) + 1)
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  g = 4;
                  break;
                case 0:
                  Object["defineProperty"](b["prototype"], "Sj", {
                    ["value"](a) {
                      var b = hr,
                        c = b[0],
                        d = "instance";
                      r2["instance"]()["gn"](), sF["instance"]()["Tn"]("AuthorizeDialog"), qZ["instance"]()["zu"](a)
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  g = 3;
                  break;
                case 8:
                  Object["defineProperty"](b["prototype"], "xj", {
                    ["value"]() {
                      var a = hr,
                        b = a[0],
                        c = "instance";
                      return vT["instance"]()["KG"](), this["startGame"]()["then"](a => (vT["instance"]()["JG"](), a))
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  g = 1;
                  break;
                case 6:
                  "use strict";
                  g = 2;
                  break;
                case 4:
                  Object["defineProperty"](b["prototype"], "addEventListener", {
                    ["value"]() {
                      var a = hr,
                        b = a[0],
                        c = "instance",
                        d = "on";
                      oc["instance"]["on"](sS["l"], this, this["gameOver"]), oc["instance"]["on"](sS["ks"], this, this["kj"]), oc["instance"]["on"](sS["Ss"], this, this["Sj"])
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  g = 7;
                  break;
                case 3:
                  Object["defineProperty"](b["prototype"], "startGame", {
                    ["value"]() {
                      var a = hr,
                        b = a[0],
                        c = a[2],
                        d = "instance",
                        e = "startGame";
                      return qK["instance"]()["Oa"]({
                        ["fail"]: b => {
                          console["warn"]("[Server] start game report failed", b)
                        }
                      }), uq["instance"]()["startGame"](), tg["instance"]()["Wl"](), vN["instance"]()["startGame"](), qZ["instance"]()["startGame"](), vU["instance"]()["startGame"](), vi["instance"]()["startGame"](), vc["instance"]()["startGame"](), qs["instance"]()["startGame"](), vd["instance"]()["startGame"](), r2["instance"]()["startGame"](), new Promise(c => {
                        sF["instance"]()["bn"]("BattleScene", !1, null, a => {
                          vS["instance"]()["startGame"](), ph["instance"]()["startGame"](), qi["instance"]()["startGame"](), c(a)
                        })
                      })
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  g = 8;
                  break;
                case 5:
                  Object["defineProperty"](b["prototype"], "Mj", {
                    ["value"]() {},
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  g = 6;
                  break;
                case 1:
                  Object["defineProperty"](b["prototype"], "gameOver", {
                    ["value"](a, b = !1) {
                      var c = hr,
                        d = c[0],
                        e = c[3],
                        f = c[2],
                        g = c[6],
                        h = "instance",
                        i = "au",
                        j = "li",
                        k = "gameOver",
                        l = "event";
                      let m, n, o;
                      uq["instance"]()["au"]["Qi"] = !0;
                      m = uq["instance"]()["au"]["li"], n = tg["instance"]();
                      a ? n["jl"](m) : b ? n["Nl"](m) : n["zl"](m), vT["instance"]()["gameOver"]();
                      o = uq["instance"]()["au"]["li"];
                      uq["instance"]()["gameOver"](a), a && r2["instance"]()["wn"](uq["instance"]()["player"]["win"]), vU["instance"]()["gameOver"](), vS["instance"]()["gameOver"](), ph["instance"]()["gameOver"](), sO["instance"]()["gameOver"](), oc["instance"]["event"](sS["It"]), vN["instance"]()["gameOver"](), vi["instance"]()["gameOver"](), vc["instance"]()["gameOver"](), qs["instance"]()["gameOver"](), qZ["instance"]()["gameOver"](a), qK["instance"]()["Ya"](a, {
                        ["fail"]: a => {
                          console["warn"]("[Server] end game report failed", a)
                        }
                      }), oc["instance"]["event"](sS["o"], a), qx["instance"]()["gameOver"](a), sF["instance"]()["bn"]("GameOverScene", !1, {
                        ["isWin"]: a,
                        ["bj"]: b,
                        ["round"]: o
                      }, () => {
                        sJ["instance"]()["gameOver"]()
                      }), vb["instance"]()["gameOver"](a), r0["instance"]()["gameOver"](), sA["instance"]()["gameOver"](), vA["instance"]()["gameOver"](), tI["clearAllDeferredTrails"](), vd["instance"]()["gameOver"](), tR["instance"]()["gu"](), r2["instance"]()["ln"](), qK["instance"]()["Fa"]()
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  g = 9;
                  break;
                case 7:
                  Object["defineProperty"](b["prototype"], "kj", {
                    ["value"]() {
                      var a = hr;
                      sF["instance"]()["Bn"]("AuthorizeDialog")
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  g = 0;
                  break;
                default:
                  break
              }
            }
          } ["bind"](b)();
          return b
