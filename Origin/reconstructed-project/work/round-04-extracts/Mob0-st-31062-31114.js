      st = function() {
        let a;
        a = class extends pe {
          constructor() {
            var a = hr;
            var b;
            b = arguments;
            super(...b), this["JE"] = "resources/img/gameObject/enemy/mob_0.png"
          }
          init(a) {
            var b = hr,
              c = b[0],
              d = b[1],
              e = "enemy";
            this["lm"] = !1, this["enemy"] = rw["instance"]()["getItem"]("mob", this), super["init"](a), this["tw"]["pos"](this["enemy"]["width"] / 2, this["enemy"]["height"])
          }
          gameOver() {
            var a = hr,
              b = a[6];
            super["gameOver"](), rw["instance"]()["recover"]("mob", this["enemy"])
          }
        };
        ! function() {
          "use strict";
          var b = hr,
            c = b[0],
            d = "defineProperty",
            pcQ = "value",
            pcR = "enumerable",
            pcS = "configurable",
            pcT = "writable";
          Object["defineProperty"](a["prototype"], "fw", {
            ["value"]() {
              this["tB"]()
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](a["prototype"], "mw", {
            ["value"]() {
              var a = hr,
                b = a[3],
                c = "tw";
              Laya["Tween"]["killAll"](this["tw"]), this["tw"]["scale"](1, 1)
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          })
        } ["bind"](a)();
        return a
      } ["bind"](this)["apply"]();
