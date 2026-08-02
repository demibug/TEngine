        nx = pV, tn = function() {
          var a = hr,
            b = "get",
            c = "Ba";
          let d;
          d = class e extends qU {
            constructor() {
              var a = hr,
                b = a[0];
              var c;
              c = arguments;
              super(...c), this["xa"] = null, this["path"] = "https://api01.mihuangame.com/api/v2/", this["ba"] = !1, this["authentication"] = "", this["Ma"] = 0, this["userType"] = 0, this["Pa"] = 3, this["channelAppId"] = 0
            }
            init(a) {
              this["channelAppId"] = a
            }
            request(a, d, f, g = "get", h = e["Ba"]) {
              var i = hr,
                j = i[2],
                k = "authentication",
                l = "once",
                m = "Event",
                n = "success",
                o = "fail";
              let p, q;
              p = new Laya["HttpRequest"];
              q = ["Content-Type", "application/json", "authentication", this["authentication"]];
              p["http"]["timeout"] = h, p["send"](this["url"] + a, d, g, "json", q), p["once"](Laya["Event"]["COMPLETE"], this, () => {
                let a;
                a = p["data"];
                f["success"] && f["success"](a)
              }), p["once"](Laya["Event"]["ERROR"], this, a => {
                f["fail"] && f["fail"](a)
              })
            }
            Da(a, d, f = "get", g = e["Ba"]) {
              var h = hr;
              return new Promise((b, c) => {
                this["request"](a, d, function() {
                  var a = hr,
                    neM = "success",
                    neN = "fail";
                  let d;
                  d = {
                    ["success"]: 0,
                    ["fail"]: 0
                  };
                  d["success"] = a => b(a);
                  d["fail"] = a => c(a);
                  return d
                } ["apply"](), f, g)
              })
            }
            static Ia(a, b = e["Ba"]) {
              var d = hr,
                f = d[2],
                g = "timer";
              return new Promise(c => {
                let h, i, j;
                h = !1;
                j = a => {
                  h || (h = !0, Laya["timer"]["clear"](e, i), c(a))
                }, i = () => j(!1);
                Laya["timer"]["once"](b, e, i), a["then"](() => j(!0), () => j(!1))
              })
            }
            Ca(a, b, d, f = e["Ba"]) {
              var g = hr,
                h = g[0],
                i = g[2],
                j = "success",
                k = "fail";
              return a ? this["Da"]("sys/user/login", b, "post", f)["then"](a => {
                let b;
                b = a;
                return this["Ta"](b), (null == d ? void 0 : d["success"]) && d["success"](b), b
              }, a => (console["warn"]("[Server] login failed or timeout", a), (null == d ? void 0 : d["fail"]) && d["fail"](a), null)) : ((null == d ? void 0 : d["fail"]) && d["fail"]("login code is empty"), Promise["resolve"](null))
            }
            Ta(a) {
              var b = hr,
                c = b[4],
                d = b[0],
                e = b[6],
                f = "data",
                g = "authentication",
                h = "number",
                i = "userId",
                j = "userType",
                k = "attach",
                l = "province",
                m = "instance";
              let n, o, p;
              console["log"]("applyLoginResponse", a);
              o = a && a["data"] && a["data"]["authentication"];
              o && (this["authentication"] = o);
              n = a && a["data"] && "number" == typeof a["data"]["userId"] ? a["data"]["userId"] : 0;
              this["Ma"] = n, this["userType"] = a && a["data"] && "number" == typeof a["data"]["userType"] ? a["data"]["userType"] : 0, this["xa"] = a && a["data"] ? a["data"]["userData"] : null;
              p = "";
              if (a && a["data"] && a["data"]["attach"]) {
                let b;
                b = a["data"]["attach"]["province"];
                "string" == typeof b && (p = b)
              }
              uq["instance"]()["player"]["province"] = p["length"] > 0 ? p : "未知", n > 0 && oc["instance"]["event"](sS["xs"], n)
            }
            Ra() {
              var a = hr,
                b = a[0],
                c = "warn",
                d = "instance";
              let e;
              if (this["Ma"] <= 0) return void console["warn"]("[Server] 未登录，跳过云端存档同步");
              e = rj["parseCloudSaveRaw"](this["xa"]);
              if (!e) return void console["warn"]("[Server] 登录未返回云端存档，使用本地数据");
              uq["instance"]()["player"]["resolveCloudOnLoad"](e) ? uq["instance"]()["Ua"]() : this["Fa"](!0)
            }
            Oa(a) {
              var b = hr;
              this["request"]("zyyad/game/start", null, a, "get")
            }
            Ya(a, b) {
              var c = hr,
                d = c[5],
                e = c[3];
              let f;
              f = uq["instance"]()["player"]["curStar"];
              this["request"]("zyyad/game/end?star=" + f + "&win=" + (a ? 1 : 0), function() {
                var a = hr,
                  nfh = "skin";
                let b;
                b = {
                  ["skin"]: 0
                };
                b["skin"] = 1;
                return b
              } ["apply"](), b || {}, "get")
            }
            Xa(a) {
              var b = hr;
              this["request"]("zyyad/game/country/list?type=" + this["Pa"], null, a, "get")
            }
            Ga(a) {
              var b = hr;
              this["request"]("zyyad/game/province/detail/list?type=" + this["Pa"], null, a, "get")
            }
            Ha(a) {
              this["Xa"](a)
            }
            getTime(a) {
              var b = hr;
              this["request"]("sys/server/time", null, a)
            }
            Wa(a) {
              var b = hr;
              this["getTime"](function() {
                var b = hr,
                  c = b[4],
                  d = b[6],
                  nfp = "success",
                  e = "data";
                let f;
                f = {
                  ["success"]: 0
                };
                f["success"] = f => {
                  let g, h;
                  g = f, h = g && "number" == typeof g["data"] ? g["data"] : 0;
                  np["Gs"](h, uq["instance"]()["player"]["isGetLastRankReward"]) >= 1 && this["request"]("bestRank", null, a)
                };
                return f
              } ["bind"](this)["apply"]())
            }
            Fa(a = !1) {
              var b = hr,
                c = b[5],
                d = "LocalStorage",
                f = "ja";
              let g;
              if (this["Ma"] <= 0) return;
              if (!a) {
                let a;
                a = Number(Laya["LocalStorage"]["getItem"](e["ja"]) || "0");
                if (a++, Laya["LocalStorage"]["setItem"](e["ja"], String(a)), 1 !== a && a % 5 != 0) return void console["log"]("[Server] 云端存档跳过，当前局数：" + a)
              }
              g = uq["instance"]()["player"]["cloudPush"]();
              this["request"]("sys/user/data", g, function() {
                var a = hr,
                  b = a[15],
                  nfx = "success",
                  nfy = "fail";
                let c;
                c = {
                  ["success"]: 0,
                  ["fail"]: 0
                };
                c["success"] = () => {
                  console["log"]("[Server] 用户存档上传成功")
                };
                c["fail"] = c => {
                  console["warn"]("[Server] 用户存档上传失败", c)
                };
                return c
              } ["apply"](), "post")
            }
            za(a) {
              var b = hr;
              this["request"]("sys/user/info", a, function() {
                var a = hr,
                  b = a[6],
                  nfC = "success",
                  nfD = "fail",
                  c = "log";
                let d;
                d = {
                  ["success"]: 0,
                  ["fail"]: 0
                };
                d["success"] = a => {
                  console["log"]("上传用户数据成功", a)
                };
                d["fail"] = a => {
                  console["log"]("上传用户数据失败", a)
                };
                return d
              } ["apply"](), "post")
            }
            track(a, b) {
              var c = hr;
              a && 0 !== a["length"] && this["request"]("sys/oa/point/add/new", a, function() {
                var a = hr,
                  nfH = "success",
                  nfI = "fail";
                let c;
                c = {
                  ["success"]: 0,
                  ["fail"]: 0
                };
                c["success"] = () => {
                  (null == b ? void 0 : b["success"]) && b["success"]()
                };
                c["fail"] = a => {
                  (null == b ? void 0 : b["fail"]) && b["fail"](a)
                };
                return c
              } ["apply"](), "post")
            }
            Na(a) {
              var b = hr;
              this["request"]("sys/oa/errorUpload/add", a, function() {
                var a = hr,
                  b = a[6],
                  nfM = "success",
                  nfN = "fail",
                  c = "log";
                let d;
                d = {
                  ["success"]: 0,
                  ["fail"]: 0
                };
                d["success"] = () => {
                  console["log"]("上传错误日志成功")
                };
                d["fail"] = a => {
                  console["log"]("上传错误日志失败", a)
                };
                return d
              } ["apply"](), "post")
            }
          };
          ! function() {
            "use strict";
            var a = hr,
              b = a[1],
              c = a[0],
              e = "defineProperty",
              nfT = "enumerable",
              nfU = "configurable",
              nfV = "value",
              nfW = "writable";
            Object["defineProperty"](d["prototype"], "url", {
              ["get"]() {
                var a = hr;
                return this["ba"] ? "https://debug.mihuangame.com/api/v2/" : this["path"]
              },
              ["enumerable"]: false,
              ["configurable"]: true
            });
            Object["defineProperty"](d["prototype"], "Aa", {
              ["value"]() {
                return this["Ma"]
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            });
            Object["defineProperty"](d["prototype"], "Ea", {
              ["value"]() {
                return this["userType"]
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            })
          } ["bind"](d)();
          return d
        } ["bind"](this)["apply"]();
        break;
      case 7:
        qK = tn, ry = function() {
          let a;
          a = class extends qU {};
          ! function() {
            "use strict";
            var b = hr,
              c = b[0],
              d = "defineProperty",
              nf1 = "value",
              nf2 = "enumerable",
              nf3 = "configurable",
              nf4 = "writable";
            Object["defineProperty"](a["prototype"], "init", {
              ["value"]() {
                var a = hr,
                  b = "Va";
                this["Va"] = uq["instance"]()["rank"]["Va"]
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            });
            Object["defineProperty"](a["prototype"], "Qa", {
              ["value"](a) {
                var b = hr,
                  c = hu,
                  d = b[1],
                  e = c[113],
                  f = c[45],
                  g = c[54],
                  h = c[53],
                  nf9 = "rank",
                  nga = "level",
                  i = "max",
                  j = "Va",
                  k = "get";
                let l, m, n, o, p;
                if (a <= e) {
                  let d, e;
                  e = Math["floor"](a / 5), d = a - 5 * e;
                  if (0 === d) {
                    if (0 === a) return {
                      ["rank"]: 0,
                      ["level"]: 1
                    };
                    e -= 1, d = 5
                  }
                  return {
                    ["rank"]: Math["min"](c[26], Math["max"](0, e)),
                    ["level"]: d
                  }
                }
                o = a - e, m = this["Va"]["get"](f)["level"], p = this["Va"]["get"](g)["level"], l = this["Va"]["get"](h)["level"], n = Math["max"](1, o);
                return n <= m ? {
                  ["rank"]: f,
                  ["level"]: n
                } : n <= p ? {
                  ["rank"]: g,
                  ["level"]: n
                } : n <= l ? {
                  ["rank"]: h,
                  ["level"]: n
                } : {
                  ["rank"]: c[38],
                  ["level"]: n
                }
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            });
            Object["defineProperty"](a["prototype"], "Za", {
              ["value"](a, b) {
                var c = hr,
                  d = hu,
                  e = "Va",
                  f = "max";
                if (!this["Va"] || 0 === this["Va"]["size"]) return 5 * Math["max"](0, a) + b;
                if (a <= d[26]) return 5 * a + b;
                return d[113] + Math["max"](1, b)
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            })
          } ["bind"](a)();
          return a
        } ["bind"](this)["apply"](), sI = function() {
          var a = hr;
          let b;
          b = class {
            constructor() {
              var a = hr,
                b = a[0];
              this["channelAppId"] = 0, this["Ka"] = !1, this["Ja"] = !1, this["tn"] = !1
            }
          };
          ! function() {
            var a = hr,
              c = hu,
              d = a[9],
              e = a[0],
              f = "defineProperty",
              ngn = "value",
              ngo = "enumerable",
              ngp = "configurable",
              ngq = "writable";
            w1_bY: for (let g of mg) {
              switch (g) {
                case 0:
                  Object["defineProperty"](b["prototype"], "getUserInfo", {
                    ["value"]() {
                      return p2(this, null, function*() {
                        return null
                      })
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case 1:
                  Object["defineProperty"](b["prototype"], "createImage", {
                    ["value"]() {
                      return null
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case 2:
                  Object["defineProperty"](b["prototype"], "un", {
                    ["value"]() {},
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case 3:
                  Object["defineProperty"](b["prototype"], "rn", {
                    ["value"]() {},
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case 4:
                  Object["defineProperty"](b["prototype"], "gn", {
                    ["value"]() {},
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case 5:
                  Object["defineProperty"](b["prototype"], "pn", {
                    ["value"]() {
                      return !1
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case 6:
                  Object["defineProperty"](b["prototype"], "mn", {
                    ["value"](a) {},
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case 7:
                  Object["defineProperty"](b["prototype"], "setClipboardData", {
                    ["value"](a) {
                      var b = hr;
                      console["log"]("[Plat] setClipboardData", a)
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case 8:
                  Object["defineProperty"](b["prototype"], "openAwemeUserProfile", {
                    ["value"](a, b) {},
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case 9:
                  Object["defineProperty"](b["prototype"], "nn", {
                    ["value"](a, b) {
                      var c = hr;
                      a && a(), console["log"]("播放激励视频成功")
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case 10:
                  Object["defineProperty"](b["prototype"], "en", {
                    ["value"]() {
                      return p2(this, null, function*() {})
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[7]:
                  Object["defineProperty"](b["prototype"], "init", {
                    ["value"]() {
                      var a = hr,
                        b = "Stat",
                        c = "_statUI";
                      Laya["Stat"]["_statUI"] && Laya["Stat"]["_statUI"]["_sp"]["scale"](2, 2)
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[9]:
                  "use strict";
                  break;
                case c[3]:
                  Object["defineProperty"](b["prototype"], "an", {
                    ["value"](a) {
                      return p2(this, null, function*() {
                        return !1
                      })
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[14]:
                  Object["defineProperty"](b["prototype"], "cn", {
                    ["value"](a, b) {
                      a && a()
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[12]:
                  Object["defineProperty"](b["prototype"], "yn", {
                    ["value"]() {},
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[11]:
                  Object["defineProperty"](b["prototype"], "ln", {
                    ["value"]() {},
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[13]:
                  Object["defineProperty"](b["prototype"], "share", {
                    ["value"](a, b) {
                      var c = hr;
                      a && a(), console["log"]("分享成功")
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[5]:
                  Object["defineProperty"](b["prototype"], "dn", {
                    ["value"]() {},
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[4]:
                  Object["defineProperty"](b["prototype"], "hn", {
                    ["value"](a) {
                      return p2(this, null, function*() {
                        a && a(1, 1)
                      })
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[1]:
                  Object["defineProperty"](b["prototype"], "Ln", {
                    ["value"](a, b) {},
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[10]:
                  Object["defineProperty"](b["prototype"], "wn", {
                    ["value"](a) {},
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
        break;
      case 9:
        sy = qB, s1 = function() {
          let a;
          a = class {
            constructor() {
              var a = hr,
                b = hu,
                c = a[0],
                d = "zo",
                nhn = "qo";
              this["zo"] = 10, this["No"] = {
                [b[115]]: {
                  ["qo"]: 1
                },
                [b[116]]: {
                  ["qo"]: this["zo"]
                },
                [b[86]]: {
                  ["qo"]: 1
                }
              }
            }
          };
          ! function() {
            "use strict";
            var b = hr;
            Object["defineProperty"](a["prototype"], "$o", {
              ["value"](a) {
                var b = hr,
                  c = "No";
                var d;
                return null != (d = this["No"][a]) ? d : this["No"][0]
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            })
          } ["bind"](a)();
          return a
        } ["bind"](this)["apply"](), r7 = function() {
          var a = hr;
          let b;
          b = class extends qU {
            constructor() {
              var a = hr;
              var b;
              b = arguments;
              super(...b), this["Vo"] = new s1, this["userType"] = 0
            }
          };
          ! function() {
            var a = hr,
              c = a[0],
              d = "defineProperty",
              nhv = "value",
              nhw = "enumerable",
              nhx = "configurable",
              nhy = "writable";
            for (let e of mh) {
              if (-1 == e) {} else if (0 == e) {
                Object["defineProperty"](b["prototype"], "init", {
                  ["value"]() {
                    var a = hr,
                      b = a[0];
                    this["Qo"](), oc["instance"]["on"](sS["xs"], this, this["Zo"])
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (1 == e) {
                Object["defineProperty"](b["prototype"], "Qo", {
                  ["value"]() {
                    var a = hr,
                      b = a[6],
                      c = a[0];
                    let d;
                    d = qK["instance"]()["Ea"]();
                    this["userType"] = this["Ko"](d)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (2 == e) {
                Object["defineProperty"](b["prototype"], "sl", {
                  ["value"]() {
                    var a = hr,
                      b = a[0];
                    return this["$o"]()["qo"]
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (3 == e) {
                Object["defineProperty"](b["prototype"], "tl", {
                  ["value"]() {
                    return hu[86] === this["userType"]
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (4 == e) {
                Object["defineProperty"](b["prototype"], "$o", {
                  ["value"]() {
                    var a = hr,
                      b = a[0];
                    return this["Vo"]["$o"](this["userType"])
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (5 == e) {
                "use strict";
                continue
              } else if (6 == e) {
                Object["defineProperty"](b["prototype"], "Ea", {
                  ["value"]() {
                    return this["userType"]
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (7 == e) {
                Object["defineProperty"](b["prototype"], "Zo", {
                  ["value"](a) {
                    this["Qo"]()
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (8 == e) {
                Object["defineProperty"](b["prototype"], "Ko", {
                  ["value"](a) {
                    return 1 === a || hu[86] === a ? a : 0
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (9 == e) {
                Object["defineProperty"](b["prototype"], "Jo", {
                  ["value"]() {
                    return 1 === this["userType"]
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              }
            }
          } ["bind"](b)();
          return b
