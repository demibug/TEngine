      } ["bind"](this)["apply"](), r6 = function() {
        var a = hr;
        let b;
        b = class extends qY {
          constructor() {
            var a = hr,
              b = a[0];
            var c;
            c = arguments;
            super(...c), this["Kk"] = pI["rk"], this["Lx"] = new Laya["Point"]
          }
        };
        ! function() {
          var a = hr,
            c = a[0],
            d = "defineProperty",
            oQZ = "value",
            oQ0 = "enumerable",
            oQ1 = "configurable",
            oQ2 = "writable";
          w1_cX: for (let e of mC) {
            switch (e) {
              case 0:
                Object["defineProperty"](b["prototype"], "A_", {
                  ["value"]() {
                    var a = hr,
                      b = hu,
                      c = b[1],
                      d = "eS";
                    this["eS"]["size"](c, c), this["eS"]["anchor"](.5, .5)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                break;
              case 1:
                Object["defineProperty"](b["prototype"], "Cw", {
                  ["value"](a) {
                    var b = hr,
                      c = b[0];
                    a["hit"](this["sS"], this["iS"])
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                break;
              case 2:
                Object["defineProperty"](b["prototype"], "onUpdate", {
                  ["value"](a) {},
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                break;
              case 3:
                Object["defineProperty"](b["prototype"], "AS", {
                  ["value"]() {},
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                break;
              case 4:
                Object["defineProperty"](b["prototype"], "Px", {
                  ["value"](a) {
                    var b = hr,
                      c = hu,
                      d = b[0],
                      e = b[3],
                      f = c[95],
                      g = c[97],
                      h = "Lx";
                    let i, j, k, l;
                    k = uq["instance"]()["Cy"](a["enemy"], !0, !1), l = this["Lx"]["x"] - k["x"], j = this["Lx"]["y"] - k["y"];
                    i = -1 * Math["atan2"](-l, j);
                    return i = f * i / Math["PI"], i > f && (i -= g), i < -f && (i += g), i
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                break;
              case 5:
                Object["defineProperty"](b["prototype"], "DS", {
                  ["value"]() {},
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                break;
              case 6:
                "use strict";
                break;
              case 7:
                Object["defineProperty"](b["prototype"], "xS", {
                  ["value"]() {
                    var a = hr,
                      b = a[0];
                    this["ck"] && this["aS"]()
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                break;
              case 8:
                Object["defineProperty"](b["prototype"], "wS", {
                  ["value"]() {
                    var a = hr,
                      b = a[0],
                      c = a[3],
                      d = a[4],
                      e = a[6],
                      f = "vx",
                      g = "to",
                      h = "xx";
                    this["vx"] && Laya["Tween"]["create"](this["eS"])["duration"](this["vx"])["to"]("rotation", this["Sx"])["to"]("height", this["kx"] + 2 * this["xx"])["to"]("width", 2 * this["xx"])["then"](() => {
                      this["bx"] && this["aS"]()
                    })
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                break;
              case 9:
                Object["defineProperty"](b["prototype"], "onReset", {
                  ["value"](a) {
                    var b = hr,
                      c = b[0],
                      d = b[2],
                      e = "iS",
                      f = "root",
                      g = "mx",
                      h = "eS",
                      i = "bold",
                      j = "length",
                      k = "rotation",
                      l = "pos",
                      m = "vx",
                      n = "kx",
                      o = "Sx",
                      p = "xx",
                      q = "ck";
                    let r;
                    var s, t, u, v, w, x;
                    if (!this["iS"] || !this["iS"]["root"]) return !1;
                    if (this["Lx"]["copy"](this["sw"]["Cy"](this["iS"]["root"], !0)), !a["mx"]) return;
                    r = a["mx"];
                    this["eS"]["size"](2 * r["bold"], r["length"] + 2 * r["bold"]), this["eS"]["anchor"](.5, 1), this["eS"]["rotation"] = r["rotation"], this["eS"]["pos"](r["pos"]["x"], r["pos"]["y"]), this["vx"] = null != (s = r["vx"]) ? s : 0, this["kx"] = null != (t = r["kx"]) ? t : r["length"], this["Sx"] = null != (u = r["Sx"]) ? u : r["rotation"], this["xx"] = null != (v = r["xx"]) ? v : r["bold"], this["ck"] = null != (w = r["ck"]) && w, this["bx"] = null != (x = r["Mx"]) && x
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
