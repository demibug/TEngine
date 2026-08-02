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
