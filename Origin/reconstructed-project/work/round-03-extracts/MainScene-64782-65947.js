        } = Laya), nA = function() {
          var a = hr;
          if (b7) mc["pop"]();
          let b;
          b = class extends q4 {
            constructor() {
              var a = hr,
                b = a[0],
                c = a[3],
                rSj = "rotation";
              var d;
              d = arguments;
              super(...d), this["MQ"] = ["你很会打吗？"], this["PQ"] = hu[101], this["AQ"] = 0, this["EQ"] = 0, this["BQ"] = !1, this["DQ"] = {
                ["x"]: 0,
                ["y"]: 0,
                ["rotation"]: 0
              }, this["IQ"] = {
                ["x"]: 0,
                ["y"]: 0,
                ["rotation"]: 0
              }, this["CQ"] = 0, this["TQ"] = !1, this["RQ"] = 0, this["UQ"] = 0, this["FQ"] = null, this["OQ"] = null, this["YQ"] = [], this["XQ"] = !1, this["GQ"] = 0, this["HQ"] = 0, this["WQ"] = ["下次再见！", "没货了", "没事常联系"], this["jQ"] = null, this["zQ"] = null, this["NQ"] = 0
            }
          };
          ! function() {
            var a = hr,
              c = hu,
              d = a[0],
              e = a[6],
              f = "defineProperty",
              rSo = "value",
              rSp = "enumerable",
              rSq = "configurable",
              rSr = "writable";
            w1_er: for (let g of m6) {
              switch (g) {
                case 0:
                  Object["defineProperty"](b["prototype"], "HZ", {
                    ["value"](a) {
                      var b = hr,
                        c = hu,
                        d = b[0],
                        e = "jZ";
                      let f;
                      if (!this["jZ"](a)) return;
                      f = this["followBtnImg"];
                      Laya["Tween"]["create"](f)["to"]("y", c[37])["duration"](c[325])["then"](() => {
                        this["jZ"](a) && (this["XZ"](), this["zZ"](a))
                      }, this)
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case 1:
                  Object["defineProperty"](b["prototype"], "hZ", {
                    ["value"]() {
                      var a = hr,
                        b = a[0],
                        c = "instance";
                      !(uq["instance"]()["player"]["followDouyinState"] >= 1) && r2["instance"]()["sc"]() || (this["followBtn"]["visible"] = !1, this["pZ"]())
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case 2:
                  Object["defineProperty"](b["prototype"], "$Q", {
                    ["value"]() {
                      var a = hr;
                      sF["instance"]()["bn"]("AvatarSettingScene")
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case 3:
                  Object["defineProperty"](b["prototype"], "cZ", {
                    ["value"]() {
                      var a = hr,
                        b = a[3],
                        c = a[4],
                        d = a[0],
                        e = a[6],
                        f = "dySidebarBtn";
                      this["dySidebarBtn"]["visible"] && (Laya["Tween"]["create"](this["dySidebarBtnLight"])["to"]("alpha", .7)["duration"](hu[123])["repeat"](-1, !0), this["GQ"] = qs["instance"]()["pd"](this["dySidebarBtn"], new nL(["resources/img/mainUI/sidebar/star.png"])))
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case 4:
                  Object["defineProperty"](b["prototype"], "fZ", {
                    ["value"]() {
                      var a = hr,
                        b = a[0],
                        c = a[5],
                        d = "bag",
                        e = "Tween",
                        f = "killAll",
                        g = "getChildByName",
                        h = "TQ",
                        i = "rotation";
                      let j, k;
                      if (this["CQ"]++, !this["bag"] || this["bag"]["destroyed"]) return;
                      Laya["Tween"]["killAll"](this["bag"]), this["bag"]["scale"](1, 1);
                      k = this["bag"]["getChildByName"]("weapon1"), j = this["bag"]["getChildByName"]("weapon2");
                      k && (Laya["Tween"]["killAll"](k), this["TQ"] && (k["rotation"] = this["RQ"])), j && (Laya["Tween"]["killAll"](j), this["TQ"] && (j["rotation"] = this["UQ"]))
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case 5:
                  Object["defineProperty"](b["prototype"], "mZ", {
                    ["value"]() {
                      var a = hr,
                        b = a[3],
                        c = a[0],
                        d = a[5],
                        e = a[6],
                        f = "playBtn",
                        g = "destroyed",
                        h = "max",
                        i = "jQ",
                        j = "Sprite",
                        k = "mouseEnabled",
                        l = "size",
                        m = "graphics",
                        n = "drawRect",
                        o = "#ffffff",
                        p = "addChild",
                        q = "zQ",
                        r = "clear";
                      let s, t, u, v;
                      if (!this["playBtn"] || this["playBtn"]["destroyed"]) return !1;
                      s = Math["max"](1, this["playBtn"]["width"]), v = Math["max"](1, this["playBtn"]["height"] - 10);
                      if (!this["jQ"] || this["jQ"]["destroyed"]) {
                        let d;
                        d = new Laya["Sprite"];
                        d["mouseEnabled"] = !1, d["size"](s, v), d["graphics"]["drawRect"](0, 0, s, v, "#ffffff"), d["alpha"] = nA["wZ"], d["visible"] = !1, this["playBtn"]["addChild"](d), this["jQ"] = d
                      }
                      if (!this["zQ"] || this["zQ"]["destroyed"]) {
                        let b;
                        b = new Laya["Sprite"];
                        b["mouseEnabled"] = !1, this["zQ"] = b, this["jQ"]["mask"] = b, this["jQ"]["addChild"](b)
                      }
                      u = Math["max"](8, s * nA["vZ"]), t = 2 * v;
                      return this["zQ"]["graphics"]["clear"](), this["zQ"]["graphics"]["drawRect"](.5 * -u, .5 * -t, u, t, "#ffffff"), this["zQ"]["pivot"](0, 0), this["zQ"]["rotation"] = nA["_Z"], this["zQ"]["y"] = .5 * v, this["jQ"]["size"](s, v), this["jQ"]["graphics"]["clear"](), this["jQ"]["graphics"]["drawRect"](0, 0, s, v, "#ffffff"), !0
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case 6:
                  Object["defineProperty"](b["prototype"], "s$", {
                    ["value"]() {
                      var a = hr,
                        b = a[2],
                        c = a[6];
                      this["goldTxt"]["text"] = uq["instance"]()["player"]["gold"]["toString"]()
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case 7:
                  Object["defineProperty"](b["prototype"], "tZ", {
                    ["value"]() {
                      var a = hr;
                      r2["instance"]()["openAwemeUserProfile"](() => {
                        this["hZ"]()
                      })
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case 8:
                  Object["defineProperty"](b["prototype"], "onOpened", {
                    ["value"](a) {
                      var b = hr,
                        c = b[0],
                        d = b[7],
                        e = b[10],
                        f = "instance",
                        g = "visible",
                        h = "player",
                        i = "weaponFree";
                      let j;
                      tg["instance"]()["Yl"](), this["KQ"](), this["weaponBtn"]["visible"] = !0;
                      j = uq["instance"]()["player"];
                      qx["instance"]()["Vc"]() && (j["weaponFree"] || (qx["instance"]()["Kc"](), j["weaponFree"] = !0)), pC["instance"]()["playMusic"]("bg_mainScene"), 2 != uq["instance"]()["player"]["sidebarState"] && r2["instance"]()["tc"]() || (this["dySidebarBtn"]["visible"] = !1), this["hZ"](), this["nZ"](), this["rZ"](), this["oZ"](), this["lZ"](), this["cZ"](), this["uZ"]()
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case 9:
                  Object["defineProperty"](b["prototype"], "KQ", {
                    ["value"]() {
                      var a = hr,
                        b = "avatarImg";
                      this["avatarImg"]["texture"] = null, tR["instance"]()["_u"](this["avatarImg"])
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case 10:
                  Object["defineProperty"](b["prototype"], "Gj", {
                    ["value"]() {
                      var a = hr,
                        b = a[5],
                        c = "instance",
                        d = "stamina",
                        e = "staminaTxt",
                        f = "color";
                      let g;
                      g = uq["instance"]()["player"]["stamina"];
                      this["staminaTxt"]["text"] = g["toString"](), g < uq["instance"]()["stamina"]["qp"] ? this["staminaTxt"]["color"] = "#e95b55" : this["staminaTxt"]["color"] = g > 5 && g <= 10 ? "#2797d6" : "#f7de76"
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[7]:
                  Object["defineProperty"](b["prototype"], "uZ", {
                    ["value"]() {
                      var a = hr,
                        b = a[0];
                      this["followBtn"]["visible"] && this["followBtnImg"] && (this["pZ"](), this["XZ"](), this["GZ"](this["HQ"]))
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[9]:
                  Object["defineProperty"](b["prototype"], "eZ", {
                    ["value"]() {},
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[3]:
                  Object["defineProperty"](b["prototype"], "FZ", {
                    ["value"](a, b) {
                      var c = hr,
                        d = hu,
                        e = c[0],
                        f = c[3],
                        g = c[5],
                        h = c[8],
                        i = c[6],
                        j = c[4],
                        k = c[2],
                        l = d[17],
                        m = "EQ",
                        n = "Tween",
                        o = "killAll",
                        p = "swordLight",
                        q = "knifeLight",
                        r = "playBtn",
                        s = "colLight",
                        t = "destroyed",
                        u = "alpha",
                        v = "create",
                        w = "to",
                        x = "duration",
                        y = "ease",
                        z = "Ease",
                        A = "quadOut",
                        B = "quadIn",
                        C = "color",
                        D = "scaleX",
                        E = "scaleY",
                        F = "timer",
                        G = "once";
                      a === this["EQ"] && (this["SZ"](), Laya["Tween"]["killAll"](this["swordLight"]), Laya["Tween"]["killAll"](this["knifeLight"]), Laya["Tween"]["killAll"](this["playBtn"]), this["colLight"] && !this["colLight"]["destroyed"] && Laya["Tween"]["killAll"](this["colLight"]), this["swordLight"]["alpha"] = 0, this["knifeLight"]["alpha"] = 0, this["colLight"] && !this["colLight"]["destroyed"] && (this["colLight"]["alpha"] = 0, this["colLight"]["visible"] = !0, Laya["Tween"]["create"](this["colLight"])["to"]("alpha", .75)["duration"](d[32])["ease"](Laya["Ease"]["quadOut"])["chain"]()["to"]("alpha", 0)["duration"](d[78])["ease"](Laya["Ease"]["quadIn"])), Laya["Tween"]["create"](this["swordLight"])["to"]("alpha", .72)["duration"](l)["ease"](Laya["Ease"]["quadOut"]), Laya["Tween"]["create"](this["knifeLight"])["to"]("alpha", .78)["delay"](d[30])["duration"](l)["ease"](Laya["Ease"]["quadOut"]), Laya["Tween"]["create"](this["playBtn"])["to"]("color", nA["OZ"])["to"]("scaleX", 1.028)["to"]("scaleY", 1.028)["duration"](d[88])["ease"](Laya["Ease"]["quadOut"]), Laya["timer"]["once"](d[81], this, () => {
                        a === this["EQ"] && (Laya["Tween"]["create"](this["swordLight"])["to"]("alpha", 0)["duration"](d[171])["ease"](Laya["Ease"]["quadIn"]), Laya["Tween"]["create"](this["knifeLight"])["to"]("alpha", 0)["duration"](d[261])["ease"](Laya["Ease"]["quadIn"]), Laya["Tween"]["create"](this["playBtn"])["to"]("color", nA["IZ"])["to"]("scaleX", 1)["to"]("scaleY", 1)["duration"](d[132])["ease"](Laya["Ease"]["sineOut"]))
                      }), Laya["timer"]["once"](d[326], this, () => {
                        a === this["EQ"] && b()
                      }))
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[14]:
                  Object["defineProperty"](b["prototype"], "LZ", {
                    ["value"]() {
                      var a = hr,
                        b = a[0],
                        c = a[3],
                        d = a[11],
                        e = "instance",
                        f = "Point",
                        g = "TEMP",
                        h = "staminaImg",
                        i = "width",
                        j = "height",
                        k = "localToGlobal",
                        l = "globalToLocal",
                        m = "btnStaminaImg";
                      return new Promise(c => {
                        let d, n, o;
                        o = uq["instance"]()["stamina"]["qp"];
                        Laya["Point"]["TEMP"]["x"] = this["staminaImg"]["width"] / 2, Laya["Point"]["TEMP"]["y"] = this["staminaImg"]["height"] / 2, this["staminaImg"]["localToGlobal"](Laya["Point"]["TEMP"]), this["globalToLocal"](Laya["Point"]["TEMP"]);
                        d = new Laya["Point"](Laya["Point"]["TEMP"]["x"], Laya["Point"]["TEMP"]["y"]);
                        Laya["Point"]["TEMP"]["x"] = this["btnStaminaImg"]["width"] / 2, Laya["Point"]["TEMP"]["y"] = this["btnStaminaImg"]["height"] / 2, this["btnStaminaImg"]["localToGlobal"](Laya["Point"]["TEMP"]), this["globalToLocal"](Laya["Point"]["TEMP"]);
                        n = new Laya["Point"](Laya["Point"]["TEMP"]["x"], Laya["Point"]["TEMP"]["y"]);
                        qs["instance"]()["Md"](this, "resources/img/mainUI/stamina/stamina.png", this["staminaImg"]["width"] / 2, this["staminaImg"]["height"] / 2, d, n, this["btnStaminaImg"]["width"], this["btnStaminaImg"]["height"], () => {
                          c()
                        }, void 0, null, 1, 1, hu[1], o)
                      })
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[12]:
                  Object["defineProperty"](b["prototype"], "v$", {
                    ["value"]() {
                      var a = hr,
                        b = a[3],
                        c = a[4],
                        d = "Point",
                        e = "TEMP",
                        f = "talkHitArea",
                        g = "MQ";
                      Laya["Point"]["TEMP"]["x"] = this["talkHitArea"]["width"] / 4, Laya["Point"]["TEMP"]["y"] = this["talkHitArea"]["height"] / 3, this["talkHitArea"]["localToGlobal"](Laya["Point"]["TEMP"]), qs["instance"]()["xg"](Laya["Point"]["TEMP"]["x"], Laya["Point"]["TEMP"]["y"], this["MQ"][np["range"](0, this["MQ"]["length"], !0)], this["talkHitArea"], !1)
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[11]:
                  Object["defineProperty"](b["prototype"], "oZ", {
                    ["value"]() {
                      var a = hr,
                        b = a[3],
                        c = a[0],
                        d = "colEff";
                      this["colEff"] && !this["colEff"]["destroyed"] && this["sword"] && this["knife"] && (this["AZ"](), this["EZ"](this["EQ"]))
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[13]:
                  Object["defineProperty"](b["prototype"], "QQ", {
                    ["value"]() {
                      var a = hr,
                        b = a[0],
                        c = a[5],
                        d = "instance";
                      let e;
                      e = qx["instance"]();
                      e["Vc"]() ? sF["instance"]()["bn"]("WeaponScene") : pp["instance"]()["showTip"]("第" + e["Zc"]() + "天解锁武器呦~")
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[5]:
                  Object["defineProperty"](b["prototype"], "VQ", {
                    ["value"]() {
                      var a = hr;
                      sF["instance"]()["Bn"]("GetStaminaDialog")
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[4]:
                  Object["defineProperty"](b["prototype"], "XZ", {
                    ["value"]() {
                      var a = hr,
                        b = hu,
                        c = "followBtnImg";
                      this["followBtnImg"] && (this["followBtnImg"]["skin"] = "resources/img/mainUI/followBtn2.png", this["followBtnImg"]["x"] = -b[12], this["followBtnImg"]["y"] = b[37])
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[1]:
                  Object["defineProperty"](b["prototype"], "kZ", {
                    ["value"]() {
                      var a = hr,
                        b = a[5],
                        c = a[0],
                        d = "zQ",
                        e = "destroyed",
                        f = "jQ";
                      this["NQ"]++, this["zQ"] && !this["zQ"]["destroyed"] && Laya["Tween"]["killAll"](this["zQ"]), this["jQ"] && !this["jQ"]["destroyed"] && (this["jQ"]["visible"] = !1)
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[10]:
                  Object["defineProperty"](b["prototype"], "qQ", {
                    ["value"]() {
                      var a = hr,
                        b = hu,
                        c = a[2],
                        d = a[6],
                        e = a[3],
                        f = a[1],
                        g = b[132],
                        h = b[22],
                        i = "gZ",
                        j = "instance",
                        k = "scale",
                        l = "play",
                        m = "zhan2",
                        n = "pos",
                        o = "mat",
                        p = "addChild",
                        q = "zhaoYun";
                      let r, s, t;
                      this["gZ"] = nz["instance"]()["$d"]("aDou"), this["gZ"]["scale"](2, 2), this["gZ"]["play"]("zhan2", !0), this["gZ"]["pos"](b[19], -b[87]), this["mat"]["addChild"](this["gZ"]);
                      r = new Laya["Image"]("resources/img/gameObject/enemy/shadow1.png");
                      r["size"](g, b[61]), r["pos"](-h, b[327]), this["mat"]["addChild"](r);
                      t = nz["instance"]()["$d"]("zhaoYun");
                      t["pos"](h, b[328]), t["scale"](2, 2), t["play"]("shouye", !0), this["mat"]["addChild"](t);
                      s = nz["instance"]()["$d"]("zhaoYun");
                      s["pos"](b[171], g), s["scale"](2, 2), s["play"]("zhan2", !0), this["mat"]["addChild"](s)
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[2]:
                  Object["defineProperty"](b["prototype"], "JQ", {
                    ["value"]() {
                      var a = hr;
                      sF["instance"]()["Bn"]("SidebarDialog")
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[8]:
                  Object["defineProperty"](b["prototype"], "nZ", {
                    ["value"]() {
                      var a = hr,
                        b = a[0],
                        c = a[5],
                        d = a[32],
                        e = a[2],
                        f = a[3],
                        g = "rank",
                        h = "text",
                        i = "bZ",
                        j = "level",
                        k = "skin",
                        l = "visible",
                        m = "star5";
                      let n;
                      n = uq["instance"]()["rank"]["yu"];
                      if (this["rankTxt"]["text"] = n["rank"], "大帝" != n["rank"]) {
                        for (let a = 0; a < 5; a++) {
                          let b;
                          b = this["bZ"](a);
                          a <= n["level"] - 1 ? b["skin"] = "resources/img/gameOverUI/star4.png" : b["skin"] = "resources/img/gameOverUI/star3.png", b["visible"] = !0
                        }
                        this["star5"]["visible"] = !1
                      } else {
                        for (let a = 0; a < 5; a++) this["bZ"](a)["visible"] = !1;
                        this["star5"]["visible"] = !0, this["starNum"]["text"] = "x" + n["level"]
                      }
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[6]:
                  Object["defineProperty"](b["prototype"], "iZ", {
                    ["value"]() {
                      var a = hr,
                        b = hu,
                        c = a[3],
                        d = a[0],
                        e = a[28],
                        f = "visible",
                        g = "shopWalk";
                      let h;
                      this["shopBtn"]["visible"] = !1, this["shopWalk"]["visible"] = !0, this["shopWalk"]["x"] = b[216];
                      h = b[114] * (b[221] + this["shopWalk"]["width"]) / Laya["stage"]["height"];
                      Laya["Tween"]["to"](this["shopWalk"], {
                        ["x"]: h
                      }, b[101]), this["dZ"] = qs["instance"]()["Kf"](this["shopWalk"], ["resources/img/mainUI/walk0.png", "resources/img/mainUI/walk1.png", "resources/img/mainUI/walk2.png"], b[132])
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[19]:
                  Object["defineProperty"](b["prototype"], "onClosed", {
                    ["value"]() {
                      var a = hr,
                        b = a[0],
                        c = "GQ";
                      this["GQ"] > 0 && (qs["instance"]()["Jf"]("btnSparkle", this["GQ"]), this["GQ"] = 0), this["pZ"](), this["yZ"](), this["fZ"](), Laya["Tween"]["killAll"](this["dySidebarBtnLight"])
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[57]:
                  Object["defineProperty"](b["prototype"], "sZ", {
                    ["value"]() {
                      var a = hr,
                        b = "visible";
                      this["shopBtn"]["visible"] = !0, this["shopWalk"]["visible"] = !1
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[37]:
                  Object["defineProperty"](b["prototype"], "pZ", {
                    ["value"]() {
                      var a = hr,
                        b = a[0],
                        c = a[3],
                        d = "timer",
                        e = "clear",
                        f = "followBtnImg";
                      this["HQ"]++, Laya["timer"]["clear"](this, this["HZ"]), Laya["timer"]["clear"](this, this["WZ"]), this["followBtnImg"] && (Laya["Tween"]["killAll"](this["followBtnImg"]), this["XZ"]())
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[23]:
                  Object["defineProperty"](b["prototype"], "YZ", {
                    ["value"](a) {
                      var b = hr,
                        c = hu,
                        d = b[5],
                        e = b[0],
                        f = b[6],
                        g = b[3],
                        h = c[45],
                        i = c[135],
                        j = c[12],
                        k = c[81],
                        l = c[65],
                        m = c[9],
                        n = c[61],
                        o = "bag",
                        p = "getChildByName",
                        q = "TQ",
                        r = "RQ",
                        s = "rotation",
                        t = "UQ",
                        u = "Tween",
                        v = "killAll",
                        w = "create",
                        x = "to",
                        y = "duration",
                        z = "delay",
                        A = "chain";
                      let B, C;
                      if (a !== this["CQ"] || !this["bag"] || this["bag"]["destroyed"] || !this["bag"]["visible"]) return;
                      C = this["bag"]["getChildByName"]("weapon1"), B = this["bag"]["getChildByName"]("weapon2");
                      C && B && (this["TQ"] || (this["TQ"] = !0, this["RQ"] = C["rotation"], this["UQ"] = B["rotation"]), Laya["Tween"]["killAll"](C), Laya["Tween"]["killAll"](B), C["rotation"] = this["RQ"], B["rotation"] = this["UQ"], Laya["Tween"]["create"](C)["to"]("rotation", this["RQ"])["duration"](h)["delay"](i)["chain"]()["to"]("rotation", this["RQ"] - j)["duration"](k)["chain"]()["to"]("rotation", this["RQ"] + 10)["duration"](l)["chain"]()["to"]("rotation", this["RQ"] + 6)["duration"](h)["chain"]()["to"]("rotation", this["RQ"] - m)["duration"](k)["chain"]()["to"]("rotation", this["RQ"] + 8)["duration"](n)["chain"]()["to"]("rotation", this["RQ"])["duration"](h), Laya["Tween"]["create"](B)["to"]("rotation", this["UQ"])["duration"](h)["delay"](i)["chain"]()["to"]("rotation", this["UQ"] + j)["duration"](k)["chain"]()["to"]("rotation", this["UQ"] - 10)["duration"](l)["chain"]()["to"]("rotation", this["UQ"] - 6)["duration"](h)["chain"]()["to"]("rotation", this["UQ"] + m)["duration"](k)["chain"]()["to"]("rotation", this["UQ"] - 8)["duration"](n)["chain"]()["to"]("rotation", this["UQ"])["duration"](h))
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[46]:
                  Object["defineProperty"](b["prototype"], "CZ", {
                    ["value"](a, b, c, d, e, f) {
                      var g = hr,
                        h = g[3],
                        i = g[6],
                        j = g[2],
                        k = "Tween",
                        l = "create",
                        m = "to",
                        n = "rotation",
                        o = "duration",
                        p = "ease",
                        q = "then";
                      let r, s;
                      r = 2;
                      s = () => {
                        r--, r > 0 || a === this["EQ"] && f()
                      };
                      Laya["Tween"]["create"](this["sword"])["to"]("x", b["x"])["to"]("y", b["y"])["to"]("rotation", b["rotation"])["duration"](d)["ease"](e)["then"](s, this), Laya["Tween"]["create"](this["knife"])["to"]("x", c["x"])["to"]("y", c["y"])["to"]("rotation", c["rotation"])["duration"](d)["ease"](e)["then"](s, this)
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[22]:
                  Object["defineProperty"](b["prototype"], "WZ", {
                    ["value"](a) {
                      var b = hr,
                        c = b[0];
                      this["jZ"](a) && this["GZ"](a)
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[36]:
                  Object["defineProperty"](b["prototype"], "startGame", {
                    ["value"]() {
                      return p2(this, null, function*() {
                        var a = hr,
                          b = a[0],
                          c = a[5],
                          d = a[2],
                          e = a[6],
                          f = "instance",
                          g = "now",
                          h = "AQ",
                          i = "shopWalk";
                        if (!p0["instance"]()["jW"]()) return pp["instance"]()["showTip"]("体力不足，无法开始游戏！"), Promise["reject"]("体力不足");
                        Date["now"]() - this["AQ"] < this["PQ"] || (p0["instance"]()["zW"](), this["AQ"] = Date["now"](), yield this["LZ"](), qZ["instance"]()["Ou"](), vb["instance"]()["xA"](), sF["instance"]()["bn"]("MatchScene"), Laya["Tween"]["killAll"](this["shopWalk"]), this["shopWalk"]["visible"] = !1, this["shopWalk"]["x"] = hu[216], qs["instance"]()["Jf"]("imgLoop", this["dZ"]), this["shopWalk"]["skin"] = "resources/img/mainUI/walk0.png")
                      })
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[31]:
                  Object["defineProperty"](b["prototype"], "yZ", {
                    ["value"]() {
                      var a = hr,
                        b = a[0],
                        c = a[3],
                        d = a[5],
                        e = a[8],
                        f = a[6],
                        g = "Tween",
                        h = "killAll",
                        i = "sword",
                        j = "knife",
                        k = "playBtn",
                        l = "swordLight",
                        m = "knifeLight",
                        n = "colLight",
                        o = "destroyed",
                        p = "alpha",
                        q = "pos",
                        r = "DQ",
                        s = "rotation",
                        t = "IQ";
                      this["EQ"]++, Laya["Tween"]["killAll"](this["sword"]), Laya["Tween"]["killAll"](this["knife"]), Laya["Tween"]["killAll"](this["playBtn"]), Laya["Tween"]["killAll"](this["swordLight"]), Laya["Tween"]["killAll"](this["knifeLight"]), this["colLight"] && !this["colLight"]["destroyed"] && (Laya["Tween"]["killAll"](this["colLight"]), this["colLight"]["alpha"] = 0), this["kZ"](), this["BQ"] && this["sword"] && !this["sword"]["destroyed"] && this["knife"] && !this["knife"]["destroyed"] && (this["sword"]["pos"](this["DQ"]["x"], this["DQ"]["y"]), this["sword"]["rotation"] = this["DQ"]["rotation"], this["knife"]["pos"](this["IQ"]["x"], this["IQ"]["y"]), this["knife"]["rotation"] = this["IQ"]["rotation"]), this["swordLight"] && !this["swordLight"]["destroyed"] && (this["swordLight"]["alpha"] = 0), this["knifeLight"] && !this["knifeLight"]["destroyed"] && (this["knifeLight"]["alpha"] = 0), this["playBtn"] && !this["playBtn"]["destroyed"] && (this["playBtn"]["color"] = nA["IZ"], this["playBtn"]["scale"](1, 1))
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[41]:
                  Object["defineProperty"](b["prototype"], "rZ", {
                    ["value"]() {
                      var a = hr,
                        b = a[0],
                        c = a[2],
                        d = a[1],
                        e = "FQ",
                        f = "OQ",
                        g = "YQ",
                        rWk = "length",
                        h = "instance",
                        i = "rank",
                        j = "id",
                        k = "XQ",
                        l = "max",
                        m = "selectedIndex",
                        n = "level";
                      let o, p, q, r;
                      if (!this["FQ"] || !this["OQ"] || 0 === this["YQ"]["length"]) return;
                      o = uq["instance"]()["rank"]["yu"], p = uq["instance"]()["rank"]["Va"]["get"](o["id"]);
                      if (!p) throw new Error("段位配置不存在: " + o["id"]);
                      this["XQ"] = !0;
                      q = Math["max"](0, this["YQ"]["indexOf"](o["id"]));
                      this["FQ"]["selectedIndex"] = q;
                      r = Math["max"](1, p["level"]);
                      this["OQ"]["labels"] = Array["from"]({
                        ["length"]: r
                      }, (a, b) => String(b + 1))["join"](","), this["OQ"]["selectedIndex"] = Math["max"](0, Math["min"](o["level"] - 1, r - 1)), this["XQ"] = !1
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[15]:
                  Object["defineProperty"](b["prototype"], "AZ", {
                    ["value"]() {
                      var a = hr,
                        b = a[0],
                        c = a[8],
                        d = a[3],
                        e = a[6],
                        f = "BQ",
                        g = "colEff",
                        h = "sword",
                        rWA = "rotation",
                        i = "BZ",
                        j = "knife",
                        k = "DZ",
                        l = "alpha",
                        m = "colLight";
                      this["BQ"] || (this["colEff"]["mouseEnabled"] = !1, this["colEff"]["visible"] = !0, this["DQ"] = {
                        ["x"]: this["sword"]["x"],
                        ["y"]: this["sword"]["y"],
                        ["rotation"]: nA["BZ"]
                      }, this["IQ"] = {
                        ["x"]: this["knife"]["x"],
                        ["y"]: this["knife"]["y"],
                        ["rotation"]: nA["DZ"]
                      }, this["sword"]["rotation"] = nA["BZ"], this["knife"]["rotation"] = nA["DZ"], this["swordLight"]["alpha"] = 0, this["knifeLight"]["alpha"] = 0, this["colLight"] && !this["colLight"]["destroyed"] && (this["colLight"]["alpha"] = 0), this["BQ"] = !0)
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[21]:
                  Object["defineProperty"](b["prototype"], "Fe", {
                    ["value"]() {
                      var a = hr;
                      sF["instance"]()["bn"]("RankScene")
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[30]:
                  Object["defineProperty"](b["prototype"], "GZ", {
                    ["value"](a) {
                      var b = hr,
                        c = hu,
                        d = b[0],
                        e = b[3],
                        f = b[2],
                        g = c[8],
                        h = c[132],
                        i = c[12],
                        j = "jZ",
                        k = "to",
                        l = "duration",
                        m = "chain";
                      let n;
                      if (!this["jZ"](a)) return;
                      n = this["followBtnImg"];
                      this["XZ"](), Laya["Tween"]["create"](n)["to"]("x", -g)["duration"](h)["chain"]()["to"]("x", -7)["duration"](h)["chain"]()["to"]("x", -g)["duration"](h)["chain"]()["to"]("x", -7)["duration"](h)["chain"]()["to"]("x", -i)["duration"](h)["chain"]()["to"]("y", 7)["duration"](c[325])["then"](() => {
                        this["jZ"](a) && (n["skin"] = "resources/img/mainUI/followBtn3.png", n["x"] = -i, Laya["timer"]["once"](c[123], this, this["HZ"], [a], !1))
                      }, this)
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[35]:
                  Object["defineProperty"](b["prototype"], "onAwake", {
                    ["value"]() {
                      var a = hr,
                        b = a[0],
                        c = a[7],
                        d = a[5],
                        e = a[3],
                        f = a[8],
                        g = a[6],
                        h = "s$",
                        i = "Gj",
                        j = "playBtn",
                        k = "on",
                        l = "Event",
                        m = "CLICK",
                        n = "settingBtn",
                        o = "instance",
                        p = "bn",
                        q = "rankBtn",
                        r = "avatarArea",
                        s = "addStaminaBtn",
                        t = "weaponBtn",
                        u = "shopWalk",
                        v = "dySidebarBtn",
                        w = "followBtn";
                      this["qQ"](), this["s$"](), this["Gj"](), this["playBtn"]["on"](Laya["Event"]["CLICK"], this, this["startGame"]), this["settingBtn"]["on"](Laya["Event"]["CLICK"], this, () => {
                        sF["instance"]()["bn"]("SettingScene")
                      }), this["shopBtn"]["on"](Laya["Event"]["CLICK"], this, () => {
                        sF["instance"]()["bn"]("ShopScene")
                      }), this["rankBtn"]["on"](Laya["Event"]["CLICK"], this, this["Fe"]), this["talkHitArea"]["on"](Laya["Event"]["CLICK"], this, this["v$"]), this["avatarArea"]["on"](Laya["Event"]["CLICK"], this, this["$Q"]), this["addStaminaBtn"]["on"](Laya["Event"]["CLICK"], this, this["VQ"]), this["weaponBtn"]["on"](Laya["Event"]["CLICK"], this, this["QQ"]), this["shopWalk"]["on"](Laya["Event"]["CLICK"], this, this["ZQ"]), tR["instance"]()["Lu"](() => {
                        this["KQ"]()
                      }), this["dySidebarBtn"]["on"](Laya["Event"]["CLICK"], this, this["JQ"]), this["followBtn"]["on"](Laya["Event"]["CLICK"], this, this["tZ"]), qs["instance"]()["Tf"]([this["playBtn"], this["settingBtn"], this["avatarArea"], this["rankBtn"], this["weaponBtn"], this["addStaminaBtn"], this["dySidebarBtn"], this["followBtn"], this["shopWalk"]]), oc["instance"]["on"](sS["$t"], this, this["s$"]), oc["instance"]["on"](sS["Vt"], this, this["Gj"]), oc["instance"]["on"](sS["Qt"], this, this["sZ"]), oc["instance"]["on"](sS["Zt"], this, this["iZ"]), oc["instance"]["on"](sS["_s"], this, this["hZ"]), this["eZ"](), sE["instance"]()["_j"] && this["aZ"]()
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[32]:
                  Object["defineProperty"](b["prototype"], "EZ", {
                    ["value"](a) {
                      var b = hr,
                        c = hu,
                        d = b[0],
                        e = c[9],
                        f = "EQ",
                        g = "colEff",
                        rXg = "rotation",
                        h = "CZ",
                        i = "Ease",
                        j = "sineInOut";
                      let k, l, m, n, o, p, q, r, s;
                      if (a !== this["EQ"] || !this["colEff"] || this["colEff"]["destroyed"]) return;
                      l = this["DQ"], o = this["IQ"], n = nA["TZ"], k = {
                        ["x"]: l["x"] + n,
                        ["y"]: l["y"],
                        ["rotation"]: nA["RZ"]
                      }, r = {
                        ["x"]: o["x"] - n,
                        ["y"]: o["y"],
                        ["rotation"]: nA["UZ"]
                      }, m = {
                        ["x"]: k["x"] - 3,
                        ["y"]: k["y"] + 5,
                        ["rotation"]: k["rotation"] + e
                      }, p = {
                        ["x"]: r["x"] + 3,
                        ["y"]: r["y"] + 5,
                        ["rotation"]: r["rotation"] - e
                      }, s = {
                        ["x"]: k["x"] - 2,
                        ["y"]: k["y"] - 4,
                        ["rotation"]: k["rotation"] - 7
                      }, q = {
                        ["x"]: r["x"] + 2,
                        ["y"]: r["y"] - 4,
                        ["rotation"]: r["rotation"] + 7
                      };
                      this["CZ"](a, k, r, c[95], Laya["Ease"]["sineInOut"], () => {
                        this["CZ"](a, m, p, c[132], Laya["Ease"]["sineOut"], () => {
                          this["CZ"](a, s, q, c[174], Laya["Ease"]["sineInOut"], () => {
                            let e, g;
                            g = {
                              ["x"]: l["x"],
                              ["y"]: l["y"],
                              ["rotation"]: l["rotation"]
                            }, e = {
                              ["x"]: o["x"],
                              ["y"]: o["y"],
                              ["rotation"]: o["rotation"]
                            };
                            this["CZ"](a, g, e, c[200], Laya["Ease"]["quadIn"], () => {
                              a === this["EQ"] && this["FZ"](a, () => {
                                a === this["EQ"] && Laya["timer"]["once"](c[329], this, () => {
                                  a === this["EQ"] && this["EZ"](a)
                                })
                              })
                            })
                          })
                        })
                      })
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[60]:
                  Object["defineProperty"](b["prototype"], "PZ", {
                    ["value"](a) {
                      var b = hr,
                        c = b[0],
                        d = "instance";
                      let e;
                      if (this["XQ"]) return;
                      e = uq["instance"]()["rank"]["yu"];
                      qZ["instance"]()["Uu"](e["id"], a + 1), qZ["instance"]()["Cu"](), this["nZ"](), this["rZ"]()
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[43]:
                  Object["defineProperty"](b["prototype"], "MZ", {
                    ["value"](a) {
                      var b = hr,
                        c = b[0],
                        d = "instance";
                      let e, f;
                      if (this["XQ"]) return;
                      e = this["YQ"][a];
                      if (null == e) return;
                      f = uq["instance"]()["rank"]["yu"];
                      qZ["instance"]()["Uu"](e, f["level"]), qZ["instance"]()["Cu"](), this["nZ"](), this["rZ"]()
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[44]:
                  Object["defineProperty"](b["prototype"], "ZQ", {
                    ["value"]() {
                      var a = hr,
                        b = hu,
                        c = a[3],
                        d = "Point",
                        e = "TEMP",
                        f = "shopWalk",
                        g = "WQ";
                      Laya["Point"]["TEMP"]["x"] = this["shopWalk"]["width"] / 2 - b[1], Laya["Point"]["TEMP"]["y"] = b[22], this["shopWalk"]["localToGlobal"](Laya["Point"]["TEMP"]), qs["instance"]()["xg"](Laya["Point"]["TEMP"]["x"], Laya["Point"]["TEMP"]["y"], this["WQ"][np["range"](0, this["WQ"]["length"], !0)], this["shopWalk"], !1)
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[28]:
                  Object["defineProperty"](b["prototype"], "jZ", {
                    ["value"](a) {
                      var b = hr,
                        c = "followBtn",
                        d = "followBtnImg";
                      return a === this["HQ"] && this["followBtn"] && this["followBtn"]["visible"] && this["followBtnImg"] && !this["followBtnImg"]["destroyed"]
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[56]:
                  Object["defineProperty"](b["prototype"], "zZ", {
                    ["value"](a) {
                      var b = hr;
                      Laya["timer"]["once"](hu[100], this, this["WZ"], [a], !1)
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[62]:
                  Object["defineProperty"](b["prototype"], "bZ", {
                    ["value"](a) {
                      var b = hr,
                        c = b[3];
                      switch (a) {
                        case 0:
                          return this["star0"];
                        case 1:
                          return this["star1"];
                        case 2:
                          return this["star2"];
                        case 3:
                          return this["star3"];
                        case 4:
                          return this["star4"];
                        default:
                          throw new Error("MainScene.mainRankStarImage: invalid index " + a)
                      }
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[59]:
                  Object["defineProperty"](b["prototype"], "aZ", {
                    ["value"]() {
                      var a = hr,
                        b = hu,
                        c = a[5],
                        d = a[1],
                        e = a[2],
                        f = a[0],
                        g = a[3],
                        h = a[6],
                        i = a[4],
                        j = a[8],
                        k = b[95],
                        l = b[1],
                        m = b[330],
                        n = b[28],
                        o = b[6],
                        p = b[331],
                        q = "width",
                        r = "Text",
                        s = "text",
                        t = "fontSize",
                        u = "bold",
                        v = "color",
                        w = "stroke",
                        x = "strokeColor",
                        y = "pos",
                        z = "addChild",
                        A = "ComboBox",
                        B = "labels",
                        C = "height",
                        D = "itemSize",
                        E = "scrollType",
                        F = "ScrollType",
                        G = "Vertical",
                        H = "selectHandler",
                        I = "Handler",
                        J = "create",
                        K = "OQ",
                        L = "FQ",
                        M = "rank",
                        N = "Va";
                      let O, P, Q, R, S, T, U, V, W, X, Y;
                      w1_et: for (let g of vY) {
                        switch (g) {
                          case 0:
                            W = "#ffffff", P = "#000000", Y = Math["max"](0, .5 * (Laya["stage"]["width"] - b[254])), S = Y, O = Y + k + b[30], R = new Laya["Text"];
                            break;
                          case 1:
                            V["text"] = "星级", V["fontSize"] = l, V["bold"] = !0, V["color"] = W, V["stroke"] = 3, V["strokeColor"] = P, V["pos"](O, m), this["addChild"](V);
                            break;
                          case 2:
                            this["YQ"] = Q;
                            break;
                          case 3:
                            V = new Laya["Text"];
                            break;
                          case 4:
                            U = new Laya["ComboBox"];
                            break;
                          case 5:
                            T = new Laya["ComboBox"];
                            break;
                          case 6:
                            R["text"] = "段位", R["fontSize"] = l, R["bold"] = !0, R["color"] = W, R["stroke"] = 3, R["strokeColor"] = P, R["pos"](S, m), this["addChild"](R);
                            break;
                          case 7:
                            U["labels"] = "1", U["width"] = b[111], U["height"] = n, U["itemSize"] = o, U["scrollType"] = Laya["ScrollType"]["Vertical"], U["pos"](O, p), U["selectHandler"] = Laya["Handler"]["create"](this, a => {
                              this["PZ"](a)
                            }, null, !1), this["addChild"](U), this["OQ"] = U;
                            break;
                          case 8:
                            if (this["FQ"] || this["OQ"]) return;
                            break;
                          case 9:
                            X = uq["instance"]()["rank"], Q = Array["from"](X["Va"]["keys"]())["sort"]((a, b) => a - b);
                            break;
                          case 10:
                            T["labels"] = Q["map"](a => X["Va"]["get"](a)["rank"])["join"](","), T["width"] = k, T["height"] = n, T["itemSize"] = o, T["scrollType"] = Laya["ScrollType"]["Vertical"], T["pos"](S, p), T["selectHandler"] = Laya["Handler"]["create"](this, a => {
                              this["MZ"](a)
                            }, null, !1), this["addChild"](T), this["FQ"] = T;
                            break;
                          default:
                            break
                        }
                      }
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[58]:
                  Object["defineProperty"](b["prototype"], "lZ", {
                    ["value"]() {
                      var a = hr,
                        b = hu,
                        c = a[0],
                        d = a[3],
                        e = a[4],
                        f = b[45],
                        g = b[81],
                        h = "bag",
                        i = "destroyed",
                        j = "visible",
                        k = "CQ",
                        l = "to",
                        m = "scaleX",
                        n = "scaleY",
                        o = "duration",
                        p = "chain";
                      let q, r;
                      if (this["fZ"](), !this["bag"] || this["bag"]["destroyed"] || !this["bag"]["visible"]) return;
                      r = ++this["CQ"], q = this["bag"]["y"];
                      this["YZ"](r), Laya["Tween"]["create"](this["bag"])["to"]("scaleX", 1.2)["to"]("scaleY", .8)["duration"](f)["delay"](b[135])["chain"]()["to"]("scaleX", .8)["to"]("scaleY", 1.2)["to"]("y", q - b[22])["duration"](g)["chain"]()["to"]("scaleX", .9)["to"]("scaleY", 1.1)["to"]("y", q)["duration"](b[65])["chain"]()["to"]("scaleX", 1.1)["to"]("scaleY", .9)["duration"](f)["chain"]()["to"]("scaleX", .9)["to"]("scaleY", 1.1)["to"]("y", q - b[12])["duration"](g)["chain"]()["to"]("scaleX", .95)["to"]("scaleY", 1.15)["to"]("y", q)["duration"](b[61])["chain"]()["to"]("scaleX", 1)["to"]("scaleY", 1)["duration"](f)["then"](() => {
                        r === this["CQ"] && this["bag"] && !this["bag"]["destroyed"] && this["bag"]["visible"] && this["lZ"]()
                      }, this)
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[55]:
                  "use strict";
                  break;
                case c[17]:
                  Object["defineProperty"](b["prototype"], "SZ", {
                    ["value"]() {
                      var a = hr,
                        b = a[0],
                        c = a[3],
                        d = a[2],
                        e = "jQ",
                        f = "destroyed",
                        g = "NQ",
                        h = "visible";
                      let i, j, k, l;
                      if (!this["mZ"]()) return;
                      i = this["jQ"], l = this["zQ"];
                      if (!i || !l || i["destroyed"] || l["destroyed"]) return;
                      this["kZ"]();
                      k = ++this["NQ"], j = Math["max"](1, this["playBtn"]["width"]);
                      i["visible"] = !0, l["x"] = .5 * -j, Laya["Tween"]["create"](l)["to"]("x", 1.5 * j)["duration"](nA["xZ"])["ease"](Laya["Ease"]["linearNone"])["then"](() => {
                        k === this["NQ"] && this["jQ"] && !this["jQ"]["destroyed"] && (this["jQ"]["visible"] = !1)
                      }, this)
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
