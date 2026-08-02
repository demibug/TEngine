        let b;
        b = class extends rc {
          constructor() {
            var a = hr,
              b = a[0];
            var c;
            c = arguments;
            super(...c), this["objectType"] = 1, this["r_"] = 0, this["addAttPower"] = 0, this["H_"] = 0, this["W_"] = 0, this["j_"] = 1, this["level"] = 1, this["Wm"] = 0
          }
          resetData() {
            var a = hr,
              b = a[0];
            super["resetData"](), this["H_"] = 0, this["W_"] = 0
          }
          X_(a = 1, b = !0) {
            var c = hr,
              d = hu,
              e = c[0],
              f = c[3],
              g = "instance",
              h = "Oc",
              i = "mp",
              j = "type",
              k = "level",
              l = "event",
              m = "hs",
              n = "onLevelChange";
            super["X_"](a, b), this["v_"] = uq["instance"]()["Oc"]["mp"][this["type"]]["kp"] / uq["instance"]()["Oc"]["fp"][this["level"] - 1], this["m_"] = uq["instance"]()["Oc"]["mp"][this["type"]]["_p"] * uq["instance"]()["Oc"]["gp"][this["level"] - 1], a > 0 ? (oc["instance"]["event"](sS["hs"], this, d[11]), oc["instance"]["event"](sS["hs"], this, d[13]), oc["instance"]["event"](sS["ns"], this["id"]), this["event"]("onLevelChange", [this["level"], !0]), pC["instance"]()["playSound"]("soldier_merge_upgrade")) : this["event"]("onLevelChange", [this["level"], !1])
          }
          gameOver() {
            var a = hr,
              b = a[6],
              c = a[0],
              d = a[2],
              e = "T_";
            super["gameOver"](), this["q_"] = !1, this["root"] = null, this["currentState"] = "UnitIdle", this["T_"] && (this["T_"]["rotation"] = 0, this["T_"]["offAll"](), this["T_"]["stop"](), this["T_"]["visible"] = !1, nz["instance"]()["Zd"](this["T_"], this["Q_"]), this["T_"]["removeSelf"](), this["T_"] = null)
          }
        };
        ! function() {
          var a = hr,
            c = hu,
            d = a[0],
            e = c[14],
            f = c[7],
            g = c[13],
            h = c[12],
            i = c[4],
            j = c[3],
            k = c[9],
            l = c[5],
            m = c[11],
            n = "defineProperty",
            oAA = "value",
            oAB = "enumerable",
            oAC = "configurable",
            oAD = "writable",
            oAE = "get";
          let o = 0,
            p = u9;
          w1_cQ: while (o < c[1]) {
            ++o;
            switch (p) {
              case e:
                Object["defineProperty"](b["prototype"], "G_", {
                  ["value"]() {},
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                p = f;
                break;
              case 7:
                "use strict";
                p = 4;
                break;
              case 5:
                Object["defineProperty"](b["prototype"], "yw", {
                  ["value"]() {
                    var a = hr,
                      b = a[0];
                    switch (this["currentState"]) {
                      case "UnitIdle":
                        this["Z_"]();
                        break;
                      case "UnitAttack":
                        this["K_"]()
                    }
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                p = g;
                break;
              case f:
                Object["defineProperty"](b["prototype"], "tk", {
                  ["value"]() {
                    var a = hr,
                      b = a[3],
                      c = a[0],
                      d = "Point",
                      e = "TEMP",
                      f = "Oc";
                    Laya["Point"]["TEMP"]["setTo"](this["Oc"]["width"] / 2, this["Oc"]["height"] / 2), this["Oc"]["localToGlobal"](Laya["Point"]["TEMP"]), qs["instance"]()["wg"](!0, this["wp"], Laya["Point"]["TEMP"]["x"], Laya["Point"]["TEMP"]["y"])
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                p = h;
                break;
              case 3:
                Object["defineProperty"](b["prototype"], "J_", {
                  ["value"]() {},
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                p = 9;
                break;
              case 6:
                Object["defineProperty"](b["prototype"], "h_", {
                  ["value"]() {},
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                p = 8;
                break;
              case i:
                Object["defineProperty"](b["prototype"], "N_", {
                  ["get"]() {
                    return 1 / this["z_"]
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true
                });
                p = j;
                break;
              case k:
                Object["defineProperty"](b["prototype"], "A_", {
                  ["value"](a) {
                    var b = hr,
                      c = b[2],
                      d = b[0],
                      e = "type",
                      f = "instance",
                      g = "Oc",
                      h = "id",
                      i = "mp";
                    this["type"] = uq["instance"]()["Oc"]["Lp"]["findIndex"](b => a === b), this["id"] = uq["instance"]()["xy"](), this["root"] = this["Oc"], this["w_"] = uq["instance"]()["Oc"]["mp"][this["type"]]["wp"] * uq["instance"]()["map"]["ye"], this["m_"] = uq["instance"]()["Oc"]["mp"][this["type"]]["_p"], this["v_"] = uq["instance"]()["Oc"]["mp"][this["type"]]["kp"], this["Oc"]["name"] = "soldier_" + this["id"], this["V_"](), 1 == this["l_"] && (this["q_"] = !0)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                p = 10;
                break;
              case g:
                Object["defineProperty"](b["prototype"], "pw", {
                  ["value"](a) {
                    var b = hr;
                    if ("UnitAttack" === a) this["J_"]()
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                p = 0;
                break;
              case 8:
                Object["defineProperty"](b["prototype"], "onMoved", {
                  ["value"]() {
                    var a = hr,
                      b = "q_",
                      c = "changeState",
                      d = "UnitIdle";
                    1 == this["l_"] ? (this["q_"] = !0, this["changeState"]("UnitIdle")) : (this["q_"] = !1, this["changeState"]("UnitIdle"))
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                p = k;
                break;
              case 4:
                Object["defineProperty"](b["prototype"], "_p", {
                  ["get"]() {
                    var a = hr,
                      b = a[0];
                    let c;
                    c = this["m_"] + this["addAttPower"];
                    return this["nm"] ? c : c * uq["instance"]()["au"]["xi"]
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true
                });
                p = l;
                break;
              case 10:
                Object["defineProperty"](b["prototype"], "V_", {
                  ["value"]() {
                    var a = hr,
                      b = a[0],
                      c = a[6],
                      d = a[2],
                      e = a[5],
                      f = "T_",
                      g = "sw",
                      h = "map";
                    this["T_"] || (this["T_"] = nz["instance"]()["$d"](this["Q_"]), this["T_"]["name"] = "sp"), this["Oc"]["addChild"](this["T_"]), this["T_"]["visible"] = !0, this["T_"]["play"]("zhan", !0), this["T_"]["anchorX"] = .5, this["T_"]["anchorY"] = .5, this["T_"]["pos"](this["sw"]["map"]["ye"] / 2, this["sw"]["map"]["gridHei"] / 2)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                p = 2;
                break;
              case 0:
                Object["defineProperty"](b["prototype"], "Z_", {
                  ["value"]() {
                    var a = hr,
                      b = a[2],
                      c = "T_";
                    this["T_"] && (this["T_"]["playbackRate"](1), this["T_"]["play"]("zhan", !0))
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                p = m;
                break;
              case l:
                Object["defineProperty"](b["prototype"], "wp", {
                  ["get"]() {
                    var a = hr,
                      b = a[0];
                    return this["w_"] + this["H_"] * uq["instance"]()["map"]["ye"]
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true
                });
                p = 1;
                break;
              case 1:
                Object["defineProperty"](b["prototype"], "z_", {
                  ["get"]() {
                    var a = hr,
                      b = a[0],
                      c = a[10],
                      d = "W_",
                      e = "type",
                      f = "j_",
                      g = "v_",
                      h = "T_";
                    return this["W_"] < 0 && (this["W_"] = 0), -1 == this["type"] ? this["j_"] = 0 : this["j_"] = uq["instance"]()["Oc"]["mp"][this["type"]]["kp"] / (this["v_"] / (1 + this["W_"])), this["T_"] && "UnitAttack" == this["currentState"] && this["T_"]["playbackRate"](this["j_"]), this["v_"] / (1 + this["W_"])
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true
                });
                p = i;
                break;
              case 9:
                Object["defineProperty"](b["prototype"], "idle", {
                  ["value"](a) {},
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                p = e;
                break;
              case m:
                Object["defineProperty"](b["prototype"], "K_", {
                  ["value"]() {
                    var a = hr,
                      b = "T_";
                    this["T_"] && this["T_"]["playbackRate"](this["j_"])
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                p = 3;
                break;
              case j:
                Object["defineProperty"](b["prototype"], "i_", {
                  ["value"]() {},
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                p = 6;
                break;
              case h:
                Object["defineProperty"](b["prototype"], "hk", {
                  ["value"]() {
                    var a = hr;
                    qs["instance"]()["wg"](!1)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                p = 7;
                break;
              case 2:
                Object["defineProperty"](b["prototype"], "update", {
                  ["value"](a) {
                    var b = hr;
                    if ("UnitIdle" === this["currentState"]) this["idle"](a)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                p = 5;
                break;
              default:
                break
            }
          }
        } ["bind"](b)();
        return b
      } ["bind"](this)["apply"](), sd = class {}, rP = class extends sd {
