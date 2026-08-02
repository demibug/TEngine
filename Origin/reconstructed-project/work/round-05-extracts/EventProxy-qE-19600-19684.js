      } ["bind"](this)["apply"](), qE = function() {
        let a;
        a = class {
          constructor() {
            this["objectType"] = 0
          }
        };
        ! function() {
          "use strict";
          var b = hr,
            c = "defineProperty",
            ojj = "value",
            ojk = "enumerable",
            ojl = "configurable",
            ojm = "writable";
          Object["defineProperty"](a["prototype"], "once", {
            ["value"](a, b, c) {
              var d = hr,
                e = "am",
                f = "once";
              return c ? this["am"]()["once"](a, c, b) : this["am"]()["once"](a, b)
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](a["prototype"], "on", {
            ["value"](a, b, c) {
              var d = hr,
                e = d[0],
                f = "am",
                g = "on";
              return c ? this["am"]()["on"](a, c, b) : this["am"]()["on"](a, b)
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](a["prototype"], "off", {
            ["value"](a, b) {
              var c = hr;
              return this["am"]()["off"](a, b)
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](a["prototype"], "event", {
            ["value"](a, b) {
              var c = hr;
              return this["am"]()["event"](a, b)
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](a["prototype"], "offAllCaller", {
            ["value"](a) {
              var b = hr;
              return this["am"]()["offAllCaller"](a)
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](a["prototype"], "offAll", {
            ["value"](a) {
              var b = hr;
              return this["am"]()["offAll"](a)
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](a["prototype"], "gameOver", {
            ["value"]() {
              var a = hr;
              this["event"]("onDestroy")
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          })
        } ["bind"](a)();
        return a
