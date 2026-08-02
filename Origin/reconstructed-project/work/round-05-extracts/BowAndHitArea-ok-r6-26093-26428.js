      px = Laya["Point"], ok = function() {
        var a = hr;
        let b;
        b = class extends td {
          constructor() {
            var a = hr,
              b = a[0],
              c = a[4];
            var d;
            d = arguments;
            super(...d), this["Q_"] = "bow", this["nx"] = 0, this["v_"] = hu[123], this["config"] = {
              ["type"]: rd,
              ["XS"]: {
                ["tS"]: "弓箭小兵箭矢",
                ["RS"]: "resources/img/weapon/arrow_0.png"
              },
              ["sS"]: this["_p"],
              ["iS"]: this,
              ["fS"]: 1.75
            }
          }
          A_(a) {
            var b = hr,
              c = b[0];
            super["A_"](a), this["T_"]["setInitPlaybackRate"](1.25)
          }
          gameOver() {
            var a = hr,
              b = a[6],
              c = "T_";
            Laya["Tween"]["killAll"](this["T_"]), this["T_"]["rotation"] = 0, super["gameOver"]()
          }
        };
        ! function() {
          "use strict";
          var a = hr,
            c = a[0],
            d = "defineProperty",
            oQg = "value",
            oQh = "enumerable",
            oQi = "configurable",
            oQj = "writable";
          Object["defineProperty"](b["prototype"], "J_", {
            ["value"]() {
              var a = hr,
                b = a[0],
                c = a[6],
                d = a[2];
              Laya["Tween"]["create"](this["T_"])["to"]("rotation", 0)["duration"](hu[252])["ease"](Laya["Ease"]["linearInOut"])
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](b["prototype"], "ox", {
            ["value"](a = !1) {
              var b = hr,
                c = b[0],
                oQq = "id",
                oQr = "Bm",
                d = "lx";
              let e;
              e = {
                ["id"]: -1,
                ["x"]: 0,
                ["y"]: 0,
                ["Bm"]: 1 / 0
              };
              for (let f = 0; f < this["lx"]["length"]; f++) {
                let g;
                g = this["lx"][f];
                if (a) {
                  let a;
                  a = vi["instance"]()["JS"]["get"](g["id"]);
                  if (!a || !a["rm"]) continue
                }
                g["Bm"] < e["Bm"] && (e = g)
              }
              return e
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](b["prototype"], "attack", {
            ["value"]() {
              var a = hr,
                b = hu,
                c = a[0],
                d = a[3],
                e = a[2],
                f = a[4],
                g = b[252],
                h = "id",
                i = "TEMP",
                j = "Oc",
                k = "T_",
                l = "Event",
                oQD = "rotation";
              let m, n, o, p;
              o = this["ox"]();
              if (this["ux"] = o["id"], o["id"] < 0) return;
              Laya["Point"]["TEMP"]["setTo"](this["Oc"]["x"] + this["Oc"]["width"] / 2, this["Oc"]["y"] + this["Oc"]["height"] / 2);
              m = this["hx"](px["TEMP"], o["id"], b[111]);
              this["T_"]["on"](Laya["Event"]["STOPPED"], this, () => {
                this["T_"]["offAll"](Laya["Event"]["STOPPED"]), this["yx"]()
              }), this["T_"]["play"]("attack", !1, !0, 0, g);
              p = this["T_"]["rotation"], n = np["Cs"](p, m);
              Laya["Tween"]["to"](this["T_"], {
                ["rotation"]: p + n
              }, g, Laya["Ease"]["linearInOut"])
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](b["prototype"], "yx", {
            ["value"]() {
              var a = hr,
                b = hu,
                c = a[0],
                d = a[4],
                e = a[2],
                f = "instance",
                g = "ux",
                h = "config";
              let i;
              pC["instance"]()["playSound"]("bow_attack");
              i = vi["instance"]()["JS"]["get"](this["ux"]);
              i && i["rm"] || (this["ux"] = this["ox"](!0)["id"]), this["config"]["yS"] = oF["produce"](b[81], {
                ["Lk"]: this["ux"]
              }), this["config"]["gS"] = on["create"](b[111], !0)["Ak"](this["ux"]), this["T_"]["play"]("attack", !1, !0, b[252], b[123]), uq["instance"]()["Cy"](this["Oc"], !0), this["config"]["sS"] = this["_p"], vA["instance"]()["gx"](this["config"], Laya["Point"]["TEMP"])["LS"]()
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](b["prototype"], "qS", {
            ["value"](a) {
              var b = hr,
                c = b[0],
                d = "instance",
                e = "enemy",
                f = "map";
              let g;
              g = vi["instance"]()["JS"]["get"](a);
              return g ? px["create"]()["setTo"](g["enemy"]["x"] + uq["instance"]()["map"]["ye"] / 2, g["enemy"]["y"] + uq["instance"]()["map"]["gridHei"] / 2) : null
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](b["prototype"], "hx", {
            ["value"](a, b, c) {
              var d = hr,
                e = d[0],
                f = "recover";
              let g, h, i;
              g = this["qS"](b);
              if (!g) return null;
              h = px["create"]();
              h["setTo"](a["x"] + (g["x"] - a["x"]) / 2, a["y"] + (g["y"] - a["y"]) / 2 - c);
              i = np["Rs"](a, h, g, 0);
              return h["recover"](), g["recover"](), i + hu[88]
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          })
        } ["bind"](b)();
        return b
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
