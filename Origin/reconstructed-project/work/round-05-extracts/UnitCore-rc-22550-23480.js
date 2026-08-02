            ["configurable"]: true,
            ["writable"]: true
          })
        } ["bind"](b)();
        return b
      } ["bind"](this)["apply"]();
      continue
    } else if (3 == b) {
      p4 = function() {
        let a;
        a = class extends rD {
          constructor() {
            var a = hr,
              b = a[0];
            var c;
            c = arguments;
            super(...c), this["Wb"] = 3, this["Ub"] = !0, this["bM"] = !0, this["MM"] = 2, this["iM"] = !0, this["hM"] = 0, this["range"] = 0, this["PM"] = 0, this["AM"] = !1
          }
          init(a, b) {
            var c = hr,
              d = c[2],
              e = "EM";
            super["init"](a, b), this["EM"] || (this["EM"] = new Laya["Image"], this["EM"]["skin"] = "resources/img/props/rangeUp.png"), this["EM"]["visible"] = !1
          }
          update(a) {
            var b = hr;
            super["update"](a), this["DM"](a)
          }
          reset() {
            var a = hr,
              b = a[3];
            super["reset"](), this["Ab"]["alpha"] = 1
          }
          gameOver() {
            var a = hr,
              b = a[0];
            super["gameOver"](), this["PM"] = 0, this["Ab"]["alpha"] = 1
          }
        };
        ! function() {
          "use strict";
          var b = hr,
            c = b[0],
            d = "defineProperty",
            oxm = "value",
            oxn = "enumerable",
            oxo = "configurable",
            oxp = "writable";
          Object["defineProperty"](a["prototype"], "Ib", {
            ["value"](a, b) {
              var c = hr,
                d = c[0],
                e = c[5],
                f = c[1],
                g = c[3],
                h = "instance",
                i = "Cy",
                j = "range",
                k = "EM";
              let l, m, n, o;
              n = b["ub"](a["containerType"], this["nm"])["getItem"](a["x"], a["y"]);
              m = !1;
              if (n instanceof ok) o = n, l = uq["instance"]()["Cy"](n["Oc"], !0), m = !1;
              else if (n instanceof qo) {
                let a;
                a = vc["instance"]()["BM"]["get"](n["Ux"]);
                o = a, l = uq["instance"]()["Cy"](a["general"], !0), m = !0
              }
              this["hM"] = o["id"], this["range"] = o["wp"], this["AM"] = m, this["EM"]["visible"] = !0, this["EM"]["size"](2 * this["range"], 2 * this["range"]), this["EM"]["anchor"](.5, .5), this["EM"]["scale"](1, 1), this["EM"]["alpha"] = 0, oc["instance"]["event"](sS["bt"], this["EM"]), this["EM"]["pos"](l["x"], l["y"]), this["PM"] = 1, this["reset"]()
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](a["prototype"], "DM", {
            ["value"](a) {
              var b = hr,
                c = hu,
                d = b[0],
                e = c[81],
                f = "PM",
                g = "EM",
                h = "alpha",
                i = "scaleX",
                j = "hM",
                k = "instance";
              if (1 == this["PM"]) this["EM"]["alpha"] += a / e, this["EM"]["alpha"] >= 1 && (this["PM"] = 2);
              else if (2 == this["PM"]) this["EM"]["scaleX"] += a / e, this["EM"]["scaleY"] += a / e, this["EM"]["scaleX"] >= 2 && (this["PM"] = 3);
              else if (3 == this["PM"] && (this["EM"]["alpha"] -= a / c[176], this["EM"]["alpha"] <= 0)) {
                let a;
                a = this["hM"];
                a = (this["AM"], this["hM"]), vd["instance"]()["applyBuff"](a, 2, 1, !0), vc["instance"]()["IM"](a, 0, 2, 1, !0), this["EM"]["removeSelf"](), this["PM"] = 0
              }
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          })
        } ["bind"](a)();
        return a
      } ["bind"](this)["apply"]();
      continue
    } else if (4 == b) {
      pP["xk"] = "BulletMovementBase";
      continue
    } else if (5 == b) {
      ri = rb, oW = class extends qU {}, sc = function() {
        let a;
        a = class extends oW {};
        ! function() {
          "use strict";
          var b = hr,
            c = b[5],
            d = "defineProperty",
            oxK = "value",
            oxL = "enumerable",
            oxM = "configurable",
            oxN = "writable";
          Object["defineProperty"](a["prototype"], "init", {
            ["value"]() {},
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](a["prototype"], "produce", {
            ["value"](a) {
              var b = hr;
              return Laya["Pool"]["createByClass"](a)
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](a["prototype"], "recover", {
            ["value"](a) {
              var b = hr;
              Laya["Pool"]["recoverByClass"](a)
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          })
        } ["bind"](a)();
        return a
      } ["apply"](), rc = function() {
        var a = hr;
        let b;
        b = class extends ri {
          constructor() {
            var a = hr,
              b = a[0],
              c = "Point";
            var d;
            d = arguments;
            super(...d), this["level"] = 1, this["r_"] = 0, this["o_"] = 0, this["l_"] = 0, this["c_"] = new Laya["Point"], this["u_"] = new Laya["Point"], this["p_"] = 1, this["y_"] = !1, this["f_"] = {
              ["ug"]: {
                ["x"]: 0,
                ["y"]: 0
              },
              ["p1"]: {
                ["x"]: 0,
                ["y"]: 0
              },
              ["p2"]: {
                ["x"]: 0,
                ["y"]: 0
              },
              ["time"]: 0
            }, this["g_"] = 1, this["L_"] = [], this["m_"] = 0, this["w_"] = 0, this["v_"] = 1, this["__"] = !1, this["k_"] = !1, this["S_"] = !1, this["Nv"] = 1, this["x_"] = !1
          }
          gameOver() {
            var a = hr,
              b = a[6],
              c = a[0],
              d = a[2],
              e = a[3],
              f = a[5],
              g = a[4],
              h = "instance",
              i = "wa",
              j = "Oc",
              k = "id",
              l = "scale",
              m = "removeSelf",
              n = "T_",
              o = "recover";
            super["gameOver"](), this["Pw"](), nx["instance"]()["wa"](this["Oc"]["name"]), nx["instance"]()["wa"](this["id"] + "_jump"), this["C_"] = null, Laya["timer"]["clearAll"](this), oc["instance"]["event"](sS["es"], this["id"]), this["p_"] = 1, this["f_"]["time"] = 0, this["Oc"]["x"] = 0, this["Oc"]["y"] = 0, this["Oc"]["anchorX"] = 0, this["Oc"]["anchorY"] = 0, this["Oc"]["scale"](1, 1), this["Oc"]["removeSelf"](), this["Oc"]["filters"] = null, Laya["Tween"]["killAll"](this["Oc"]), this["T_"]["visible"] = !0, this["T_"]["rotation"] = 0, this["T_"]["removeSelf"](), this["T_"]["scale"](1, 1), this["Oc"]["offAll"](), rw["instance"]()["recover"]("soldier", this["Oc"]), this["resetData"](), sc["instance"]()["recover"](this), this["L_"]["length"] = 0, this["y_"] = !1, this["g_"] = 1
          }
        };
        ! function() {
          var a = hr,
            c = hu,
            d = a[0],
            e = "defineProperty",
            oyb = "value",
            oyc = "enumerable",
            oyd = "configurable",
            oye = "writable";
          for (let f of u5) {
            if (-1 == f) {} else if (0 == f) {
              Object["defineProperty"](b["prototype"], "I_", {
                ["value"]() {
                  var a = hr,
                    b = a[0],
                    c = a[3],
                    d = "T_";
                  nx["instance"]()["wa"](this["id"] + "_jump"), this["C_"] = null, this["T_"] && !this["T_"]["destroyed"] && Laya["Tween"]["killAll"](this["T_"]), this["y_"] = !1, this["p_"] = 1
                },
                ["enumerable"]: false,
                ["configurable"]: true,
                ["writable"]: true
              });
              continue
            } else if (1 == f) {
              Object["defineProperty"](b["prototype"], "Pw", {
                ["value"](a = 0, b = -1, c = -1) {
                  var d = hr,
                    e = d[0],
                    f = "l_",
                    g = "u_";
                  this["o_"] = this["l_"], this["l_"] = a, this["c_"]["copy"](this["u_"]), this["u_"]["setTo"](b, c)
                },
                ["enumerable"]: false,
                ["configurable"]: true,
                ["writable"]: true
              });
              continue
            } else if (2 == f) {
              Object["defineProperty"](b["prototype"], "am", {
                ["value"]() {
                  return this["Oc"]
                },
                ["enumerable"]: false,
                ["configurable"]: true,
                ["writable"]: true
              });
              continue
            } else if (3 == f) {
              Object["defineProperty"](b["prototype"], "resetData", {
                ["value"]() {
                  var a = hr,
                    b = a[0],
                    c = "setTo";
                  this["level"] = 1, this["r_"] = 0, this["M_"]["visible"] = !0, this["o_"] = 0, this["l_"] = 0, this["c_"]["setTo"](-1, -1), this["u_"]["setTo"](-1, -1)
                },
                ["enumerable"]: false,
                ["configurable"]: true,
                ["writable"]: true
              });
              continue
            } else if (4 == f) {
              Object["defineProperty"](b["prototype"], "Jm", {
                ["value"]() {
                  var a = hr;
                  return t1["zn"]["Soldier"]
                },
                ["enumerable"]: false,
                ["configurable"]: true,
                ["writable"]: true
              });
              continue
            } else if (5 == f) {
              Object["defineProperty"](b["prototype"], "D_", {
                ["value"](a) {
                  var b = hr,
                    c = hu,
                    d = b[0],
                    e = b[4],
                    f = b[3],
                    g = b[5],
                    h = b[6],
                    i = c[22],
                    j = c[81],
                    k = "R_",
                    l = "p_",
                    m = "Oc",
                    n = "targetX",
                    o = "T_",
                    p = "scaleX",
                    q = "y_",
                    r = "to",
                    s = "scaleY",
                    t = "duration",
                    u = "f_",
                    v = "ug",
                    w = "p1",
                    x = "targetY",
                    y = "p2",
                    z = "I_",
                    A = "time",
                    B = "g_",
                    C = "instance",
                    D = "C_";
                  if (this["R_"]()) {
                    if (1 == this["p_"]) {
                      if (this["Oc"]["x"] < this["targetX"] ? this["T_"]["scaleX"] = -1 : this["T_"]["scaleX"] = 1, this["y_"]) return;
                      this["Oc"]["zIndex"] = t1["sr"], this["y_"] = !0, Laya["Tween"]["create"](this["T_"])["to"]("scaleY", .7)["duration"](i)["chain"]()["to"]("scaleY", 1)["duration"](i)["then"](() => {
                        this["R_"]() ? (this["p_"] = 2, this["y_"] = !1, this["f_"]["ug"]["x"] = this["Oc"]["x"], this["f_"]["ug"]["y"] = this["Oc"]["y"], this["f_"]["p1"]["x"] = this["Oc"]["x"] + (this["targetX"] - this["Oc"]["x"]) / 2, this["f_"]["p1"]["y"] = Math["min"](this["targetY"], this["Oc"]["y"]) - j, this["f_"]["p2"]["x"] = this["targetX"], this["f_"]["p2"]["y"] = this["targetY"]) : this["I_"]()
                      }, this)
                    } else if (2 == this["p_"]) {
                      if (!this["R_"]()) return void this["I_"]();
                      this["f_"]["time"] += a / (j * this["g_"]), np["Us"](this["f_"]["ug"], this["f_"]["p1"], this["f_"]["p2"], this["Oc"], this["f_"]["time"]) ? (this["Oc"]["x"] = this["targetX"], this["Oc"]["y"] = this["targetY"], this["p_"] = 3) : this["f_"]["time"] < .5 ? (this["T_"]["scaleX"] += .02, this["T_"]["scaleY"] += .02) : this["T_"]["scaleX"] > 1 && (this["T_"]["scaleX"] -= .02, this["T_"]["scaleY"] -= .02)
                    } else if (3 == this["p_"]) {
                      let a;
                      if (!this["R_"]()) return void this["I_"]();
                      this["p_"] = 1, this["g_"] = 1, this["f_"]["time"] = 0, this["T_"]["scale"](1, 1), pC["instance"]()["playSound"]("soldier_set");
                      a = this["C_"];
                      this["C_"] = null, a && a(), nx["instance"]()["wa"](this["id"] + "_jump"), this["b_"]()
                    }
                  } else this["I_"]()
                },
                ["enumerable"]: false,
                ["configurable"]: true,
                ["writable"]: true
              });
              continue
            } else if (6 == f) {
              Object["defineProperty"](b["prototype"], "X_", {
                ["value"](a = 1, b = !0) {
                  var c = hr,
                    d = hu,
                    e = c[1],
                    f = c[0],
                    g = c[6],
                    h = d[43],
                    i = "level",
                    j = "sw",
                    k = "Oc",
                    l = "Op";
                  let m;
                  m = a > 0;
                  this["level"] = Math["min"](5, Math["max"](this["level"] + a, 1)), this["r_"] = this["sw"]["Oc"]["Ip"][this["level"] - 1], this["M_"]["value"] = this["level"]["toString"](), this["x_"] || (this["Op"] = this["sw"]["Oc"]["Op"]["get"](this["P_"])[this["level"] - 1]), m && (b && qs["instance"]()["Gf"](this["Oc"], h, h), this["G_"]())
                },
                ["enumerable"]: false,
                ["configurable"]: true,
                ["writable"]: true
              });
              continue
            } else if (7 == f) {
              "use strict";
              continue
            } else if (8 == f) {
              Object["defineProperty"](b["prototype"], "init", {
                ["value"](a, b) {
                  var c = hr,
                    d = c[0],
                    e = c[5],
                    f = c[1],
                    g = c[4],
                    h = c[2],
                    i = "sw",
                    j = "instance",
                    k = "Oc",
                    l = "Op",
                    m = "M_",
                    n = "l_";
                  this["sw"] = uq["instance"](), this["nm"] = b, this["Oc"] = rw["instance"]()["getItem"]("soldier", this), this["Op"] = this["sw"]["Oc"]["Op"]["get"](a)[0], this["x_"] = !(this["sw"]["Oc"]["Lp"]["indexOf"](a) >= 0), this["M_"] = this["Oc"]["getChildByName"]("lvl"), this["M_"]["value"] = "1", this["Oc"]["zIndex"] = t1["entityZIndexFromPixelY"](this["Oc"]["y"], this["sw"]["map"]["gridHei"], this["Jm"]()), this["P_"] = a, (3 == this["l_"] || 1 == this["l_"]) && this["changeState"]("none"), this["A_"](a), nx["instance"]()["La"](this["Oc"]["name"], this, this["update"])
                },
                ["enumerable"]: false,
                ["configurable"]: true,
                ["writable"]: true
              });
              continue
            } else if (9 == f) {
              Object["defineProperty"](b["prototype"], "R_", {
                ["value"]() {
                  var a = hr,
                    b = a[0],
                    c = "Oc",
                    d = "destroyed",
                    e = "T_";
                  return !(!this["Oc"] || this["Oc"]["destroyed"] || !this["T_"] || this["T_"]["destroyed"])
                },
                ["enumerable"]: false,
                ["configurable"]: true,
                ["writable"]: true
              });
              continue
            }
            if (10 == f) {
              Object["defineProperty"](b["prototype"], "B_", {
                ["value"]() {
                  var a = hr,
                    b = a[2],
                    c = a[0],
                    d = "currentState",
                    e = "instance",
                    f = "id",
                    g = "_jump";
                  switch (this["currentState"]) {
                    case "none":
                      break;
                    case "skip":
                      nx["instance"]()["wa"](this["id"] + "_jump"), nx["instance"]()["La"](this["id"] + "_jump", this, this["D_"]);
                      break;
                    default:
                      this["yw"](this["currentState"])
                  }
                  this["event"]("onStateChange", this["currentState"])
                },
                ["enumerable"]: false,
                ["configurable"]: true,
                ["writable"]: true
              });
              continue
            } else if (c[7] == f) {
              Object["defineProperty"](b["prototype"], "zw", {
                ["value"](a, b) {
                  var c = hr,
                    d = c[0];
                  0 == a ? this["addAttPower"] += b : 2 == a ? this["H_"] += b : 1 == a && (this["W_"] += b)
                },
                ["enumerable"]: false,
                ["configurable"]: true,
                ["writable"]: true
              });
              continue
            } else if (c[9] == f) {
              Object["defineProperty"](b["prototype"], "jw", {
                ["value"](a) {
                  var b = hr,
                    c = b[0];
                  return 0 == a ? this["m_"] : 2 == a ? this["w_"] / this["sw"]["map"]["ye"] : 1 == a ? 1 : void 0
                },
                ["enumerable"]: false,
                ["configurable"]: true,
                ["writable"]: true
              });
              continue
            } else if (c[3] == f) {
              Object["defineProperty"](b["prototype"], "changeState", {
                ["value"](a) {
                  var b = hr,
                    c = b[0];
                  this["E_"](), this["currentState"] = a, this["B_"]()
                },
                ["enumerable"]: false,
                ["configurable"]: true,
                ["writable"]: true
              });
              continue
            } else if (c[14] == f) {
              Object["defineProperty"](b["prototype"], "b_", {
                ["value"]() {
                  var a = hr,
                    b = a[0],
                    c = "Oc";
                  this["Oc"]["zIndex"] = t1["entityZIndexFromPixelY"](this["Oc"]["y"], this["sw"]["map"]["gridHei"], this["Jm"]())
                },
                ["enumerable"]: false,
                ["configurable"]: true,
                ["writable"]: true
              });
              continue
            } else if (c[12] == f) {
              Object["defineProperty"](b["prototype"], "U_", {
                ["value"](a, b, c, d, e = !1, f) {
                  var g = hr,
                    h = g[0],
                    i = g[3],
                    j = g[1],
                    k = g[5],
                    l = "Point",
                    m = "TEMP",
                    n = "Oc",
                    o = "instance",
                    p = "event",
                    q = "parent",
                    r = "globalToLocal",
                    s = "pos",
                    t = "C_",
                    u = "F_",
                    v = "O_",
                    w = "targetX",
                    x = "sw",
                    y = "map",
                    z = "ye",
                    A = "targetY",
                    B = "gridHei",
                    C = "b_";
                  this["Pw"](a, b, c), Laya["Point"]["TEMP"]["setTo"](0, 0), this["Oc"]["localToGlobal"](Laya["Point"]["TEMP"]), 1 == a ? (oc["instance"]["event"](sS["bt"], this["Oc"]), this["Oc"]["zIndex"] = t1["entityZIndexFromGridRow"](c, this["Jm"]()), this["Oc"]["parent"]["globalToLocal"](Laya["Point"]["TEMP"]), this["Oc"]["pos"](Laya["Point"]["TEMP"]["x"], Laya["Point"]["TEMP"]["y"]), this["C_"] = d, this["F_"] = b, this["O_"] = c, this["targetX"] = b * this["sw"]["map"]["ye"], this["targetY"] = c * this["sw"]["map"]["gridHei"]) : 3 == a ? this["nm"] ? (oc["instance"]["event"](sS["Mt"], this["Oc"], b), this["Oc"]["parent"]["globalToLocal"](Laya["Point"]["TEMP"]), this["b_"](), this["C_"] = d, this["F_"] = b, this["O_"] = c, this["targetX"] = this["Oc"]["x"], this["targetY"] = this["Oc"]["y"], this["Oc"]["pos"](Laya["Point"]["TEMP"]["x"], Laya["Point"]["TEMP"]["y"])) : (b = 4, c = -5, this["b_"](), this["C_"] = d, this["targetX"] = this["Oc"]["x"] * this["sw"]["map"]["ye"], this["targetY"] = this["Oc"]["y"] * this["sw"]["map"]["gridHei"]) : f && (f["parent"]["addChild"](this["Oc"]), this["Oc"]["parent"]["globalToLocal"](Laya["Point"]["TEMP"]), this["Oc"]["pos"](Laya["Point"]["TEMP"]["x"], Laya["Point"]["TEMP"]["y"]), this["C_"] = d, this["targetX"] = f["x"], this["targetY"] = f["y"]), this["changeState"]("skip")
                },
                ["enumerable"]: false,
                ["configurable"]: true,
                ["writable"]: true
              });
              continue
            } else if (c[11] == f) {
              Object["defineProperty"](b["prototype"], "h_", {
                ["value"]() {
                  var a = hr,
                    b = a[0];
                  this["b_"](), oc["instance"]["event"](sS["Rt"])
                },
                ["enumerable"]: false,
                ["configurable"]: true,
                ["writable"]: true
              });
              continue
            } else if (c[13] == f) {
              Object["defineProperty"](b["prototype"], "setState", {
                ["value"](a, b, c) {
                  var d = hr,
                    e = d[0],
                    f = "Nv",
                    g = "level",
                    h = "X_";
                  1 == a ? this["__"] = b : 2 == a ? this["k_"] = b : 3 == a && (this["S_"] = b, b ? (this["Nv"] = this["level"], this["X_"](1 - this["level"])) : this["X_"](this["Nv"] - 1))
                },
                ["enumerable"]: false,
                ["configurable"]: true,
                ["writable"]: true
              });
              continue
            } else if (c[5] == f) {
              Object["defineProperty"](b["prototype"], "E_", {
                ["value"]() {
                  var a = hr;
                  this["pw"](this["currentState"])
                },
                ["enumerable"]: false,
                ["configurable"]: true,
                ["writable"]: true
              });
              continue
            } else if (c[4] == f) {
              Object["defineProperty"](b["prototype"], "Y_", {
                ["value"](a, b, c) {
                  var d = hr,
                    e = d[0],
                    f = d[3],
                    g = "instance",
                    h = "Oc",
                    i = "parent";
                  1 == this["l_"] && oc["instance"]["event"](sS["j"], this["id"]), this["U_"](a, b, c, () => {
                    this["Oc"]["parent"] && (this["onMoved"](), qs["instance"]()["Wf"](this["Oc"]["parent"], this["Oc"]["x"] + this["Oc"]["width"] / 2, this["Oc"]["y"] + this["Oc"]["height"] / 2, 1))
                  })
                },
                ["enumerable"]: false,
                ["configurable"]: true,
                ["writable"]: true
              });
              continue
            } else if (c[1] == f) {
              Object["defineProperty"](b["prototype"], "i_", {
                ["value"]() {
                  var a = hr,
                    b = a[0],
                    c = "instance",
                    d = "event",
                    e = "Tt";
                  this["Oc"]["zIndex"] = t1["Fr"], "none" == this["currentState"] ? oc["instance"]["event"](sS["Tt"], !0) : oc["instance"]["event"](sS["Tt"], !1)
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
      } ["bind"](this)["apply"](), td = function() {
        var a = hr;
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
        constructor() {
          var a = hr,
            b = a[0];
          super(), this["ek"] = !1, this["ak"] = !0
        }
      };
      continue
    } else if (6 == b) {
      o4 = function() {
        let a;
        a = class extends pc {
          constructor() {
            var a = hr,
              b = a[37];
            var c;
            c = arguments;
            super(...c), this["lM"] = ["resources/img/props/upLvlSpellBurn0.png", "resources/img/props/upLvlSpellBurn1.png", "resources/img/props/upLvlSpellBurn2.png", "resources/img/props/upLvlSpellBurn3.png", "resources/img/props/upLvlSpellBurn4.png", "resources/img/props/upLvlSpellBurn5.png"]
          }
          reset() {
            var a = hr;
            super["reset"](), this["Ab"]["skin"] = "resources/img/props/upLvlSpell_1.png"
          }
        };
        ! function() {
          "use strict";
          var b = hr;
          Object["defineProperty"](a["prototype"], "pM", {
            ["value"]() {
              return 1
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          })
        } ["bind"](a)();
        return a
      } ["bind"](this)["apply"]();
      continue
    } else if (7 == b) {
      ti = function() {
        let a;
        a = class extends rD {
          constructor() {
