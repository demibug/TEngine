        } ["bind"](this)["apply"](), q3 = function() {
          var a = hr;
          let b;
          b = class c extends qU {
            constructor() {
              var a = hr,
                b = a[0],
                c = a[7],
                d = a[14],
                e = a[11],
                f = a[9];
              var g;
              g = arguments;
              super(...g), this["vn"] = new Map, this["_n"] = new Map([
                ["MainScene",
                  ["GetStaminaDialog", "SidebarDialog"]
                ],
                ["BattleScene",
                  ["DeckDialog", "PauseDialog", "BossTipDialog"]
                ],
                ["RankScene",
                  ["RankRewardDialog"]
                ],
                ["ShopScene",
                  ["DeletePropsTipDialog", "ReplacePropsTipDialog"]
                ],
                ["GameOverScene",
                  ["ShareLpDialog"]
                ],
                ["WeaponScene",
                  ["NewWeaponDialog", "WeaponIntroDialog", "RecycleWeaponDialog"]
                ]
              ])
            }
            xn(a) {
              var b = hr;
              this["kn"]["forEach"]((c, d) => {
                d !== a && c["parent"] && c["close"]()
              })
            }
            bn(a, b = !1, d, e) {
              var f = hr,
                g = hu,
                h = f[0],
                i = f[1],
                j = f[4],
                k = f[5],
                l = g[114],
                m = "kn",
                n = "xn",
                o = "open",
                p = "getChildByName",
                q = "centerX",
                r = "centerY",
                s = "height",
                t = "stage";
              let u, v;
              u = c["Mn"]["has"](a);
              v = this["kn"]["get"](a);
              v ? (u && this["xn"](a), v["open"](b, d), e && e(v)) : Laya["Scene"]["open"](`scene/${a}.ls`, b, d)["then"](b => {
                let c, d;
                this["kn"]["set"](a, b);
                d = b["getChildByName"]("bg");
                d && (d["centerX"] = 0, d["centerY"] = 0, d["height"] = Laya["stage"]["height"]);
                c = b["getChildByName"]("box");
                c && (c["centerX"] = 0, c["centerY"] = 0, Laya["stage"]["height"] < l && (c["scaleX"] = c["scaleY"] = Laya["stage"]["height"] / l)), u && this["xn"](a), e && e(b)
              })
            }
            Pn(a, b = !0) {
              var c = hr,
                d = "kn",
                e = "get";
              this["An"](a), b && this["kn"]["get"](a) && this["kn"]["get"](a)["close"]()
            }
            En(a) {
              var b = hr;
              return this["kn"]["get"](a)
            }
            Bn(a, b = !0, c) {
              var d = hr,
                e = d[0],
                f = d[2],
                g = d[5],
                h = "set",
                i = "height";
              return new Promise(j => {
                void 0 !== c && this["vn"]["set"](a, c), Laya["Dialog"]["open"](`dialog/${a}.lh`, b, c)["then"](b => {
                  let c;
                  this["Sn"]["set"](a, b);
                  c = b["getChildByName"]("bg");
                  c && (c["centerX"] = 0, c["centerY"] = 0, c["height"] = Laya["stage"]["height"]), j(b)
                })
              })
            }
            Dn(a) {
              var b = hr;
              return this["vn"]["get"](a)
            }
            Cn(a) {
              var b = hr;
              this["vn"]["delete"](a)
            }
            An(a) {
              var b = hr,
                c = b[0];
              let d;
              d = this["_n"]["get"](a);
              if (d)
                for (let a = 0; a < d["length"]; a++) this["Tn"](d[a])
            }
            Rn(a) {
              var b = hr,
                c = b[3],
                d = b[4],
                e = "Tween",
                f = "to";
              let g;
              g = this["kn"]["get"]("BattleScene");
              g && Laya["Tween"]["create"](g)["duration"](a)["to"]("x", 0)["to"]("y", 0)["delay"](hu[81])["interp"](Laya["Tween"]["shake"], 3)["then"](() => {
                g["x"] = 0, g["y"] = 0
              })
            }
            Un(a) {
              var b = hr,
                c = "kn";
              let d;
              d = this["kn"]["get"](a);
              d && (d["destroy"](!0), this["kn"]["delete"](a))
            }
            Tn(a) {
              var b = hr,
                c = "Sn",
                d = "get";
              this["Sn"]["get"](a) && (this["Sn"]["get"](a)["close"](), this["Cn"](a))
            }
          };
          ! function() {
            "use strict";
            var a = hr;
            Object["defineProperty"](b["prototype"], "init", {
              ["value"]() {
                var a = hr,
                  b = a[0];
                this["kn"] = new Map, this["Sn"] = new Map, this["vn"] = new Map
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            })
          } ["bind"](b)();
          return b
        } ["bind"](this)["apply"]();
        break;
      case 8:
        q3["Mn"] = new Set(["BattleScene", "MainScene", "GameOverScene", "MatchScene"]);
