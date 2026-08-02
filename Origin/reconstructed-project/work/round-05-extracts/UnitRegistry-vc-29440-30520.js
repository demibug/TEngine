          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "gameOver", {
          ["value"](a) {
            var b = hr,
              c = b[0],
              d = "NP";
            for (let a of this["NP"]) this["cA"](a[0]);
            this["NP"]["clear"](), this["oA"](), this["KP"]["length"] = 0
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        })
      } ["bind"](b)();
      return b
    } ["bind"](this)["apply"](),
    vc = function() {
      var a = hr;
      let b = class extends qU {
        constructor() {
          var a = hr,
            b = a[0];
          var c = arguments;
          super(...c), this["rp"] = [], this["PA"] = new Map, this["AA"] = new Map, this["BM"] = new Map, this["EA"] = new Map, this["BA"] = new Map, this["DA"] = []
        }
      };
      ! function() {
        "use strict";
        var a = hr,
          c = a[6],
          d = a[0],
          e = "defineProperty",
          o5l = "value",
          o5m = "enumerable",
          o5n = "configurable",
          o5o = "writable";
        Object["defineProperty"](b["prototype"], "init", {
          ["value"]() {
            this["addEvent"]()
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "startGame", {
          ["value"]() {},
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "addEvent", {
          ["value"]() {
            var a = hr,
              b = a[0],
              c = "instance",
              d = "on";
            oc["instance"]["on"](sS["m"], this, this["IA"]), oc["instance"]["on"](sS["_"], this, this["CA"]), oc["instance"]["on"](sS["j"], this, this["TA"]), oc["instance"]["on"](sS["q"], this, this["RA"]), oc["instance"]["on"](sS["$"], this, this["UA"]), oc["instance"]["on"](sS["st"], this, this["tk"]), oc["instance"]["on"](sS["it"], this, this["hk"]), oc["instance"]["on"](sS["ht"], this, this["FA"]), oc["instance"]["on"](sS["et"], this, this["OA"]), oc["instance"]["on"](sS["ts"], this, this["YA"]), oc["instance"]["on"](sS["ns"], this, this["XA"])
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "gP", {
          ["value"](a, b, c, d, e, f = 1, g = null) {
            var h = hr,
              i = h[0];
            const j = {
              ["containerType"]: a,
              ["text"]: b,
              ["nm"]: c,
              ["x"]: d,
              ["y"]: e,
              ["We"]: f,
              ["L_"]: g
            };
            return this["GA"](j)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "GA", {
          ["value"](a) {
            var b = hr,
              c = b[0];
            if (uq["instance"]()["au"]["Qi"]) return null;
            const {
              ["containerType"]: d, ["text"]: e, ["nm"]: f, ["x"]: g, ["y"]: h, ["We"]: i = 1, ["L_"]: j
            } = a, k = this["HA"](e), l = this["WA"](k, e);
            this["jA"](l, d, e, f, g, h), this["zA"](l, k), j && this["NA"](l["id"], j), this["qA"](l, d, f, g, h), this["$A"](l, d, f, g, h, k);
            let m = i;
            return "Soldier" === k && 3 === d && 1 === i && (m = this["VA"](f, i)), m > 1 && l["X_"](m - l["level"], !1), this["QA"](l), l
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "VA", {
          ["value"](a, b) {
            var c = hr,
              d = hu,
              e = c[0],
              f = d[2],
              g = "instance",
              h = "props",
              i = "Ye",
              j = "je",
              k = "random";
            if (a) {
              if (!vb["instance"]()["MA"](a, f)) return b;
              const c = vb["instance"]()["LA"](f),
                d = .01 * uq["instance"]()["props"]["Ye"][f]["je"][c - 1];
              if (d > 0 && Math["random"]() < d) return 2
            } else
              for (const [a, b] of vb["instance"]()["NP"])
                if (f === b["type"] && !b["nm"]) {
                  const a = b["level"] || 1,
                    d = .01 * uq["instance"]()["props"]["Ye"][f]["je"][a - 1];
                  if (d > 0 && Math["random"]() < d) return 2;
                  break
                } return b
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "HA", {
          ["value"](a) {
            var b = hr,
              c = b[0],
              d = b[5],
              e = "indexOf",
              f = "Soldier";
            if ("农" === a) return "Farmer";
            const g = uq["instance"]()["Oc"];
            return -1 !== g["op"]["indexOf"](a) ? "Soldier" : -1 !== g["lp"]["indexOf"](a) ? "GeneralPart" : "Soldier"
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "WA", {
          ["value"](a, b) {
            var c = hr,
              d = c[5],
              e = c[0],
              f = "instance",
              g = "produce",
              h = "zx";
            switch (a) {
              case "Farmer":
                return sc["instance"]()["produce"](om);
              case "GeneralPart":
                return sc["instance"]()["produce"](tb["zx"][4]);
              case "Soldier":
                const i = uq["instance"]()["Oc"]["op"]["indexOf"](b);
                return sc["instance"]()["produce"](tb["zx"][i]);
              default:
                throw new Error(`未知的单位类型: ${a}`)
            }
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "jA", {
          ["value"](a, b, c, d, e, f) {
            var g = hr;
            a["Pw"](b, e, f), a["init"](c, d)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "zA", {
          ["value"](a, b) {
            var c = hr,
              d = c[0],
              e = "set",
              f = "id";
            a instanceof td ? this["PA"]["set"](a["id"], a) : a instanceof qo ? this["AA"]["set"](a["id"], a) : a instanceof om && this["EA"]["set"](a["id"], a)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "qA", {
          ["value"](a, b, c, d, e) {
            var f = hr;
            const g = na["instance"]()["ub"](b, c);
            g && g["setItem"](a, d, e)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "$A", {
          ["value"](a, b, c, d, e, f) {
            var g = hr,
              h = g[0];
            switch (b) {
              case 3:
                this["ZA"](a, c, d);
                break;
              case 1:
                this["KA"](a, c, d, e, f);
                break;
              case 5:
                this["JA"](a, d, e, f)
            }
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "ZA", {
          ["value"](a, b, c) {
            var d = hr,
              e = d[0],
              f = "instance",
              g = "event",
              h = "Oc";
            b ? oc["instance"]["event"](sS["Mt"], a["Oc"], c) : oc["instance"]["event"](sS["bt"], a["Oc"], 4, -5)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "KA", {
          ["value"](a, b, c, d, e) {
            var f = hr,
              g = f[0],
              h = f[9],
              i = "instance",
              j = "event";
            if (oc["instance"]["event"](sS["bt"], a["Oc"], c, d), "GeneralPart" === e) {
              const b = a;
              b["changeState"]("GeneralPartWait"), oc["instance"]["event"](sS["ts"], b)
            }
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "JA", {
          ["value"](a, b, c, d) {
            var e = hr,
              f = e[0],
              g = e[9],
              h = "instance",
              i = "event";
            if (oc["instance"]["event"](sS["ss"], a["Oc"], b, c), "GeneralPart" === d) {
              const b = a;
              b["changeState"]("GeneralPartWait"), oc["instance"]["event"](sS["ts"], b)
            }
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "WP", {
          ["value"](a) {
            var b = hr,
              c = b[0],
              d = b[6],
              e = "PA",
              f = "u_";
            let g = this["PA"]["get"](a);
            if (!g) return;
            const h = na["instance"]()["ub"](g["l_"], g["nm"]);
            h && h["removeItem"](g["u_"]["x"], g["u_"]["y"]), g["gameOver"](), this["PA"]["delete"](a)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "HP", {
          ["value"](a) {
            var b = hr,
              c = b[0],
              d = b[6],
              e = "AA",
              f = "u_";
            let g = this["AA"]["get"](a);
            if (!g) return;
            const h = na["instance"]()["ub"](g["l_"], g["nm"]);
            h && h["removeItem"](g["u_"]["x"], g["u_"]["y"]), g["gameOver"](), this["AA"]["delete"](a)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "Nb", {
          ["value"](a) {
            var b = hr,
              c = b[0],
              d = b[6],
              e = "EA",
              f = "u_";
            let g = this["EA"]["get"](a);
            if (!g) return;
            const h = na["instance"]()["ub"](g["l_"], g["nm"]);
            h && h["removeItem"](g["u_"]["x"], g["u_"]["y"]), g["gameOver"](), this["EA"]["delete"](a)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "tE", {
          ["value"](a) {
            var b = hr,
              c = b[0];
            const d = this["BM"]["get"](a);
            d && this["TA"](d["lp"][0]["id"])
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "sE", {
          ["value"](a, b = !0) {
            var c = hr,
              d = hu,
              e = c[14],
              f = c[0],
              g = c[7],
              h = c[1],
              i = c[2],
              j = c[4],
              k = c[6],
              l = "length",
              m = "r_",
              n = "instance",
              o = "Oc",
              p = "Yc",
              q = "weaponId",
              r = "player",
              s = "au",
              t = "Ai",
              u = "Ei",
              v = "bc",
              w = "id",
              x = "L_";
            var y;
            for (let b = 0; b < a["length"]; b++) a[b]["changeState"]("GeneralPartMerge");
            let z = a[0]["nm"],
              A = a[0]["r_"],
              B = "";
            for (let b = 0; b < a["length"]; b++) B += a[b]["P_"], A < a[b]["r_"] && (A = a[b]["r_"]);
            let C = uq["instance"]()["Oc"]["Yc"]["findIndex"](a => a === B),
              D = vM["iE"](C);
            if (b) {
              if (z) D["weaponId"] = uq["instance"]()["player"]["equip"][C];
              else
                for (let a = 0; a < uq["instance"]()["au"]["Ai"]["Ei"]["length"]; a++)
                  if (uq["instance"]()["Oc"]["Yc"][C] == uq["instance"]()["au"]["Ai"]["Ei"][a]["general"]) {
                    const b = uq["instance"]()["au"]["Ai"]["Ei"][a]["bc"],
                      c = null == (y = uq["instance"]()["bc"]["Jc"]["get"](b)) ? void 0 : y["type"];
                    if (void 0 !== c && 4 !== c) {
                      D["weaponId"] = b;
                      break
                    }
                  }
            } else D["weaponId"] = d[1];
            D["init"](a, z, C), D["hE"](A);
            let E, F = [];
            for (let b = 0; b < a["length"]; b++) {
              E = a[b], F["push"](E["id"]), E["Ux"] = D["id"];
              for (let a = 0; a < E["L_"]["length"]; a++) vd["instance"]()["applyBuff"](D["id"], E["L_"][a]["Vw"], E["L_"][a]["num"], E["L_"][a]["eE"])
            }
            return this["BA"]["set"](D["id"], F), this["QA"](D), z && b && uq["instance"]()["player"]["addMergedGeneral"](C), D
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "aE", {
          ["value"](a, b) {
            var c = hr,
              d = c[0];
            for (let e of this["BM"])
              if (a == e[1]["type"] && b == e[1]["nm"]) return !0;
            return !1
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "nE", {
          ["value"](a) {
            var b = hr,
              c = "length";
            let d = uq["instance"]()["Oc"]["merge"],
              e = [];
            for (let f = 0; f < d["length"]; f++)
              for (let g = 0; g < d[f]["length"]; g++)
                if (a == d[f][g]) {
                  for (let g = 0; g < d[f]["length"]; g++) a != d[f][g] && e["push"](d[f][g]);
                  break
                } for (let a = 0; a < e["length"]; a++)
              for (let d = a + 1; d < e["length"]; d++) e[d] == e[a] && (e["splice"](d, 1), d--);
            return e
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "IA", {
          ["value"](a, b) {
            var c = hr;
            this["BM"]["set"](a, b)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "CA", {
          ["value"](a) {
            var b = hr;
            this["BM"]["delete"](a)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "TA", {
          ["value"](a) {
            var b = hr,
              c = b[0],
              d = "BA",
              e = "length",
              f = "BM",
              g = "get",
              h = "lp";
            for (let i of this["BA"]["entries"]())
              for (let j = 0; j < i[1]["length"]; j++)
                if (i[1][j] == a) {
                  for (let a = 0; a < this["BM"]["get"](i[0])["lp"]["length"]; a++) {
                    const d = this["BM"]["get"](i[0])["lp"][a];
                    d["changeState"]("GeneralPartWait"), d["Ux"] = -1, d["k_"] = !1
                  }
                  return this["BM"]["get"](i[0])["gameOver"](), void this["BA"]["delete"](i[0])
                }
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "RA", {
          ["value"](a) {
            var b = hr;
            for (let c of this["BA"])
              for (let d = 0; d < c[1]["length"]; d++)
                if (a == c[1][d]) return c[0];
            return -1
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "FA", {
          ["value"](a, b, c = !1) {
            var d = hr,
              e = d[0],
              f = d[1],
              g = "BM",
              h = "has",
              i = "get",
              j = "hE",
              k = "length";
            !c && this["BM"]["has"](a) && this["BM"]["get"](a)["hE"](1);
            let l = (c ? .2 : .5) / b["length"];
            for (let a = 0; a < b["length"]; a++) this["BM"]["has"](b[a]) && this["BM"]["get"](b[a])["hE"](l)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "tk", {
          ["value"](a) {
            var b = hr,
              c = b[0],
              d = "get";
            let e = this["BM"]["get"](this["RA"](a));
            e || (e = this["PA"]["get"](a)), e && e["tk"]()
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "hk", {
          ["value"]() {
            var a = hr;
            qs["instance"]()["wg"](!1)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "rE", {
          ["value"](a, b) {
            var c = hr,
              d = c[0],
              e = "id",
              f = "lp",
              g = "length",
              h = "P_";
            let i = this["BM"]["get"](this["RA"](b["id"]));
            if (!i) return null;
            for (let b = 0; b < i["lp"]["length"]; b++)
              if (i["lp"][b]["id"] == a["id"]) return null;
            let j = i["lp"];
            for (let b = 0; b < j["length"]; b++)
              if (a["P_"] == j[b]["P_"]) return i;
            return null
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "oE", {
          ["value"](a, b, c, d) {
            var e = hr,
              f = e[0],
              g = e[5];
            let h = [];
            return this["BM"]["forEach"]((i, j) => {
              const k = i["general"];
              i["nm"] == d && np["Es"](c, a, b, k["x"], k["y"], k["width"], k["height"]) && h["push"]({
                ["id"]: j,
                ["x"]: k["x"],
                ["y"]: k["y"]
              })
            }), h
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "lE", {
          ["value"](a, b, c, d) {
            var e = hr,
              f = e[0];
            let g = [];
            return this["PA"]["forEach"]((h, i) => {
              const j = h["Oc"];
              h["nm"] == d && np["Es"](c, a, b, j["x"], j["y"], j["width"], j["height"]) && g["push"]({
                ["id"]: i,
                ["x"]: j["x"],
                ["y"]: j["y"]
              })
            }), g
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "cE", {
          ["value"](a, b, c, d) {
            var e = hr,
              f = e[0];
            let g = [];
            return this["AA"]["forEach"]((h, i) => {
              const j = h["Oc"];
              h["nm"] == d && np["Es"](c, a, b, j["x"], j["y"], j["width"], j["height"]) && g["push"]({
                ["id"]: i,
                ["x"]: j["x"],
                ["y"]: j["y"]
              })
            }), g
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "TM", {
          ["value"](a, b, c, d) {
            var e = hr,
              f = e[0];
            let g = [];
            return g = g["concat"](this["lE"](a, b, c, d), this["oE"](a, b, c, d)), g
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "uM", {
          ["value"](a) {
            var b = hr,
              c = b[0],
              d = "get";
            let e = this["PA"]["get"](a);
            return e || (e = this["AA"]["get"](a)), e || (e = this["EA"]["get"](a)), e
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "uE", {
          ["value"](a) {
            var b = hr;
            return this["BM"]["get"](a)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "pE", {
          ["value"](a, b) {
            var c = hr,
              d = c[0],
              e = c[5],
              f = "general",
              g = "Oc",
              h = "instance",
              i = "map",
              j = "ye",
              k = "gridHei";
            for (let c of this["BM"]) {
              const e = c[1]["general"]["x"],
                l = c[1]["general"]["y"];
              for (let f of c[1]["lp"])
                if ((e + f["Oc"]["x"]) / uq["instance"]()["map"]["ye"] == a && (l + f["Oc"]["y"]) / uq["instance"]()["map"]["gridHei"] == b) return {
                  ["x"]: e / uq["instance"]()["map"]["ye"] + .5,
                  ["y"]: l / uq["instance"]()["map"]["gridHei"]
                }
            }
            return {
              ["x"]: -1,
              ["y"]: -1
            }
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "UA", {
          ["value"](a, b, c = !0) {
            var d = hr,
              e = d[0],
              f = "yE",
              g = "get",
              h = "X_",
              i = "level",
              j = "instance",
              k = "Oc",
              l = "Ip",
              m = "length",
              n = "hE",
              o = "r_",
              p = "Dp";
            if (this["yE"] = this["PA"]["get"](a), this["yE"]) this["yE"]["X_"](b, c);
            else {
              if (this["yE"] = this["AA"]["get"](a), this["yE"]) {
                let a = this["BM"]["get"](this["RA"](this["yE"]["id"]));
                if (a)
                  if (a["Ap"]) {
                    if (a["level"] + b - 1 < 0 || a["level"] + b - 1 >= uq["instance"]()["Oc"]["Ip"]["length"]) return;
                    a["hE"](uq["instance"]()["Oc"]["Ip"][a["level"] + b - 1] - a["r_"], c)
                  } else {
                    if (a["level"] + b - 1 < 0 || a["level"] + b - 1 >= uq["instance"]()["Oc"]["Dp"]["length"]) return;
                    a["hE"](uq["instance"]()["Oc"]["Dp"][a["level"] + b - 1] - a["r_"], c)
                  }
                else this["yE"]["X_"](b, c);
                return
              }
              this["yE"] = this["EA"]["get"](a), this["yE"] && this["yE"]["X_"](b, c)
            }
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "OA", {
          ["value"](a) {
            var b = hr,
              c = b[0],
              d = "yE",
              e = "get",
              f = "u_";
            let g = !0;
            this["yE"] = this["PA"]["get"](a), this["yE"] || (this["yE"] = this["AA"]["get"](a), g = !1);
            let h = this["yE"]["nm"],
              i = this["yE"]["l_"],
              j = this["yE"]["u_"]["x"],
              k = this["yE"]["u_"]["y"],
              l = this["yE"]["level"],
              m = this["yE"]["L_"]["concat"]();
            g ? this["WP"](a) : this["HP"](a), this["gP"](i, this["fE"](this["yE"]["P_"]), h, j, k, l, m)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "fE", {
          ["value"](a) {
            var b = hr,
              c = b[0],
              d = "eh",
              e = "rp",
              f = "length";
            let g = uq["instance"]()["eh"]["eh"];
            this["rp"]["length"] = 0;
            for (let c = 0; c < g["length"]; c++) g[c] != a && "铲" != g[c] && this["rp"]["push"](g[c]);
            return this["rp"][np["range"](0, this["rp"]["length"], !0)]
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "IM", {
          ["value"](a, b, c, d, e) {
            var f = hr,
              g = f[1],
              h = f[0],
              i = f[2],
              j = "get",
              k = "BM",
              l = "lp",
              m = "L_",
              n = "push",
              o8c = "type",
              o8d = "Vw",
              o8e = "num",
              o8f = "eE";
            let o = this["PA"]["get"](a),
              p = this["BM"]["get"](a);
            if (o || (p = this["BM"]["get"](a)), o || p)
              if (p)
                for (let a = 0; a < p["lp"]["length"]; a++) p["lp"][a]["L_"]["push"]({
                  ["type"]: b,
                  ["Vw"]: c,
                  ["num"]: d,
                  ["eE"]: e
                });
              else o["L_"]["push"]({
                ["type"]: b,
                ["Vw"]: c,
                ["num"]: d,
                ["eE"]: e
              })
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "NA", {
          ["value"](a, b) {
            var c = hr,
              d = c[0],
              e = c[4],
              f = "get",
              g = "L_",
              h = "type";
            let i = this["PA"]["get"](a),
              j = !1;
            if (i || (i = this["AA"]["get"](a), j = !0), !i) return void console["error"]("没有找到文字", a);
            let k = i["L_"]["find"](a => 0 == a["type"]);
            if (i["L_"] = i["L_"]["concat"](b), !j)
              for (let a = 0; a < b["length"]; a++) k && 0 == b[a]["type"] || vd["instance"]()["applyBuff"](i["id"], b[a]["Vw"], b[a]["num"], b[a]["eE"])
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "gE", {
          ["value"](a) {
            var b = hr,
              c = hu,
              d = b[0],
              o8o = "id",
              e = "level";
            let f;
            for (let g of this["PA"]) a == g[1]["nm"] && 1 == g[1]["l_"] && (vd["instance"]()["dE"](g[1]["id"], c[11]) || (f ? g[1]["level"] > f["level"] && (f = g[1]) : f = g[1]));
            return f ? {
              ["id"]: f["id"],
              ["We"]: f["level"]
            } : null
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "LE", {
          ["value"](a) {
            var b = hr,
              c = b[0],
              d = "level",
              o8t = "id",
              o8u = "type",
              e = "Oc";
            let f = [],
              g = 5;
            for (let h of this["PA"]) a == h[1]["nm"] && 1 == h[1]["l_"] && h[1]["level"] <= g && (h[1]["level"] < g && (f["length"] = 0, g = h[1]["level"]), f["push"]({
              ["id"]: h[1]["id"],
              ["type"]: h[1]["type"],
              ["We"]: h[1]["level"],
              ["x"]: h[1]["Oc"]["x"],
              ["y"]: h[1]["Oc"]["y"]
            }));
            return f
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "mE", {
          ["value"](a, b, c) {
            var d = hr,
              e = d[0],
              f = "nm",
              g = "l_",
              h = "u_";
            for (let d of this["PA"])
              if (d[1]["nm"] == a && 1 == d[1]["l_"] && d[1]["u_"]["x"] == b && d[1]["u_"]["y"] == c) return !0;
            for (let d of this["AA"])
              if (d[1]["nm"] == a && 1 == d[1]["l_"] && d[1]["u_"]["x"] == b && d[1]["u_"]["y"] == c) return !0;
            for (let d of this["EA"])
              if (d[1]["nm"] == a && 1 == d[1]["l_"] && d[1]["u_"]["x"] == b && d[1]["u_"]["y"] == c) return !0;
            return !1
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "LP", {
          ["value"](a, b, c, d, e, f) {
            var g = hr,
              h = g[2],
              i = g[0],
              j = g[1],
              o8F = "nm",
              o8G = "map",
              k = "instance",
              l = "applyBuff",
              m = "id",
              n = "set";
            let o = {
              ["sign"]: a,
              ["nm"]: b,
              ["Vw"]: c,
              ["num"]: d,
              ["eE"]: e,
              ["time"]: f,
              ["map"]: new Map
            };
            this["DA"]["push"](o);
            for (let a of this["PA"]) {
              if (a[1]["nm"] != b) continue;
              const g = vd["instance"]()["applyBuff"](a[1]["id"], c, d, e, f);
              o["map"]["set"](a[1]["id"], g)
            }
            for (let a of this["BM"]) {
              if (a[1]["nm"] != b) continue;
              const g = vd["instance"]()["applyBuff"](a[1]["id"], c, d, e, f);
              o["map"]["set"](a[1]["id"], g)
            }
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "mP", {
          ["value"](a) {
            var b = hr,
              c = b[0],
              d = "DA";
            const e = this["DA"]["findIndex"](c => c["sign"] == a);
            if (e < 0) return;
            const f = this["DA"][e];
            for (let a of f["map"]) a[1] >= 0 && vd["instance"]()["Jw"](a[0], f["Vw"], a[1]);
            this["DA"]["splice"](e, 1)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "XA", {
          ["value"](a) {
            var b = hr,
              c = b[0],
              d = b[4],
              e = b[2],
              f = "DA",
              g = "map";
            for (let h = 0; h < this["DA"]["length"]; h++) {
              const i = this["DA"][h];
              if (!i["sign"]["startsWith"]("rain")) continue;
              const j = i["map"]["get"](a);
              void 0 === j || j < 0 || (vd["instance"]()["Jw"](a, i["Vw"], j), i["map"]["delete"](a))
            }
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "QA", {
          ["value"](a) {
            var b = hr,
              c = b[0],
              d = b[1],
              e = "DA",
              f = "nm",
              g = "id";
            for (let h = 0; h < this["DA"]["length"]; h++) {
              if (a["nm"] != this["DA"][h]["nm"]) continue;
              const i = vd["instance"]()["applyBuff"](a["id"], this["DA"][h]["Vw"], this["DA"][h]["num"], this["DA"][h]["eE"], this["DA"][h]["time"]);
              this["DA"][h]["map"]["set"](a["id"], i)
            }
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "YA", {
          ["value"](a) {
            var b = hr,
              c = b[0],
              d = b[1],
              e = b[2],
              f = "instance",
              g = "Oc",
              h = "ub",
              i = "nm",
              j = "sb",
              k = "Jx",
              l = "length",
              m = "map",
              n = "pe",
              o = "P_",
              p = "push",
              o9f = "arr",
              o9g = "type",
              q = "u_",
              r = "sE",
              s = "familyName",
              t = "indexOf",
              u = "dp";
            if (-1 != a["Ux"]) return;
            let v = uq["instance"]()["Oc"]["merge"];
            const w = a["l_"];
            let x, y, z;
            if (5 === w) {
              const b = na["instance"]()["ub"](5, a["nm"]);
              x = b["sb"], y = b["Jx"]["length"], z = b["Jx"][0]["length"]
            } else {
              x = na["instance"]()["ub"](1, a["nm"])["sb"], y = uq["instance"]()["map"]["pe"]["length"], z = uq["instance"]()["map"]["pe"][0]["length"]
            }
            let A = [];
            for (let b = 0; b < v["length"]; b++)
              for (let c = 0; c < v[b]["length"]; c++)
                if (a["P_"] == v[b][c]) {
                  A["push"]({
                    ["arr"]: v[b],
                    ["i"]: c,
                    ["type"]: b
                  });
                  break
                } let B, C = [];
            for (let b = 0; b < A["length"]; b++) {
              let d = !0;
              C["length"] = 0;
              for (let c = 0; c < A[b]["arr"]["length"]; c++) {
                if (B = a["u_"]["x"] + (c - A[b]["i"]), B < 0 || B >= y) {
                  d = !1;
                  continue
                }
                let e = x[B][a["u_"]["y"]];
                e && e["P_"] == A[b]["arr"][c] ? C["push"](e) : d = !1
              }
              if (d) {
                if (this["aE"](A[b]["type"], a["nm"])) continue;
                this["sE"](C);
                return
              }
            }
            if (!uq["instance"]()["Oc"]["Ep"]) return;
            if (5 === w) return;
            if (C["length"] = 0, uq["instance"]()["Oc"]["familyName"]["indexOf"](a["P_"]) >= 0) {
              if (B = a["u_"]["x"] + 1, B >= y) return;
              let b = x[B][a["u_"]["y"]];
              if (!b || uq["instance"]()["Oc"]["dp"]["indexOf"](b["P_"]) < 0) return;
              C["push"](a), C["push"](b)
            } else if (uq["instance"]()["Oc"]["dp"]["indexOf"](a["P_"]) >= 0) {
              if (B = a["u_"]["x"] - 1, B < 0) return;
              let b = x[B][a["u_"]["y"]];
              if (!b || uq["instance"]()["Oc"]["familyName"]["indexOf"](b["P_"]) < 0) return;
              C["push"](b), C["push"](a)
            }
            this["sE"](C, !1)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "gameOver", {
          ["value"]() {
            var a = hr,
              b = a[0],
              c = "rp",
              d = "length",
              e = "BM",
              f = "clear",
              g = "PA",
              h = "push",
              i = "AA",
              j = "EA";
            this["rp"]["length"] = 0;
            for (let b of this["BM"]) b[1]["gameOver"]();
            this["BM"]["clear"](), this["BA"]["clear"](), this["rp"]["length"] = 0;
            for (let a of this["PA"]) this["rp"]["push"](a[0]);
            for (let a of this["rp"]) this["WP"](a);
            this["rp"]["length"] = 0;
            for (let a of this["AA"]) this["rp"]["push"](a[0]);
            for (let a of this["rp"]) this["HP"](a);
            this["rp"]["length"] = 0;
            for (let a of this["EA"]) this["rp"]["push"](a[0]);
            for (let a of this["rp"]) this["Nb"](a);
            this["PA"]["clear"](), this["AA"]["clear"](), this["EA"]["clear"](), this["DA"]["length"] = 0
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        })
      } ["bind"](b)();
      return b
    } ["bind"](this)["apply"]();
  for (let a of lm) {
    if (-1 == a) {} else if (0 == a) {
      continue
    }
  }
  var vd = function() {
      var a = hr;
      let b = class extends qU {};
      ! function() {
        "use strict";
        var a = hr,
          c = a[7],
          d = a[0],
          e = a[4],
          f = "defineProperty",
          o9C = "value",
          o9D = "enumerable",
          o9E = "configurable",
          o9F = "writable";
        Object["defineProperty"](b["prototype"], "init", {
          ["value"]() {
            var a = hr,
              b = a[0],
              c = "wE",
              d = "instance",
              e = "on";
            this["wE"] = uq["instance"]()["Py"], this["vE"] = new nB(this["wE"]), this["_E"] = new Map, oJ["instance"]()["init"](), oc["instance"]["on"](sS["hs"], this, this["kE"]), oc["instance"]["on"](sS["es"], this, this["SE"])
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "startGame", {
          ["value"]() {
            var a = hr;
            nx["instance"]()["La"]("BuffMgr", this, this["update"])
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "xE", {
          ["value"](a) {
            var b = hr,
              c = b[1],
              d = "_E";
            let e = this["_E"]["get"](a);
            return e || (e = new Map, this["_E"]["set"](a, e)), e
          },
          ["enumerable"]: false,
