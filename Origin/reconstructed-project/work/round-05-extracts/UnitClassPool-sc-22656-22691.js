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
