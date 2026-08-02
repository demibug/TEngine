            ["configurable"]: true,
            ["writable"]: true
          })
        } ["bind"](b)();
        return b
      } ["bind"](this)["apply"]();
      continue
    }
    if (hD == b) {
      on["xk"] = "TargetEnemyBezierMovement";
      continue
    } else if (hE == b) {
      oE = rW, qd = class extends sd {
        constructor() {
          var a = hr,
            b = a[0];
          super(), this["ek"] = !0, this["ak"] = !1
        }
      };
      continue
    }
    if (hF == b) {
      nN = function() {
        let a;
        a = class extends pP {};
        ! function() {
          "use strict";
          var b = hr,
            c = b[0],
            d = "defineProperty",
            oHg = "value",
            oHh = "enumerable",
            oHi = "configurable",
            oHj = "writable";
          Object["defineProperty"](a["prototype"], "A_", {
            ["value"](...a) {},
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](a["prototype"], "Tk", {
            ["value"](a, b) {},
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](a["prototype"], "bk", {
            ["value"](...a) {},
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](a["prototype"], "Pk", {
            ["value"]() {},
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          })
        } ["bind"](a)();
        return a
      } ["apply"]();
      continue
    } else if (hU == b) {
      tb["zx"] = function() {
        var a = hr,
          b = hu,
          c = "bind",
          d = "apply";
        let e;
        e = {
          [b[115]]: 0,
          [b[116]]: 0,
          [b[248]]: 0,
          [b[249]]: 0,
          [b[250]]: 0
        };
        e[0] = function() {
          var a = hr;
          let b;
          b = class extends td {
            constructor() {
              var a = hr;
              var b;
              b = arguments;
              super(...b), this["Q_"] = "knife"
            }
            init(a, b) {
              var c = hr;
              this["Q_"] = "knife", super["init"](a, b)
            }
            V_() {
              var a = hr,
                b = a[0];
              super["V_"](), this["T_"]["scale"](1, 1)
            }
          };
          ! function() {
            "use strict";
            var a = hr,
              c = "defineProperty",
              oHq = "value",
              oHr = "enumerable",
              oHs = "configurable",
              oHt = "writable";
            Object["defineProperty"](b["prototype"], "attack", {
              ["value"]() {
                this["Nx"]()
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            });
            Object["defineProperty"](b["prototype"], "Nx", {
              ["value"]() {
                var a = hr,
                  b = hu,
                  c = a[0],
                  d = a[4],
                  e = a[5],
                  f = a[2],
                  g = a[7],
                  h = a[3],
                  i = "lx",
                  j = "instance",
                  k = "Oc",
                  l = "width",
                  m = "height",
                  n = "length",
                  o = "bs",
                  p = "map",
                  q = "T_",
                  r = "Event";
                let s, t, u;
                if (this["lx"] = vi["instance"]()["qx"](this["Oc"]["x"] + this["Oc"]["width"] / 2, this["Oc"]["y"] + this["Oc"]["height"] / 2, this["wp"], this["nm"]), !this["lx"] || this["lx"]["length"] <= 0) return void this["changeState"]("UnitIdle");
                u = this["lx"][0], t = np["bs"]({
                  ["x"]: this["Oc"]["x"],
                  ["y"]: this["Oc"]["y"]
                }, {
                  ["x"]: u["x"],
                  ["y"]: u["y"]
                });
                for (let a = 1; a < this["lx"]["length"]; a++) {
                  let b;
                  b = np["bs"]({
                    ["x"]: this["Oc"]["x"] + this["Oc"]["width"] / 2,
                    ["y"]: this["Oc"]["y"] + this["Oc"]["height"] / 2
                  }, {
                    ["x"]: this["lx"][a]["x"] + uq["instance"]()["map"]["ye"] / 2,
                    ["y"]: this["lx"][a]["y"] + uq["instance"]()["map"]["gridHei"] / 2
                  });
                  b < t && (u = this["lx"][a], t = b)
                }
                s = {
                  ["type"]: tO,
                  ["iS"]: this,
                  ["sS"]: this["_p"],
                  ["yS"]: oF["produce"](b[81], {
                    ["Lk"]: u["id"],
                    ["ck"]: !0,
                    ["uk"]: "hitEnable",
                    ["pk"]: b[176] / this["j_"]
                  }),
                  ["tS"]: "knifeSoliderAttack"
                };
                vA["instance"]()["gx"](s)["LS"](), pC["instance"]()["playSound"]("knife_attack"), this["T_"]["on"](Laya["Event"]["STOPPED"], this, () => {
                  this["T_"]["offAll"](Laya["Event"]["STOPPED"])
                }), this["T_"]["play"]("attack", !1)
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            })
          } ["bind"](b)();
          return b
        } ["bind"](this)["apply"]();
        e[1] = ok;
        e[2] = function() {
          var a = hr;
          let b;
          b = class extends td {
            constructor() {
              var a = hr,
                b = a[0];
              var c;
              c = arguments;
              super(...c), this["Q_"] = "pike", this["$x"] = {
                ["x"]: 0,
                ["y"]: 0
              }
            }
            V_() {
              var a = hr,
                b = hu,
                c = a[0],
                d = a[3],
                e = a[5],
                f = b[22],
                g = "Vx",
                h = "Image",
                i = "size",
                j = "Qx",
                k = "pos",
                l = "addChild",
                m = "visible",
                n = "$x";
              super["V_"](), this["Vx"] || (this["Vx"] = new Laya["Image"]("resources/img/gameObject/soldier/pike.png"), this["Vx"]["size"](f, b[16]), this["Vx"]["anchorX"] = .5, this["Vx"]["anchorY"] = .5, this["Qx"] = new Laya["Image"]("resources/img/gameObject/soldier/pikeEff1.png"), this["Qx"]["size"](f, b[76]), this["Qx"]["pos"](1, -b[30]), this["Vx"]["addChild"](this["Qx"]), this["Zx"] = new Laya["Point"](this["Vx"]["width"] / 2, 0)), this["Vx"]["pos"](b[8], b[32]), this["Vx"]["visible"] = !0, this["Oc"]["addChild"](this["Vx"]), this["$x"]["x"] = this["Vx"]["x"], this["$x"]["y"] = this["Vx"]["y"], this["Qx"]["visible"] = !1
            }
            Z_() {
              var a = hr,
                b = hu,
                c = b[97],
                d = b[95],
                e = "Vx",
                f = "rotation";
              super["Z_"](), this["Vx"]["rotation"] = this["Vx"]["rotation"] % c, this["Vx"]["rotation"] < -d ? this["Vx"]["rotation"] += c : this["Vx"]["rotation"] >= d && (this["Vx"]["rotation"] -= c)
            }
            idle(a) {
              var b = hr,
                c = hu,
                d = c[1],
                e = "Vx",
                f = "rotation";
              if (super["idle"](a), 0 != this["Vx"]["rotation"]) {
                let c;
                c = a;
                this["Vx"]["rotation"] > 0 ? this["Vx"]["rotation"] -= d * c : this["Vx"]["rotation"] += d * c, Math["abs"](this["Vx"]["rotation"]) < 10 * c && (this["Vx"]["rotation"] = 0)
              }
            }
            J_() {
              var a = hr,
                b = a[0],
                c = a[5],
                d = "Vx",
                e = "$x";
              super["J_"](), Laya["Tween"]["killAll"](this["Vx"]), this["Vx"]["x"] = this["$x"]["x"], this["Vx"]["y"] = this["$x"]["y"], this["Qx"]["visible"] = !1
            }
            gameOver() {
              var a = hr,
                b = a[6],
                c = a[5],
                d = a[0],
                e = "Vx";
              super["gameOver"](), Laya["Tween"]["killAll"](this["Vx"]), nx["instance"]()["wa"]("Pike" + this["id"]), this["Vx"] && (this["Vx"]["visible"] = !1, this["Vx"]["rotation"] = 0, this["Vx"]["removeSelf"]())
            }
          };
          ! function() {
            "use strict";
            var a = hr,
              c = "defineProperty",
              oIl = "value",
              oIm = "enumerable",
              oIn = "configurable",
              oIo = "writable";
            Object["defineProperty"](b["prototype"], "attack", {
              ["value"]() {
                var a = hr,
                  b = "Vx",
                  c = "rotation";
                this["Vx"]["rotation"] < 0 && (this["Vx"]["rotation"] += hu[97]), this["Nx"]()
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            });
            Object["defineProperty"](b["prototype"], "Nx", {
              ["value"]() {
                var a = hr,
                  b = hu,
                  c = a[0],
                  d = a[4],
                  e = a[2],
                  f = a[6],
                  g = a[1],
                  h = a[5],
                  i = a[3],
                  j = b[95],
                  k = b[97],
                  l = b[88],
                  m = b[81],
                  n = "lx",
                  oIB = "length",
                  o = "bs",
                  p = "Oc",
                  q = "Vx",
                  r = "instance",
                  s = "map",
                  t = "ye",
                  u = "gridHei",
                  oIJ = "rotation",
                  v = "abs",
                  w = "q_",
                  x = "T_",
                  y = "Event",
                  z = "create",
                  A = "Zx",
                  B = "Tween",
                  C = "duration",
                  D = "j_",
                  E = "to",
                  F = "then",
                  G = "chain",
                  H = "$x",
                  I = "sin",
                  J = "PI",
                  K = "cos",
                  L = "Qx",
                  M = "visible";
                let N, O, P, Q, R, S, T, U;
                if (!this["lx"] || this["lx"]["length"] <= 0) return;
                R = this["lx"][0], O = np["bs"]({
                  ["x"]: this["Oc"]["x"],
                  ["y"]: this["Oc"]["y"]
                }, {
                  ["x"]: R["x"],
                  ["y"]: R["y"]
                }), N = this["Oc"]["x"] + this["Vx"]["x"], T = this["Oc"]["y"] + this["Vx"]["y"];
                for (let a = 1; a < this["lx"]["length"]; a++) {
                  let b;
                  b = np["bs"]({
                    ["x"]: N,
                    ["y"]: T
                  }, {
                    ["x"]: this["lx"][a]["x"] + uq["instance"]()["map"]["ye"] / 2,
                    ["y"]: this["lx"][a]["y"] + uq["instance"]()["map"]["gridHei"] / 2
                  });
                  b < O && (R = this["lx"][a], O = b)
                }
                Q = np["angle"]({
                  ["x"]: N,
                  ["y"]: T
                }, {
                  ["x"]: R["x"] + uq["instance"]()["map"]["ye"] / 2,
                  ["y"]: R["y"] + uq["instance"]()["map"]["gridHei"] / 2
                }), S = Q, P = 1;
                S > this["Vx"]["rotation"] ? Math["abs"](S - this["Vx"]["rotation"]) > j && (P = -1, S = -(k - Q)) : Math["abs"](S - this["Vx"]["rotation"]) > j ? S = k + Q : P = -1, this["q_"] = !1, this["T_"]["on"](Laya["Event"]["STOPPED"], this, () => {
                  this["q_"] = !0, this["T_"]["offAll"](Laya["Event"]["STOPPED"])
                }), this["T_"]["play"]("attack", !1);
                U = vA["instance"]()["gx"]({
                  ["type"]: sv,
                  ["gS"]: pW["create"](this["Vx"], this["Zx"]["x"], this["Zx"]["y"], !1, !1),
                  ["iS"]: this,
                  ["sS"]: this["_p"],
                  ["mx"]: {
                    ["pos"]: {
                      ["x"]: this["Zx"]["x"],
                      ["y"]: this["Zx"]["y"]
                    },
                    ["bold"]: b[12],
                    ["length"]: b[16],
                    ["rotation"]: Q,
                    ["ck"]: !1
                  }
                });
                Laya["Tween"]["create"](this["Vx"])["duration"](l / this["j_"])["to"]("rotation", S)["then"](() => {
                  this["Vx"]["rotation"] = Q
                }, this)["chain"]()["duration"](b[96] / this["j_"])["to"]("x", this["$x"]["x"] + -10 * Math["sin"](Q * (Math["PI"] / j)))["to"]("y", this["$x"]["y"] - -10 * Math["cos"](Q * (Math["PI"] / j)))["then"](() => {
                  this["Qx"]["y"] = -b[30], this["Qx"]["visible"] = !0, Laya["Tween"]["create"](this["Qx"])["to"]("y", -b[45])["duration"](m), pC["instance"]()["playSound"]("general_pike_attack")
                })["chain"]()["duration"](b[111] / this["j_"])["to"]("x", this["$x"]["x"] + m * Math["sin"](Q * (Math["PI"] / j)))["to"]("y", this["$x"]["y"] - m * Math["cos"](Q * (Math["PI"] / j)))["onStart"](() => {
                  U["LS"]()
                }, this)["then"](() => {
                  this["Qx"]["visible"] = !1, U["aS"]()
                }, this)["chain"]()["duration"](l / this["j_"])["to"]("x", this["$x"]["x"])["to"]("y", this["$x"]["y"])
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            })
          } ["bind"](b)();
          return b
        } ["bind"](this)["apply"]();
        e[3] = function() {
          let a;
          a = class extends td {
            constructor() {
              var a = hr;
              var b;
              b = arguments;
              super(...b), this["Q_"] = "cavalry"
            }
            init(a, b) {
              var c = hr;
              this["Q_"] = "cavalry", super["init"](a, b)
            }
            V_() {
              super["V_"]()
            }
          };
          ! function() {
            "use strict";
            var b = hr,
              c = "defineProperty",
              oI7 = "value",
              oI8 = "enumerable",
              oI9 = "configurable",
              oJa = "writable";
            Object["defineProperty"](a["prototype"], "attack", {
              ["value"]() {
                this["Nx"]()
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            });
            Object["defineProperty"](a["prototype"], "Nx", {
              ["value"]() {
                var a = hr,
                  b = hu,
                  c = a[0],
                  d = a[2],
                  e = a[4],
                  f = a[3],
                  g = b[167],
                  oJg = "mx",
                  h = "Oc",
                  oJi = "length",
                  i = "wp",
                  j = "instance",
                  k = "gx",
                  l = "LS";
                let m, n, o;
                this["T_"]["play"]("attack", !1);
                n = {
                  ["type"]: sv,
                  ["iS"]: this,
                  ["sS"]: this["_p"] / 2,
                  ["tS"]: "cavalrySweep",
                  ["mx"]: {
                    ["pos"]: new Laya["Point"](this["Oc"]["x"] + this["Oc"]["width"] / 2, this["Oc"]["y"] + this["Oc"]["height"] / 2),
                    ["bold"]: 5,
                    ["length"]: this["wp"] / 2,
                    ["rotation"]: b[61],
                    ["vx"]: g,
                    ["Sx"]: -g,
                    ["Mx"]: !0,
                    ["Ax"]: !0
                  }
                };
                pC["instance"]()["playSound"]("cavalry_attack");
                m = vA["instance"]()["gx"](n);
                n["mx"]["length"] = this["wp"];
                o = vA["instance"]()["gx"](n);
                Laya["timer"]["once"](b[112], this, () => {
                  m["LS"](), o["LS"]()
                })
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            })
          } ["bind"](a)();
          return a
        } ["bind"](this)["apply"]();
        e[4] = qo;
        return e
      } ["bind"](this)["apply"]();
      continue
    } else if (hV == b) {
      th["instance"]()["register"](9, () => Laya["Pool"]["createByClass"](pb));
      continue
    } else if (hY == b) {
      tO = class extends r6 {
        constructor() {
          var a;
          a = arguments;
          super(...a), this["Ex"] = !1
        }
        onReset(a) {
          super["onReset"](a)
        }
        Cw(a) {
          var b = hr,
            c = b[0],
            d = "instance",
            e = "width",
            f = "height";
          let g, h;
          super["Cw"](a);
          h = this["Px"](a), g = a["enemy"];
          this["Ex"] ? qs["instance"]()["Tg"](g, g["width"] / 2, g["height"] / 2, -h) : qs["instance"]()["Dg"](g, g["width"] / 2, g["height"] / 2, -h)
        }
      };
      continue
    } else if (hZ == b) {
      rb = function() {
        let a;
        a = class b extends qE {
          constructor() {
            var a = hr;
            var b;
            b = arguments;
            super(...b), this["s_"] = new Laya["Point"]
          }
          onMouseMove() {
            var a = hr,
              c = a[0],
              d = a[4],
              e = "a_",
              f = "stage",
              g = "s_";
            if (this["e_"] && !this["a_"]) {
              let h, i;
              i = Laya["stage"]["mouseX"] - this["s_"]["x"], h = Laya["stage"]["mouseY"] - this["s_"]["y"];
              Math["sqrt"](i * i + h * h) > b["n_"] && (this["a_"] = !0, this["i_"]())
            }
          }
          onMouseUp(a, b) {
            var c = hr,
              d = c[0],
              e = "e_",
              f = "a_",
              g = "instance",
              h = "event";
            this["e_"] && (this["e_"] = !1, this["a_"] || (oc["instance"]["event"](sS["st"], this["id"]), oc["instance"]["event"](sS["us"], this)), this["a_"] = !1, this["h_"](), oc["instance"]["event"](sS["Rt"]))
          }
        };
        ! function() {
          "use strict";
          var b = hr,
            c = b[0],
            d = "defineProperty",
            oJI = "value",
            oJJ = "enumerable",
            oJK = "configurable",
            oJL = "writable";
          Object["defineProperty"](a["prototype"], "i_", {
            ["value"]() {},
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](a["prototype"], "h_", {
            ["value"]() {},
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](a["prototype"], "onMouseDown", {
            ["value"]() {
              var a = hr,
                b = a[0],
                c = a[4],
                d = "stage";
              this["e_"] = !0, this["a_"] = !1, this["s_"]["setTo"](Laya["stage"]["mouseX"], Laya["stage"]["mouseY"])
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          })
        } ["bind"](a)();
        return a
      } ["bind"](this)["apply"]();
      continue
    } else if (h0 == b) {
      qY["IS"] = "";
      continue
    } else if (hL == b) {
      pI = s9, tc = class a {
        static produce(a, b) {
          var c = hr,
            d = hu,
            e = c[0],
            f = "Lk",
            g = "lk",
            h = "pk",
            i = "ck",
            j = "uk",
            k = "rk";
          switch (a) {
            case d[81]:
              const l = Laya["Pool"]["getItemByCreateFun"](`HitEnemyStrategy${a}`, () => {
                const b = new oE;
                return b["gk"] = "HitEnemyStrategy" + a, b["dk"] = a, b
              });
              if (b) {
                let a;
                a = b;
                "Lk" in a && (Array["isArray"](a["Lk"]) ? l["lk"] = a["Lk"] : "number" == typeof a["Lk"] && (l["lk"] = [a["Lk"]])), "pk" in a && (l["pk"] = a["pk"]), "ck" in a && (l["ck"] = a["ck"]), l["uk"] = "uk" in a ? a["uk"] : "requestRemove"
              } else l["pk"] = 0, l["lk"] = [], l["ck"] = !0;
              return l["yk"] = !1, l["fk"] = !1, l;
            case d[94]:
            default:
              return tS["rk"];
            case d[90]:
              return ts["rk"];
            case d[89]:
              return pI["rk"]
          }
        }
        static copyFrom(b) {
          var c = hr;
          let d;
          d = a["produce"](b["dk"]);
          return Object["assign"](d, b), d
        }
        static recover(a) {
          var b = hr,
            c = b[0];
          let d;
          if (!a) return;
          if (void 0 === a["dk"]) return;
          a instanceof oE && (a["lk"] = [], a["pk"] = -1, a["ck"] = !0);
