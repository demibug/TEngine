      rd = function() {
        var a = hr;
        let b;
        b = class extends qY {
          constructor() {
            var a;
            a = arguments;
            super(...a), this["CS"] = !1
          }
        };
        ! function() {
          var a = hr,
            c = a[0],
            d = "defineProperty",
            oS6 = "value",
            oS7 = "enumerable",
            oS8 = "configurable",
            oS9 = "writable";
          let e = 0,
            f = u6;
          w1_cV: while (e < 10) {
            ++e;
            switch (f) {
              case 9:
                Object["defineProperty"](b["prototype"], "AS", {
                  ["value"]() {},
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                f = 6;
                break;
              case 8:
                "use strict";
                f = 4;
                break;
              case 7:
                Object["defineProperty"](b["prototype"], "xS", {
                  ["value"]() {},
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                f = 9;
                break;
              case 1:
                Object["defineProperty"](b["prototype"], "Cw", {
                  ["value"](a) {
                    var b = hr,
                      c = b[0],
                      d = "enemy",
                      e = "instance";
                    let f, g;
                    a["hit"](this["sS"], this["iS"]);
                    g = a["enemy"]["width"] / 2, f = a["enemy"]["height"] / 2;
                    this["CS"] ? qs["instance"]()["Cg"](a["enemy"], g, f) : qs["instance"]()["Ig"](a["enemy"], g, f)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                f = 7;
                break;
              case 2:
                Object["defineProperty"](b["prototype"], "onUpdate", {
                  ["value"](a) {},
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                f = 8;
                break;
              case 3:
                Object["defineProperty"](b["prototype"], "wS", {
                  ["value"]() {},
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                f = 1;
                break;
              case 0:
                Object["defineProperty"](b["prototype"], "A_", {
                  ["value"]() {
                    var a = hr,
                      b = hu,
                      c = a[5],
                      d = "eS";
                    this["eS"]["pos"](0, 0), this["eS"]["size"](b[2], b[34]), this["eS"]["anchorX"] = .5, this["eS"]["anchorY"] = .9
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                f = 5;
                break;
              case 6:
                Object["defineProperty"](b["prototype"], "onReset", {
                  ["value"](a) {
                    var b = hr,
                      c = "CS";
                    var d;
                    this["CS"] = !!(null == (d = a["XS"]) ? void 0 : d["CS"])
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                f = 2;
                break;
              case 4:
                Object["defineProperty"](b["prototype"], "DS", {
                  ["value"]() {},
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                f = 0;
                break;
              case 5:
                Object["defineProperty"](b["prototype"], "TS", {
                  ["value"](a) {
                    var b = hr,
                      c = hu,
                      d = b[3],
                      e = b[0],
                      f = "size",
                      g = "eS",
                      h = "US",
                      i = "OS",
                      j = "YS";
                    let k;
                    k = new Laya["Image"](a["RS"]);
                    k["size"](c[2], c[34]), this["eS"]["addChild"](k), a["US"] && (this["eS"]["size"](a["US"]["x"], a["US"]["y"]), k["size"](a["US"]["x"], a["US"]["y"])), a["OS"] && this["eS"]["scale"](a["OS"]["x"], a["OS"]["y"]), a["YS"] && this["eS"]["anchor"](a["YS"]["x"], a["YS"]["y"])
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                f = 3;
                break;
              default:
                break
            }
          }
        } ["bind"](b)();
        return b
      } ["bind"](this)["apply"]();
