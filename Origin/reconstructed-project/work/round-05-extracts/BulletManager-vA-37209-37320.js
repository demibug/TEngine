  var vA = function() {
      var a = hr;
      let b = class extends qU {
        constructor() {
          var a = hr,
            b = a[0],
            c = "Point";
          var d = arguments;
          super(...d), this["NI"] = hu[81], this["qI"] = {
            ["x"]: 0,
            ["y"]: 0,
            ["TI"]: 0,
            ["RI"]: 0,
            ["r"]: 0
          }, this["$I"] = [], this["VI"] = new Laya["Point"], this["QI"] = new Laya["Point"]
        }
      };
      ! function() {
        "use strict";
        var a = hr,
          c = a[0],
          d = "defineProperty",
          pFU = "value",
          pFV = "enumerable",
          pFW = "configurable",
          pFX = "writable";
        Object["defineProperty"](b["prototype"], "init", {
          ["value"]() {
            var a = hr,
              b = a[0],
              c = "KI",
              d = "instance",
              e = "JI",
              f = "map";
            this["ZI"] = [], this["KI"] = vi["instance"](), this["JI"] = uq["instance"](), this["KI"] = vi["instance"](), this["Wv"] = this["JI"]["map"]["ye"], this["jv"] = this["JI"]["map"]["gridHei"], nx["instance"]()["La"]("bulletMgr", this, this["update"])
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "gx", {
          ["value"](a, b = {
            ["x"]: 0,
            ["y"]: 0
          }) {
            var c = hr,
              d = c[0],
              e = "Ty",
              f = "yS",
              g = "Xk",
              h = "Wk",
              i = "Hk",
              j = "Gk";
            let k;
            this["Ty"] || (this["Ty"] = this["JI"]["Ty"]), k = vk["produce"](a), a["yS"] && (k["yS"] = a["yS"]);
            const l = a["Xk"];
            if (l) {
              const a = k["Xk"];
              null != l["Wk"] && (a["Wk"] = l["Wk"]), null != l["Hk"] && (a["Hk"] = l["Hk"]), null != l["Gk"] && (a["Gk"] = l["Gk"])
            }
            return k["pos"](b["x"], b["y"]), k["resetData"](a), this["ZI"]["push"](k), k["rS"] && a["sS"], k
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "tC", {
          ["value"](a) {
            var b = hr,
              c = b[0];
            const d = this["ZI"]["findIndex"](b => b["id"] === a);
            d >= 0 && this["sC"](d)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "aS", {
          ["value"](a) {
            var b = hr,
              c = b[0];
            const d = this["ZI"]["indexOf"](a);
            d >= 0 && this["sC"](d)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "sC", {
          ["value"](a) {
            var b = hr,
              c = b[0],
              d = "ZI";
            const e = this["ZI"][a];
            e["gS"]["Sk"](), vk["recover"](e), this["ZI"]["splice"](a, 1)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "update", {
          ["value"](a) {
            var b = hr,
              c = b[0],
              d = b[2],
              e = b[11],
              f = b[1],
              g = b[4],
              h = b[3],
              i = "ZI",
              j = "length",
              pGs = "ak",
