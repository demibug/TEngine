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
