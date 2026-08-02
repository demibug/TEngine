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
