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
              k = "iC",
              l = "rS",
              m = "stage",
              n = "width",
              o = "NI",
              p = "height",
              q = "mS",
              r = "Wk",
              s = "Vk",
              t = "hC",
              u = "Gk",
              v = "$I",
              w = "JI",
              x = "Cy",
              y = "VI",
              z = "copy",
              A = "Point",
              B = "TEMP",
              C = "setTo",
              D = "QI",
              E = "KI",
              pGO = "id",
              F = "parent",
              G = "qI",
              H = "TI",
              I = "Wv",
              J = "RI",
              K = "jv",
              L = "$k",
              M = "splice",
              N = "ek",
              O = "eC",
              P = "yk",
              Q = "hitDelayTimer",
              R = "aC",
              S = "ck",
              T = "uk",
              U = "both",
              V = "pk",
              W = "Hk";
            var X = "nC",
              Y = "sC",
              Z = "timer";
            var _0, _1, _2;
            for (let g = this["ZI"]["length"] - 1; g >= 0; g--) {
              const h = this["ZI"][g],
                {
                  ["yS"]: _3,
                  ["ak"]: _4,
                  ["Zk"]: _5
                } = h,
                _6 = h["iS"];
              if (!_6) continue;
              this["iC"] = !1, this["rS"] = !1, _5 || h["BS"] || (h["x"] > Laya["stage"]["width"] + this["NI"] || h["y"] > Laya["stage"]["height"] + this["NI"] || h["x"] < -this["NI"] || h["y"] < -this["NI"]) && (this["rS"] = !0);
              const _7 = h["Xk"];
              if (h["mS"] && (h["gS"]["Tk"](a, null != (_0 = _7["Wk"]) ? _0 : h["Wk"]), h["update"](a)), this["rS"] = this["rS"] || h["rS"] || h["Vk"], h["mS"]) {
                if (this["hC"] = _3["ak"] && _4 && !this["rS"], this["hC"]) {
                  const a = !(!_7["Gk"] && !h["Gk"]),
                    g = a ? null != (_1 = _7["Gk"]) ? _1 : h["Gk"] : h["eS"];
                  if (this["$I"] = [], _5) {
                    const a = this["JI"]["Cy"](g, !0);
                    this["VI"]["copy"](a)
                  } else {
                    let b;
                    Laya["Point"]["TEMP"]["setTo"](g["width"] / 2, g["height"] / 2), b = a ? this["JI"]["Cy"](g, Laya["Point"]["TEMP"]) : g["toParentPoint"](Laya["Point"]["TEMP"]), this["VI"]["copy"](b)
                  }
                  const i = this["VI"]["x"],
                    m = this["VI"]["y"];
                  this["QI"]["setTo"](1, 1);
                  const o = Math["max"](this["QI"]["x"], this["QI"]["y"]) * Math["sqrt"](g["width"] * g["width"] + g["height"] * g["height"]) / 2;
                  this["KI"]["UB"](i, m, o, _6["nm"], this["$I"]);
                  for (let d = this["$I"]["length"] - 1; d >= 0; d--) {
                    const {
                      ["enemy"]: i, ["id"]: j
                    } = this["$I"][d];
                    _5 ? (this["VI"]["setTo"](i["x"], i["y"]), i["parent"]["localToGlobal"](this["VI"]), g["parent"]["globalToLocal"](this["VI"]), this["qI"]["x"] = this["VI"]["x"], this["qI"]["y"] = this["VI"]["y"], this["qI"]["TI"] = this["Wv"] / this["QI"]["x"], this["qI"]["RI"] = this["jv"] / this["QI"]["y"]) : (this["qI"]["x"] = i["x"], this["qI"]["y"] = i["y"], this["qI"]["TI"] = this["Wv"], this["qI"]["RI"] = this["jv"]), this["qI"]["r"] = 0, h["$k"]["has"](j) ? this["$I"]["splice"](d, 1) : a ? vx["FI"](g, this["qI"]) || this["$I"]["splice"](d, 1) : vx["OI"](g, this["qI"]) || this["$I"]["splice"](d, 1)
                  }
                  this["$I"]["length"] > 1 && !_3["ek"] && h["eC"] && this["$I"]["sort"](h["eC"]);
                  for (let a = 0; a < this["$I"]["length"]; a++) {
                    const {
                      ["id"]: b
                    } = this["$I"][a], d = this["KI"]["JS"]["get"](b);
                    if (d && h["vS"](d)) {
                      if (h["hit"](d), this["iC"] = !0, !_3["ek"]) {
                        this["rS"] = !0;
                        break
                      }
                      if (h["rS"]) {
                        this["rS"] = !0;
                        break
                      }
                      h["$k"]["add"](b)
                    }
                  }
                  this["iC"] && h["SS"]()
                }
                _3 instanceof oE && !_3["fk"] && (_3["yk"] ? (h["hitDelayTimer"] <= 0 && (this["aC"](h, _3), _3["ck"] && (this["rS"] = !0)), h["hitDelayTimer"] -= a) : (this["rS"] && ("requestRemove" === _3["uk"] || "both" === _3["uk"]) || _4 && ("hitEnable" === _3["uk"] || "both" === _3["uk"])) && (_3["pk"] > 0 ? (_3["yk"] = !0, h["hitDelayTimer"] = _3["pk"]) : (this["aC"](h, _3), _3["ck"] && (this["rS"] = !0))))
              }
              if (this["rS"]) {
                h["bS"]();
                const b = null != (_2 = _7["Hk"]) ? _2 : h["Hk"];
                0 == b || h["nS"] ? (this["nC"](h, _3), this["sC"](g)) : h["Vk"] ? (h["timer"] -= a, h["timer"] <= 0 && (this["nC"](h, _3), this["sC"](g), delete h.timer)) : (h["Vk"] = !0, h["timer"] = b)
              }
            }
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "nC", {
          ["value"](a, b) {
            var c = hr,
              d = c[0];
            b instanceof oE && !b["fk"] && b["rC"] && this["aC"](a, b)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "aC", {
          ["value"](a, b) {
            var c = hr,
              d = c[0],
              e = c[1];
            let f = !1;
            for (let c of b["lk"]) {
              const b = a["sx"]["JS"]["get"](c);
              b && (a["hit"](b), f = !0)
            }
            f && a["SS"](), b["fk"] = !0
