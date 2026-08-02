  var vi = function() {
      var a = hr;
      let b = class extends qU {
        constructor() {
          var a = hr,
            b = a[0],
            c = "Point";
          var d = arguments;
          super(...d), this["rp"] = [], this["Vy"] = new Laya["Point"], this["Qy"] = new Laya["Point"], this["JS"] = new Map, this["LB"] = [], this["mB"] = new Map, this["wB"] = new Map, this["gridSize"] = hu[65], this["vB"] = [], this["_B"] = 0, this["DA"] = []
        }
      };
      ! function() {
        "use strict";
        var a = hr,
          c = a[6],
          d = a[0],
          e = "defineProperty",
          pnn = "value",
          pno = "enumerable",
          pnp = "configurable",
          pnq = "writable";
        Object["defineProperty"](b["prototype"], "init", {
          ["value"]() {
            var a = hr,
              b = a[6],
              c = a[0],
              d = "instance";
            const e = uq["instance"]()["map"];
            this["gridSize"] = e["ye"], nx["instance"]()["La"]("enemyMgr", this, this["update"]), this["addEvent"]()
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
            oc["instance"]["on"](sS["nt"], this, this["kB"]), oc["instance"]["on"](sS["ot"], this, this["SB"]), oc["instance"]["on"](sS["ut"], this, this["sB"]), oc["instance"]["on"](sS["ft"], this, this["xB"])
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "update", {
          ["value"](a) {
            var b = hr,
              c = b[0];
            this["bB"](a), this["MB"](a)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "bB", {
          ["value"](a) {
            var b = hr,
              c = b[4],
              d = "vB",
              e = "length",
              f = "curState";
            this["vB"]["length"] = 0;
            for (const a of this["JS"]["values"]()) this["vB"]["push"](a);
            for (let b = 0, g = this["vB"]["length"]; b < g; b++) {
              const e = this["vB"][b];
              4 !== e["curState"] && 0 !== e["curState"] && e["update"](a)
            }
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "kB", {
          ["value"](a, b) {
            var c = hr,
              d = c[0];
            this["JS"]["set"](a, b), this["PB"](a, b)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "SB", {
          ["value"](a) {
            var b = hr,
              c = b[0];
            this["AB"](a), this["JS"]["delete"](a)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "EB", {
          ["value"](a, b) {
            return `${a}_${b}`
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "BB", {
          ["value"](a) {
            var b = hr,
              c = b[0],
              d = b[3],
              e = "enemy",
              f = "floor",
              g = "gridSize";
            const h = a["enemy"]["x"] + a["enemy"]["width"] / 2,
              i = a["enemy"]["y"] + a["enemy"]["height"] / 2;
            return {
              ["DB"]: Math["floor"](h / this["gridSize"]),
              ["IB"]: Math["floor"](i / this["gridSize"])
            }
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "PB", {
          ["value"](a, b) {
            var c = hr,
              d = c[0],
              e = c[1],
              f = "mB",
              g = "set";
            this["AB"](a);
            const {
              ["DB"]: h, ["IB"]: i
            } = this["BB"](b), j = this["EB"](h, i);
            this["mB"]["has"](j) || this["mB"]["set"](j, new Set), this["mB"]["get"](j)["add"](a), this["wB"]["set"](a, j)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "AB", {
          ["value"](a) {
            var b = hr,
              c = b[0],
              d = "wB",
              e = "get",
              f = "mB",
              g = "delete";
            const h = this["wB"]["get"](a);
            if (h) {
              const c = this["mB"]["get"](h);
              c && (c["delete"](a), 0 === c["size"] && this["mB"]["delete"](h)), this["wB"]["delete"](a)
            }
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "xB", {
          ["value"](a, b) {
            var c = hr,
              d = c[0];
            const {
              ["DB"]: e, ["IB"]: f
            } = this["BB"](b), g = this["EB"](e, f);
            this["wB"]["get"](a) !== g && this["PB"](a, b)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "CB", {
          ["value"](a, b, c) {
            var d = hr,
              e = d[0],
              f = d[1],
              g = "floor",
              h = "gridSize";
            const i = new Set,
              j = Math["floor"]((a - c) / this["gridSize"]),
              k = Math["floor"]((a + c) / this["gridSize"]),
              l = Math["floor"]((b - c) / this["gridSize"]),
              m = Math["floor"]((b + c) / this["gridSize"]);
            for (let a = j; a <= k; a++)
              for (let b = l; b <= m; b++) {
                const c = this["EB"](a, b),
                  d = this["mB"]["get"](c);
                if (d)
                  for (const a of d) i["add"](a)
              }
            return i
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "jL", {
          ["value"](a, b, c = !1) {
            var d = hr,
              e = d[0],
              f = d[2],
              g = "instance";
            const h = ss["NL"][a],
              i = s0["instance"]()["WL"](h);
            return i["type"] = a, i["Gm"] = c, i["init"](b), this["QA"](i), oc["instance"]["event"](sS["Ht"], b), i
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "TB", {
          ["value"](a, b) {
            var c = hr,
              d = c[0],
              e = c[2],
              f = "instance";
            const g = ss["$L"][a],
              h = s0["instance"]()["WL"](g);
            return h["type"] = a, h["init"](b), this["QA"](h), oc["instance"]["event"](sS["Ht"], b, !0), h
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "RB", {
          ["value"](a) {
            var b = hr,
              c = b[0];
            let d = [];
            return this["JS"]["forEach"](e => {
              e["cw"](a) && d["push"](e)
            }), d
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "qx", {
          ["value"](a, b, c, d) {
            var e = hr,
              f = e[1],
              g = e[0],
              h = e[3],
              i = "floor",
              j = "gridSize",
              k = "get",
              l = "enemy",
              poq = "Bm";
            let m = [];
            const n = uq["instance"]()["map"],
              o = Math["floor"]((a - c) / this["gridSize"]),
              p = Math["floor"]((a + c) / this["gridSize"]),
              q = Math["floor"]((b - c) / this["gridSize"]),
              r = Math["floor"]((b + c) / this["gridSize"]),
              s = new Set;
            for (let h = o; h <= p; h++)
              for (let i = q; i <= r; i++) {
                const j = this["EB"](h, i),
                  o = this["mB"]["get"](j);
                if (o)
                  for (const h of o) {
                    if (s["has"](h)) continue;
                    s["add"](h);
                    const i = this["JS"]["get"](h);
                    i && (i["cw"](d) && np["Es"](c, a, b, i["enemy"]["x"], i["enemy"]["y"], n["ye"], n["gridHei"]) && m["push"]({
                      ["id"]: h,
                      ["x"]: i["enemy"]["x"],
                      ["y"]: i["enemy"]["y"],
                      ["Bm"]: i["Bm"]
                    }))
                  }
              }
            return m
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "UB", {
          ["value"](a, b, c, d, e) {
            var f = hr,
              g = f[1],
              h = f[0],
              i = "enemy";
            const j = uq["instance"]()["map"],
              k = this["CB"](a, b, c);
            for (const l of k) {
              const k = this["JS"]["get"](l);
              k && (k["cw"](d) && np["Es"](c, a, b, k["enemy"]["x"], k["enemy"]["y"], j["ye"], j["gridHei"]) && e["push"](k))
            }
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "FB", {
          ["value"](a) {
            var b = hr,
              c = "JS",
              d = "get",
              e = "enemy";
            return this["JS"]["get"](a) ? {
              ["x"]: this["JS"]["get"](a)["enemy"]["x"],
              ["y"]: this["JS"]["get"](a)["enemy"]["y"]
            } : null
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "OB", {
          ["value"](a, b, c) {
            var d = hr,
              e = d[1],
              f = d[0],
              poC = "id",
              g = "enemy",
              poE = "Bm";
            let h = [];
            const i = uq["instance"]()["map"],
              j = this["CB"](a["x"], a["y"], b);
            for (const k of j) {
              if (k === a["id"]) continue;
              const j = this["JS"]["get"](k);
              j && (j["cw"](c) && np["Es"](b, a["x"], a["y"], j["enemy"]["x"], j["enemy"]["y"], i["ye"], i["gridHei"]) && h["push"]({
                ["id"]: k,
                ["x"]: j["enemy"]["x"],
                ["y"]: j["enemy"]["y"],
                ["Bm"]: j["Bm"]
              }))
            }
            return h
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "Cv", {
          ["value"](a, b, c) {
            var d = hr,
              e = d[0],
              f = d[1];
            let g;
            for (let h = 0; h < b["length"]; h++) g = this["JS"]["get"](b[h]["id"]), g && g["hit"](a, c)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "GE", {
          ["value"](a) {
            var b = hr;
            this["JS"]["get"](a)["gameOver"]()
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "YB", {
          ["value"](a, b) {
            var c = hr,
              d = c[0],
              e = "JS",
              f = "push",
              poN = "Bm",
              poO = "id",
              g = "enemy";
            let h;
            h = function(a, b, c) {
              [a[b], a[c]] = [a[c], a[b]]
            };
            let i = [];
            for (let a of this["JS"]) a[1]["cw"](b) && i["push"]([a[0], a[1]["Bm"]]);
            ! function a(b, c, d) {
              if (c < d) {
                const e = function(a, b, c) {
                  var d = hr;
                  const e = Math["floor"]((b + c) / 2);
                  a[b][1] > a[c][1] && h(a, b, c), a[e][1] > a[c][1] && h(a, e, c), a[e][1] > a[b][1] && h(a, e, b);
                  const f = a[b][1];
                  let g = b + 1;
                  for (let b = g; b <= c; b++) a[b][1] <= f && (h(a, g, b), g++);
                  return h(a, b, g - 1), g - 1
                }(b, c, d);
                a(b, c, e - 1), a(b, e + 1, d)
              }
            }(i, 0, i["length"] - 1);
            let j, k = [];
            for (let b = 0; b < a && i[b]; b++) j = this["JS"]["get"](i[b][0]), k["push"]({
              ["id"]: j["id"],
              ["x"]: j["enemy"]["x"],
              ["y"]: j["enemy"]["y"],
              ["Bm"]: j["Bm"]
            });
            return k
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "XB", {
          ["value"](a, b) {
            var c = hr,
              d = c[2],
              e = c[4],
              f = c[0],
              g = "JS",
              poW = "id",
              h = "enemy",
              poY = "Bm";
            let i = new Array,
              j = .8;
            this["JS"]["size"] < a && (j = 1);
            for (let f of this["JS"])
              if (f[1]["cw"](b) && Math["random"]() < j && (i["push"]({
                  ["id"]: f[1]["id"],
                  ["x"]: f[1]["enemy"]["x"],
                  ["y"]: f[1]["enemy"]["y"],
                  ["Bm"]: f[1]["Bm"]
                }), i["length"] >= a)) break;
            return i
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "GB", {
          ["value"](a) {
            var b = hr,
              c = b[0],
              po1 = "id",
              po2 = "Bm",
              d = "rp",
              e = "length",
              f = "enemy";
            let g = {
              ["id"]: -1,
              ["x"]: 0,
              ["y"]: 0,
              ["Bm"]: 1 / 0
            };
            this["rp"]["length"] = 0;
            for (let e of this["JS"]) e[1]["cw"](a) && this["rp"]["push"](e[1]);
            if (this["rp"]["length"] <= 0) return g;
            let h = this["rp"][np["range"](0, this["rp"]["length"], !0)];
            return -1 == h["id"] ? g : {
              ["id"]: h["id"],
              ["x"]: h["enemy"]["x"],
              ["y"]: h["enemy"]["y"],
              ["Bm"]: h["Bm"]
            }
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "HB", {
          ["value"](a) {
            var b = hr,
              c = b[0],
              po8 = "id",
              po9 = "ph",
              d = "JS",
              e = "Zi",
              ppc = "Bm";
            let f = {
              ["id"]: -1,
              ["ph"]: 1 / 0
            };
            for (let c of this["JS"]) c[1]["cw"](a) && c[1]["Zi"] < f["ph"] && (f["id"] = c[1]["id"], f["ph"] = c[1]["Zi"]);
            if (f["id"] < 0) return null;
            {
              const a = this["JS"]["get"](f["id"]),
                c = a["enemy"];
              return {
                ["id"]: f["id"],
                ["x"]: c["x"],
                ["y"]: c["y"],
                ["Bm"]: a["Bm"]
              }
            }
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "WB", {
          ["value"](a) {
            var b = hr,
              c = b[0],
              d = "Lm",
              e = "enemy";
            let f;
            for (let b of this["JS"]) a == b[1]["nm"] && (!f || b[1]["Lm"] > f["Lm"]) && (f = b[1]);
            return f ? {
              ["index"]: f["Lm"],
              ["x"]: f["enemy"]["x"],
              ["y"]: f["enemy"]["y"]
            } : null
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "jB", {
          ["value"]() {
            var a = hr,
              b = a[0],
              c = "instance",
              d = "di",
              e = "$i",
              f = "qi",
              g = "push",
              h = "zB",
              i = "enemy";
            const j = uq["instance"]()["au"],
              k = j["li"];
            if (j["di"]) return j["$i"][k] = !0, j["qi"]["push"](k), this["zB"](!0, k, !0), this["zB"](!1, k, !1), void(j["di"] = !1);
            const l = uq["instance"]()["enemy"]["fh"]["indexOf"](k);
            if (l < 0) return;
            if (void 0 !== j["$i"][k]) return;
            const m = Math["random"]() < uq["instance"]()["enemy"]["gh"][l];
            j["$i"][k] = m, m && (j["qi"]["push"](k), this["zB"](!0, k, !0), this["zB"](!1, k, !1))
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "zB", {
          ["value"](a, b, c) {
            var d = hr,
              e = d[0],
              f = d[11],
              g = "instance",
              h = "enemy",
              i = "yh",
              j = "au",
              k = "Vi";
            const l = uq["instance"]();
            let m;
            c ? (m = 3 * l["map"]["mapIndex"] + l["enemy"]["yh"], l["au"]["Vi"][b] = m, l["enemy"]["yh"] += 1, l["enemy"]["yh"] >= 3 && (l["enemy"]["yh"] = 0), a && (sF["instance"]()["Bn"]("BossTipDialog", !0, m), pC["instance"]()["playSound"]("boss_entrance"))) : m = l["au"]["Vi"][b], this["TB"](m, a)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "MB", {
          ["value"](a) {
            var b = hr,
              c = hu,
              d = b[0],
              e = "JS",
              f = "_B",
              g = "instance",
              h = "map",
              i = "nm",
              j = "event";
            if (this["JS"]["size"] <= 0) return;
            if (this["_B"] += a, this["_B"] < c[176]) return;
            this["_B"] = 0;
            let k = uq["instance"]()["map"]["Le"]["length"],
              l = !1,
              m = !1;
            for (let a of this["JS"]) l && a[1]["nm"] || m && !a[1]["nm"] || k - a[1]["Lm"] <= 5 && (oc["instance"]["event"](sS["Gt"], a[1]["nm"]), a[1]["nm"] ? l = !0 : m = !0);
            !l && uq["instance"]()["map"]["Se"] && oc["instance"]["event"](sS["Ot"], !1, 1)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "sB", {
          ["value"](a, b, c, d) {
            var e = hr,
              f = e[0],
              g = "instance",
              h = "au",
              i = "cB";
            (a ? uq["instance"]()["au"]["Ii"] : uq["instance"]()["au"]["Ti"])["num"] += 1;
            const j = ss["NL"][4];
            let k = s0["instance"]()["WL"](j);
            k["cB"]["x"] = b, k["cB"]["y"] = c, k["cB"]["index"] = d, k["init"](a)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "XE", {
          ["value"](a, b, c, d, e, f, g, h) {
            var i = hr,
              j = i[0],
              k = "instance",
              l = "Point",
              m = "map",
              n = "ye",
              o = "gridHei";
            const p = ss["NL"][6];
            let q = s0["instance"]()["WL"](p);
            q["ow"] = new Laya["Point"](d * uq["instance"]()["map"]["ye"], e * uq["instance"]()["map"]["gridHei"]), q["lw"] = new Laya["Point"](f * uq["instance"]()["map"]["ye"], g * uq["instance"]()["map"]["gridHei"]), q["Lp"] = b, q["hB"] = c, q["Lm"] = h, q["init"](a)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "Kb", {
          ["value"](a, b, c, d, e) {
            var f = hr,
              g = f[1],
              h = f[0],
              i = f[3],
              j = "instance",
              k = "Vy",
              l = "Qy",
              m = "enemy";
            let n = !1;
            const o = uq["instance"]()["map"]["ye"] / 2,
              p = this["CB"](b, c, o);
            for (const j of p) {
              const p = this["JS"]["get"](j);
              p && p["nm"] === a && (this["Vy"]["x"] = b, this["Vy"]["y"] = c, this["Qy"]["x"] = p["enemy"]["x"] + p["enemy"]["width"] / 2, this["Qy"]["y"] = p["enemy"]["y"] + p["enemy"]["height"] / 2, np["bs"](this["Vy"], this["Qy"]) < o && (p["back"](d, e), n = !0))
            }
            n && oc["instance"]["event"](sS["gs"], a)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "$M", {
          ["value"](a, b) {
            var c = hr,
              d = hu,
              e = c[0],
              f = d[61],
              g = "enemy";
            const h = this["CB"](a, b, f);
            for (const d of h) {
              const h = this["JS"]["get"](d);
              if (!h) continue;
              const i = np["bs"]({
                ["x"]: a,
                ["y"]: b
              }, {
                ["x"]: h["enemy"]["x"] + h["enemy"]["width"] / 2,
                ["y"]: h["enemy"]["y"] + h["enemy"]["height"] / 2
              });
              i <= f && h["Xw"](i, a, b)
            }
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "XM", {
          ["value"](a, b) {
            var c = hr,
              d = c[0];
            let e = this["JS"]["get"](a);
            e && (e["Ow"](), vd["instance"]()["applyBuff"](a, hu[12], 0, !1, b))
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
              pp9 = "nm",
              pqa = "map",
              k = "id";
            let l = {
              ["sign"]: a,
              ["nm"]: b,
              ["Vw"]: c,
              ["num"]: d,
              ["eE"]: e,
              ["time"]: f,
              ["map"]: new Map
            };
            this["DA"]["push"](l);
            for (let a of this["JS"]) {
              if (a[1]["nm"] != b) continue;
              const h = vd["instance"]()["applyBuff"](a[1]["id"], c, d, e, f);
              l["map"]["set"](a[1]["id"], h)
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
        Object["defineProperty"](b["prototype"], "KM", {
          ["value"](a, b) {
            var c = hr,
              d = c[0],
              e = c[1];
            let f = this["JS"]["get"](a);
            f ? b ? f["FE"]() : f["KM"]() : console["log"]("boss不存在")
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "gameOver", {
          ["value"]() {
            var a = hr,
              b = a[6],
              c = a[0],
              d = "JS",
              e = "clear",
              f = "length";
            Laya["timer"]["clearAll"](this), this["rp"] = [];
            for (let a of this["JS"]) a[1]["gameOver"]();
            this["JS"]["clear"](), this["LB"]["length"] = 0, this["mB"]["clear"](), this["wB"]["clear"](), this["DA"]["length"] = 0
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        })
      } ["bind"](b)();
      return b
