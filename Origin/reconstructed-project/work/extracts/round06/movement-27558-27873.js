              qs["instance"]()["Hf"](a, a["width"] / 2, a["height"] / 2, 2), sF["instance"]()["Rn"](hu[81])
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          })
        } ["bind"](b)();
        return b
      } ["bind"](this)["apply"]();
      continue
    } else if (iB == b) {
      th["instance"]()["register"](3, () => Laya["Pool"]["createByClass"](pc));
      continue
    } else if (iC == b) {
      th["instance"]()["register"](6, () => Laya["Pool"]["createByClass"](p4));
      continue
    } else if (hW == b) {
      th["instance"]()["register"](hF, () => Laya["Pool"]["createByClass"](rx));
      continue
    } else if (iD == b) {
      rd["IS"] = "SimpleDynamicArrow", vk && vk["GS"] ? vk["GS"]("SimpleDynamicArrow", rd) : console["warn"]("BulletFactory not available for SimpleDynamicArrow registration");
      continue
    } else if (iE == b) {
      th["instance"]()["register"](hy, () => Laya["Pool"]["createByClass"](sb));
      continue
    }
    if (iF == b) {
      th["instance"]()["register"](hx, () => Laya["Pool"]["createByClass"](qw));
      continue
    } else if (iG == b) {
      oF = tc, o9 = Laya["Vector2"], pP = function() {
        var a = hr;
        let b;
        b = class {
          constructor() {
            var a = hr,
              b = a[0];
            this["wk"] = 1, this["Lk"] = -1, this["vk"] = new o9, this["_k"] = new Laya["Point"]
          }
          static create(...a) {
            var b = hr,
              c = b[0];
            let d, e, f;
            e = this, f = e["xk"], d = Laya["Pool"]["getItemByCreateFun"](f, () => new e);
            return d["bk"](...a), d
          }
        };
        ! function() {
          var a = hr,
            c = a[0],
            d = "defineProperty",
            oXx = "value",
            oXy = "enumerable",
            oXz = "configurable",
            oXA = "writable";
          for (let e of mD) {
            if (-1 == e) {} else if (0 == e) {
              "use strict";
              continue
            }
            if (1 == e) {
              Object["defineProperty"](b["prototype"], "Ek", {
                ["value"](a) {},
                ["enumerable"]: false,
                ["configurable"]: true,
                ["writable"]: true
              });
              continue
            } else if (2 == e) {
              Object["defineProperty"](b["prototype"], "Mk", {
                ["value"]() {
                  var a = hr,
                    b = a[0];
                  let c;
                  this["Pk"]();
                  c = this["constructor"]["xk"];
                  Laya["Pool"]["recover"](c, this)
                },
                ["enumerable"]: false,
                ["configurable"]: true,
                ["writable"]: true
              });
              continue
            } else if (3 == e) {
              Object["defineProperty"](b["prototype"], "Ak", {
                ["value"](a) {
                  var b = hr,
                    c = b[0];
                  return this["Lk"] = a, this["Ek"](a), this
                },
                ["enumerable"]: false,
                ["configurable"]: true,
                ["writable"]: true
              });
              continue
            } else if (4 == e) {
              Object["defineProperty"](b["prototype"], "Sk", {
                ["value"]() {},
                ["enumerable"]: false,
                ["configurable"]: true,
                ["writable"]: true
              });
              continue
            } else if (5 == e) {
              Object["defineProperty"](b["prototype"], "kk", {
                ["value"]() {},
                ["enumerable"]: false,
                ["configurable"]: true,
                ["writable"]: true
              });
              continue
            } else if (6 == e) {
              Object["defineProperty"](b["prototype"], "Ik", {
                ["value"](a) {
                  var b = hr,
                    c = "_k";
                  return this["_k"]["setTo"](a["x"], a["y"]), this["Ck"](this["_k"]), this
                },
                ["enumerable"]: false,
                ["configurable"]: true,
                ["writable"]: true
              });
              continue
            } else if (7 == e) {
              Object["defineProperty"](b["prototype"], "Ck", {
                ["value"](a) {},
                ["enumerable"]: false,
                ["configurable"]: true,
                ["writable"]: true
              });
              continue
            }
            if (8 == e) {
              Object["defineProperty"](b["prototype"], "Dk", {
                ["value"](a) {},
                ["enumerable"]: false,
                ["configurable"]: true,
                ["writable"]: true
              });
              continue
            } else if (9 == e) {
              Object["defineProperty"](b["prototype"], "Bk", {
                ["value"](a) {
                  var b = hr,
                    c = b[0],
                    d = "vk";
                  return a instanceof o9 ? a["cloneTo"](this["vk"]) : "number" == typeof a && np["Hs"](a, this["vk"]), this["Dk"](this["vk"]), this
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
      } ["bind"](this)["apply"]();
      continue
    } else if (iH == b) {
      on = function() {
        let a;
        a = class extends pP {
          constructor() {
            var a = hr,
              b = a[0],
              c = "Point";
            var d;
            d = arguments;
            super(...d), this["HS"] = 0, this["WS"] = new Laya["Point"], this["_lastPosition"] = new Laya["Point"], this["jS"] = new Laya["Point"], this["zS"] = new Laya["Point"]
          }
          static create(a = hu[45], b = !0, c = !1, d = !0) {
            return super["create"](a, b, c, d)
          }
        };
        ! function() {
          "use strict";
          var b = hr,
            c = b[0],
            d = "defineProperty",
            oXR = "value",
            oXS = "enumerable",
            oXT = "configurable",
            oXU = "writable";
          Object["defineProperty"](a["prototype"], "kk", {
            ["value"]() {
              var a = hr,
                b = a[0],
                c = "setTo",
                d = "dS",
                e = "WS",
                f = "jS",
                g = "zS";
              this["HS"] = 0, this["NS"] || (this["_lastPosition"]["setTo"](this["dS"]["x"], this["dS"]["y"]), this["WS"]["setTo"](this["dS"]["x"], this["dS"]["y"]), this["qS"](), this["jS"]["setTo"](this["WS"]["x"] + (this["zS"]["x"] - this["WS"]["x"]) / 2, this["WS"]["y"] + (this["zS"]["y"] - this["WS"]["y"]) / 2 - this["$S"]), this["dS"]["Nk"] && (this["dS"]["rotation"] = np["Rs"](this["WS"], this["jS"], this["zS"], 0) + hu[88]))
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](a["prototype"], "Tk", {
            ["value"](a, b) {
              var c = hr,
                d = c[0],
                e = c[2],
                f = c[3],
                g = "bs",
                h = "WS",
                i = "zS",
                j = "dS",
                k = "HS",
                l = "_lastPosition",
                m = "rotation";
              let n, o;
              o = a * this["wk"] * b / hu[176];
              if (this["NS"] || this["qS"](), this["QS"]) {
                let a, b;
                a = np["bs"](this["WS"], this["zS"]), b = np["bs"](this["dS"], this["zS"]);
                if (a > 0) {
                  let d;
                  d = Math["max"](.1, b / a);
                  o *= Math["sqrt"](d)
                }
              }
              this["HS"] += o;
              n = this["dS"]["eS"];
              if (!(np["Ms"](this["zS"], n) < this["ZS"]) && this["HS"] < 1) {
                if (np["Us"](this["WS"], this["jS"], this["zS"], n, this["HS"]), this["dS"]["Nk"]) {
                  let b;
                  b = np["angle"](this["_lastPosition"], n);
                  if (this["KS"]) {
                    let d, f;
                    d = n["rotation"] - b, f = d > 10;
                    n["rotation"] = Laya["MathUtil"]["lerp"](n["rotation"], b, f ? a / (1.5 * d) : 1)
                  } else n["rotation"] = b
                }
                this["_lastPosition"]["setTo"](n["x"], n["y"])
              } else this["dS"]["aS"]();
              this["dS"]["ak"] = this["HS"] >= .8
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](a["prototype"], "qS", {
            ["value"]() {
              var a = hr,
                b = a[0],
                c = "zS",
                d = "enemy",
                e = "instance",
                f = "map";
              let g;
              g = this["sx"]["JS"]["get"](this["Lk"]);
              g ? (this["zS"]["setTo"](g["enemy"]["x"], g["enemy"]["y"]), this["zS"]["x"] += uq["instance"]()["map"]["ye"] / 2, this["zS"]["y"] += uq["instance"]()["map"]["gridHei"] / 2) : this["NS"] = !0
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](a["prototype"], "A_", {
            ["value"]() {
              var a = hr,
                b = a[0],
                c = "ZS",
                d = "dS";
              this["ZS"] = this["ix"] ? this["dS"]["eS"]["height"] / 1.5 : 0, this["ZS"] *= this["ZS"], this["NS"] && (this["dS"]["aS"](!0), this["dS"]["oS"]())
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](a["prototype"], "bk", {
            ["value"](a, b, c, d) {
              var e = hr,
                f = e[0];
              this["$S"] = a, this["QS"] = b, this["KS"] = c, this["ix"] = d, this["NS"] = !0, this["sx"] = vi["instance"]()
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](a["prototype"], "Ek", {
            ["value"](a) {
              var b = hr,
                c = b[0],
                d = "NS";
              this["sx"]["JS"]["get"](a) ? (this["NS"] = !1, this["qS"]()) : this["NS"] = !0
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](a["prototype"], "Pk", {
            ["value"]() {},
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](a["prototype"], "hx", {
            ["value"](a) {
              var b = hr,
                c = b[0],
                d = "jS",
                e = "zS";
              return this["NS"] ? null : (a || (a = this["WS"]), this["qS"](), this["jS"]["setTo"](a["x"] + (this["zS"]["x"] - a["x"]) / 2, a["y"] + (this["zS"]["y"] - a["y"]) / 2 - this["$S"]), np["Rs"](a, this["jS"], this["zS"], 0) + hu[88])
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          })
        } ["bind"](a)();
        return a
      } ["bind"](this)["apply"]();
      continue
    }
    if (hQ == b) {
