      } ["bind"](this)["apply"](), pw = Laya["Vector2"], ro = function() {
        var a = hr;
        var b = [8, 2, 9, 1, 4, 6, 7, 5, 0, 3];
        if (bW) kF(b, 7, 0);
        if (bG) kF(b, 9, 1);
        if (bY) kF(b, 8, 3);
        let c;
        c = class extends qE {
          constructor() {
            var a = hr,
              b = a[0],
              c = a[2],
              d = "Point";
            var e;
            e = arguments;
            super(...e), this["nm"] = !0, this["rm"] = !1, this["id"] = 0, this["type"] = 0, this["om"] = !1, this["lm"] = !1, this["Vy"] = new Laya["Point"], this["Qy"] = new Laya["Point"], this["um"] = 0, this["pm"] = !1, this["ym"] = 0, this["fm"] = 0, this["gm"] = 1, this["dm"] = 0, this["Lm"] = 0, this["wm"] = new pw, this["vm"] = !1, this["_m"] = !1, this["km"] = new Laya["Vector2"], this["Sm"] = hu[45], this["xm"] = 0, this["bm"] = 1, this["Mm"] = 0, this["Pm"] = 0, this["Am"] = 0, this["Em"] = 0, this["Bm"] = 1 / 0, this["Dm"] = 0, this["Im"] = null, this["Cm"] = !1, this["Tm"] = [], this["Rm"] = 0, this["Um"] = !1, this["Fm"] = 0, this["Om"] = 0, this["Ym"] = [], this["Xm"] = ["泥泞的地面让我寸步难行！", "我好像踩到什么了", "我要吐了"], this["Gm"] = !1, this["Hm"] = 0, this["Wm"] = 0, this["jm"] = !1, this["zm"] = 0, this["Nm"] = !1, this["qm"] = !1, this["$m"] = [], this["Vm"] = new Laya["Point"](0, 0)
          }
          gameOver() {
            var a = hr,
              b = a[6],
              c = a[3],
              d = a[0],
              e = a[5],
              f = "instance",
              g = "event",
              h = "id",
              i = "Tween",
              j = "killAll",
              k = "enemy",
              l = "hw",
              m = "km",
              n = "visible",
              o = "removeSelf",
              p = "width",
              q = "aw",
              r = "ow",
              s = "lw",
              t = "length",
              u = "recover",
              v = "$m";
            super["gameOver"](), oc["instance"]["event"](sS["es"], this["id"]), oc["instance"]["event"](sS["ot"], this["id"]), this["offAll"](), Laya["timer"]["clearAll"](this), Laya["Tween"]["killAll"](this["enemy"]), qs["instance"]()["Jf"]("shake", this["zm"]), Laya["Tween"]["killAll"](this["hw"]), this["Bm"] = 1 / 0, this["rm"] = !1, this["vm"] = !1, this["km"]["x"] = 0, this["km"]["y"] = 0, this["Cm"] = !1, this["path"] = null, this["Hm"] = 0, this["Lm"] = 0, this["curState"] = 0, this["enemy"]["visible"] = !0, this["enemy"]["anchorX"] = 0, this["enemy"]["anchorY"] = 0, this["enemy"]["scale"](1, 1), this["enemy"]["rotation"] = 0, this["enemy"]["removeSelf"](), this["hw"]["width"] = this["aw"], this["ew"]["width"] = this["aw"], this["ow"]["x"] = -1, this["ow"]["y"] = -1, this["lw"]["x"] = -1, this["lw"]["y"] = -1, this["Ym"]["length"] = 0, this["Im"]["visible"] = !1, s0["instance"]()["recover"](this), this["pm"] = !0;
            for (let b = this["$m"]["length"] - 1; b >= 0; b--) {
              let d;
              d = this["$m"][b];
              d["removeSelf"](), d["alpha"] = 1, rw["instance"]()["recover"]("footprint", d)
            }
            this["$m"]["length"] = 0, this["Gm"] = !1
          }
        };
        ! function() {
          var a = hr,
            d = hu,
            e = a[0],
            f = a[1],
            g = a[2],
            h = a[5],
            i = a[4],
            j = d[19],
            k = d[15],
            l = d[4],
            m = d[5],
            n = d[6],
            o = d[56],
            p = d[32],
            q = d[17],
            r = d[23],
            s = d[59],
            t = d[12],
            u = d[35],
            v = d[3],
            w = d[28],
            x = d[22],
            y = d[55],
            z = d[8],
            A = d[62],
            B = d[26],
            C = d[14],
            D = d[41],
            E = d[60],
            F = d[21],
            G = d[13],
            H = d[1],
            I = d[10],
            J = d[30],
            K = d[11],
            L = d[57],
            M = d[7],
            N = d[9],
            O = d[44],
            P = d[31],
            Q = d[58],
            R = d[46],
            S = d[37],
            T = d[36],
            U = d[43],
            V = d[2],
            W = "defineProperty",
            oj6 = "value",
            oj7 = "enumerable",
            oj8 = "configurable";
          var oj9 = "writable",
            oka = "get";
          let X = 0,
            Y = mz;
          w1_cH: while (X < d[45]) {
            ++X;
            switch (Y) {
              case j:
                Object["defineProperty"](c["prototype"], "am", {
                  ["value"]() {
                    return this["enemy"]
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = k;
                break;
              case 4:
                Object["defineProperty"](c["prototype"], "gw", {
                  ["value"]() {},
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = l;
                break;
              case 9:
                Object["defineProperty"](c["prototype"], "Hw", {
                  ["value"](a) {
                    var b = hr,
                      c = hu,
                      d = b[0],
                      e = b[3],
                      f = b[5],
                      g = b[1],
                      h = b[4],
                      i = b[6],
                      j = c[167],
                      k = c[81],
                      l = "nw",
                      m = "visible",
                      n = "enemy",
                      o = "anchorX",
                      p = "anchorY",
                      q = "pos",
                      r = "width",
                      s = "height",
                      t = "Tween",
                      u = "create",
                      v = "to",
                      w = "scaleX",
                      x = "scaleY",
                      y = "duration",
                      z = "then",
                      A = "ow",
                      B = "lw",
                      C = "instance",
                      D = "map",
                      E = "gridHei";
                    this["nw"]["visible"] = !1, this["enemy"]["anchorX"] = .5, this["enemy"]["anchorY"] = 1, this["enemy"]["scale"](0, 0), this["enemy"]["visible"] = !0, this["enemy"]["pos"](this["enemy"]["x"] + this["enemy"]["width"] / 2, this["enemy"]["y"] + this["enemy"]["height"] / 2), Laya["Tween"]["create"](this["enemy"])["to"]("scaleX", 1)["to"]("scaleY", 1)["duration"](j)["then"](() => {
                      let b;
                      b = this["ow"]["y"] > this["lw"]["y"] ? this["enemy"]["y"] - uq["instance"]()["map"]["gridHei"] / 4 : this["enemy"]["y"] + uq["instance"]()["map"]["gridHei"] * (3 / 4);
                      Laya["Tween"]["create"](this["enemy"])["to"]("y", b)["to"]("scaleX", 1.2)["to"]("scaleY", 1.2)["duration"](j)["then"](() => {
                        let b;
                        b = this["ow"]["y"] > this["lw"]["y"] ? this["enemy"]["y"] - uq["instance"]()["map"]["gridHei"] / 4 : this["enemy"]["y"] + uq["instance"]()["map"]["gridHei"] * (3 / 4);
                        Laya["Tween"]["create"](this["enemy"])["to"]("y", b)["to"]("scaleX", 1)["to"]("scaleY", 1)["duration"](j)["then"](() => {
                          this["nw"]["visible"] = !0, Laya["Tween"]["create"](this["enemy"])["to"]("scaleX", 1.1)["to"]("scaleY", .9)["duration"](k)["then"](() => {
                            Laya["Tween"]["create"](this["enemy"])["to"]("scaleX", 1)["to"]("scaleY", 1)["duration"](k)["then"](() => {
                              this["enemy"]["anchorX"] = 0, this["enemy"]["anchorY"] = 0, this["enemy"]["pos"](this["enemy"]["x"] - this["enemy"]["width"] / 2, this["enemy"]["y"] - this["enemy"]["height"]), a && a()
                            }), oc["instance"]["event"](sS["rs"]), this["Ww"]()
                          })
                        })
                      })
                    })
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = m;
                break;
              case n:
                Object["defineProperty"](c["prototype"], "zw", {
                  ["value"](a, b, c = !1) {
                    var d = hr,
                      e = d[0],
                      f = "Zi",
                      g = "Zm",
                      h = "dm",
                      i = "gm";
                    3 == a ? this["xm"] += b : 4 == a ? (this["fm"] += b, !c && b > 0 && (this["Zi"] += b), this["Zi"] > this["Zm"] && (this["Zi"] = this["Zm"])) : 6 == a && (this["dm"] += b, this["tw"]["scale"](this["gm"] + this["dm"], this["gm"] + this["dm"]))
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = o;
                break;
              case l:
                Object["defineProperty"](c["prototype"], "ww", {
                  ["value"]() {},
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = p;
                break;
              case q:
                Object["defineProperty"](c["prototype"], "Jm", {
                  ["value"]() {
                    var a = hr;
                    return t1["zn"]["Enemy"]
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = 6;
                break;
              case r:
                Object["defineProperty"](c["prototype"], "EL", {
                  ["value"](a) {
                    var b = hr,
                      c = hu,
                      d = b[0],
                      e = b[6],
                      f = c[123],
                      g = "Lm",
                      h = "path",
                      i = "length",
                      j = "sw",
                      k = "map",
                      l = "ye",
                      m = "enemy",
                      n = "gridHei",
                      o = "On";
                    let p, q, r, s;
                    if (this["Lm"] < 0) return;
                    if (this["Lm"] >= this["path"]["length"]) return;
                    r = this["path"][this["Lm"]], s = r["x"] * this["sw"]["map"]["ye"] - this["enemy"]["x"], q = r["y"] * this["sw"]["map"]["gridHei"] - this["enemy"]["y"], p = Math["sqrt"](s * s + q * q);
                    if (p < 1) this["Lm"]++;
                    else {
                      let b, c;
                      c = s / p, b = q / p;
                      this["wm"]["setValue"](c, b), this["enemy"]["x"] += c * this["On"] * a / f, this["enemy"]["y"] += b * this["On"] * a / f
                    }
                    this["enemy"]["zIndex"] = t1["entityZIndexFromPixelY"](this["enemy"]["y"], uq["instance"]()["map"]["gridHei"], this["Jm"]()), this["Bm"] = p + (this["path"]["length"] - 1 - this["Lm"]) * this["sw"]["map"]["ye"], this["Bw"]()
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = s;
                break;
              case t:
                Object["defineProperty"](c["prototype"], "pw", {
                  ["value"]() {
                    var a = hr,
                      b = a[0];
                    switch (this["curState"]) {
                      case 0:
                        this["rm"] = !0;
                        break;
                      case 1:
                        this["mw"]();
                        break;
                      case 2:
                        this["ww"]();
                        break;
                      case 3:
                        this["Im"]["visible"] = !1
                    }
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = 8;
                break;
              case p:
                Object["defineProperty"](c["prototype"], "Cw", {
                  ["value"]() {},
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = 10;
                break;
              case u:
                Object["defineProperty"](c["prototype"], "Gw", {
                  ["value"](a) {},
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = 9;
                break;
              case 6:
                Object["defineProperty"](c["prototype"], "On", {
                  ["get"]() {
                    var a = hr,
                      b = a[0],
                      c = "Sm",
                      d = "bm";
                    let e;
                    e = this["Sm"] + this["xm"];
                    return this["bm"] = e / this["Sm"], this["tw"]["bm"](this["bm"]), e
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true
                });
                Y = v;
                break;
              case 7:
                Object["defineProperty"](c["prototype"], "init", {
                  ["value"](a) {
                    var b = hr,
                      c = b[0],
                      d = b[2],
                      e = b[4],
                      f = b[1],
                      g = b[5],
                      h = b[3],
                      i = "sw",
                      j = "instance",
                      k = "id",
                      l = "enemy",
                      m = "iw",
                      n = "getChildByName",
                      o = "hw",
                      p = "ew",
                      q = "aw",
                      r = "width",
                      s = "Im",
                      t = "event",
                      u = "ow";
                    this["sw"] = uq["instance"](), this["nm"] = a, this["pm"] = !1, this["id"] = this["sw"]["xy"](), this["enemy"]["name"] = `enemy_${this["id"]}`, this["enemy"]["zIndex"] = t1["entityZIndexFromPixelY"](this["enemy"]["y"], this["sw"]["map"]["gridHei"], this["Jm"]()), this["iw"] = this["enemy"]["getChildByName"]("hpBgImg"), this["hw"] = this["iw"]["getChildByName"]("hpImg1"), this["ew"] = this["iw"]["getChildByName"]("hpImg2"), this["Qm"] = this["iw"]["getChildByName"]("hpNum"), this["aw"] = this["ew"]["width"], this["hw"]["width"] = this["aw"], this["ew"]["width"] = this["aw"], this["nw"] = this["enemy"]["getChildByName"]("shadow"), this["Im"] = this["enemy"]["getChildByName"]("stun"), this["Im"]["visible"] = !1, this["curState"] = 0, oc["instance"]["event"](sS["bt"], this["enemy"]), this["rw"](), this["enemy"]["pos"](this["ow"]["x"], this["ow"]["y"]), oc["instance"]["event"](sS["nt"], this["id"], this)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = w;
                break;
              case v:
                Object["defineProperty"](c["prototype"], "centerX", {
                  ["get"]() {
                    var a = hr,
                      b = "enemy";
                    return this["enemy"]["x"] + this["enemy"]["width"] / 2
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true
                });
                Y = 5;
                break;
              case k:
                Object["defineProperty"](c["prototype"], "Bw", {
                  ["value"]() {
                    var a = hr,
                      b = a[0],
                      c = a[3],
                      d = a[4],
                      e = "sw",
                      f = "au",
                      g = "Vy",
                      h = "enemy",
                      i = "Vm",
                      j = "instance";
                    let k;
                    if (!(this["nm"] ? this["sw"]["au"]["Ri"] : this["sw"]["au"]["Ui"])) return;
                    if (this["Vy"]["x"] = this["enemy"]["x"], this["Vy"]["y"] = this["enemy"]["y"], np["bs"](this["Vm"], this["Vy"]) < hu[22]) return;
                    k = rw["instance"]()["getItem"]("footprint", this);
                    k["zIndex"] = t1["Qn"], oc["instance"]["event"](sS["xt"], k), k["pos"](this["enemy"]["x"] + this["enemy"]["width"] / 2, this["enemy"]["y"] + 3 / 4 * this["enemy"]["height"]), k["rotation"] = np["angle"](this["Vm"], this["Vy"]), this["Vm"]["x"] = this["enemy"]["x"], this["Vm"]["y"] = this["enemy"]["y"], this["$m"]["push"](k)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = x;
                break;
              case x:
                Object["defineProperty"](c["prototype"], "uw", {
                  ["value"]() {
                    var a = hr,
                      b = a[4],
                      c = "$m",
                      d = "alpha";
                    for (let e = this["$m"]["length"] - 1; e >= 0; e--) {
                      let f;
                      f = this["$m"][e];
                      f["alpha"] -= .01, f["alpha"] <= 0 && (f["removeSelf"](), f["alpha"] = 1, rw["instance"]()["recover"]("footprint", f), this["$m"]["splice"](e, 1))
                    }
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = y;
                break;
              case m:
                Object["defineProperty"](c["prototype"], "jw", {
                  ["value"](a) {
                    var b = hr,
                      c = b[0];
                    return 3 == a ? this["Sm"] : 4 == a ? this["Km"] : 6 == a ? this["gm"] : void 0
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = n;
                break;
              case z:
                Object["defineProperty"](c["prototype"], "Lw", {
                  ["value"]() {
                    var a = hr,
                      b = a[4],
                      c = a[0],
                      d = a[3],
                      e = "instance",
                      f = "enemy",
                      g = "Vy",
                      h = "sw",
                      i = "au";
                    let j;
                    Math["random"]() < .5 && pC["instance"]()["playSound"]("enemy_dead"), this["Cm"] = !0, this["Im"]["visible"] = !1, this["enemy"]["event"]("onDead");
                    j = 1;
                    this["om"] && (j = 10), this["nm"] ? (this["Vy"]["x"] = this["enemy"]["x"] + this["enemy"]["width"] / 2, this["Vy"]["y"] = this["enemy"]["y"], this["enemy"]["parent"]["localToGlobal"](this["Vy"]), qs["instance"]()["fg"](this["Vy"]["x"], this["Vy"]["y"], j), this["sw"]["au"]["gold"] += j) : this["sw"]["au"]["Ji"] += j, this["Fw"]()
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = A;
                break;
              case B:
                Object["defineProperty"](c["prototype"], "cw", {
                  ["value"](a) {
                    var b = hr,
                      c = b[0],
                      d = "curState";
                    return 0 != this["curState"] && this["nm"] == a && 4 != this["curState"] && this["rm"]
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = C;
                break;
              case s:
                Object["defineProperty"](c["prototype"], "Dw", {
                  ["value"]() {
                    var a = hr,
                      b = "path",
                      c = "sw",
                      d = "map",
                      e = "enemy";
                    let f, g;
                    g = 0, f = 1 / 0;
                    for (let h = 0; h < this["path"]["length"]; h++) {
                      let i, j, k, l;
                      k = this["path"][h], i = k["x"] * this["sw"]["map"]["ye"] - this["enemy"]["x"], j = k["y"] * this["sw"]["map"]["gridHei"] - this["enemy"]["y"], l = i * i + j * j;
                      l < f && (f = l, g = h)
                    }
                    return g
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = 3;
                break;
              case 1:
                Object["defineProperty"](c["prototype"], "mw", {
                  ["value"]() {},
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = 4;
                break;
              case D:
                Object["defineProperty"](c["prototype"], "y", {
                  ["get"]() {
                    return this["enemy"]["y"]
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true
                });
                Y = E;
                break;
              case F:
                Object["defineProperty"](c["prototype"], "Fw", {
                  ["value"]() {
                    var a = hr,
                      c = a[0],
                      d = a[6],
                      e = a[9],
                      f = a[3],
                      g = a[2],
                      h = "weaponId",
                      i = "fragmentNum",
                      j = "Point",
                      k = "TEMP",
                      l = "instance",
                      m = "nm",
                      n = "sw",
                      o = "au",
                      p = "push",
                      q = "enemy",
                      r = "width",
                      s = "height",
                      t = "localToGlobal",
                      u = "En",
                      v = "BattleScene",
                      w = "getChildByName",
                      x = "box";
                    let y, z, A, B, C, D, E;
                    w1_cJ: for (let d of b) {
                      switch (d) {
                        case 0:
                          if (B["weaponId"] < 0 || B["fragmentNum"] <= 0) return;
                          break;
                        case 1:
                          C = Laya["Point"]["TEMP"]["x"], z = Laya["Point"]["TEMP"]["y"];
                          break;
                        case 2:
                          qs["instance"]()["Ad"](B["weaponId"], A, E, D, C, z, null, null, this["nm"]), this["nm"] ? this["sw"]["au"]["zi"]["push"](B) : this["sw"]["au"]["Ni"]["push"](B), this["sw"]["au"]["ji"]["push"](B);
                          break;
                        case 3:
                          if (!qx["instance"]()["Vc"]()) return;
                          break;
                        case 4:
                          qx["instance"]()["setWeaponFragments"](B["weaponId"], B["fragmentNum"]), Laya["Point"]["TEMP"]["x"] = this["enemy"]["width"] / 2, Laya["Point"]["TEMP"]["y"] = this["enemy"]["height"], this["enemy"]["localToGlobal"](Laya["Point"]["TEMP"]);
                          break;
                        case 5:
                          Laya["Point"]["TEMP"]["x"] = y["width"] / 2, Laya["Point"]["TEMP"]["y"] = y["height"] / 2, y["localToGlobal"](Laya["Point"]["TEMP"]);
                          break;
                        case 6:
                          A = sF["instance"]()["En"]("BattleScene")["getChildByName"]("box")["getChildByName"]("effectBox"), y = sF["instance"]()["En"]("BattleScene")["getChildByName"]("box")["getChildByName"]("xBtn");
                          break;
                        case 7:
                          E = Laya["Point"]["TEMP"]["x"], D = Laya["Point"]["TEMP"]["y"];
                          break;
                        case 8:
                          if (!this["Gm"]) return;
                          break;
                        case 9:
                          B = qx["instance"]()["tu"]();
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
                Y = 2;
                break;
              case C:
                Object["defineProperty"](c["prototype"], "update", {
                  ["value"](a) {
                    var b = hr;
                    switch (this["curState"]) {
                      case 0:
                        break;
                      case 1:
                        this["move"](a)
                    }
                    this["uw"]()
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = G;
                break;
              case H:
                Object["defineProperty"](c["prototype"], "Pw", {
                  ["value"]() {
                    var a = hr,
                      b = a[0],
                      c = a[3],
                      d = "floor",
                      e = "enemy",
                      f = "instance",
                      g = "map",
                      h = "ye",
                      i = "gridHei",
                      j = "Aw",
                      k = "Ew",
                      l = "event",
                      m = "id",
                      n = "Vy",
                      o = "Qy",
                      p = "nm";
                    let q, r;
                    q = Math["floor"]((this["enemy"]["x"] + this["enemy"]["width"] / 2) / uq["instance"]()["map"]["ye"]), r = Math["floor"]((this["enemy"]["y"] + this["enemy"]["height"] / 2) / uq["instance"]()["map"]["gridHei"]);
                    if (q == this["Aw"] && r == this["Ew"]) return;
                    if (this["rm"] && oc["instance"]["event"](sS["ft"], this["id"], this), this["Vy"]["x"] = this["enemy"]["x"], this["Vy"]["y"] = this["enemy"]["y"], this["Qy"]["x"] = q * uq["instance"]()["map"]["ye"], this["Qy"]["y"] = r * uq["instance"]()["map"]["gridHei"], np["bs"](this["Vy"], this["Qy"]) > 5) return;
                    this["Aw"], this["Ew"];
                    this["Am"] = this["Aw"], this["Em"] = this["Ew"], this["Aw"] = q, this["Ew"] = r, oc["instance"]["event"](sS["vt"], this["nm"], this["Aw"], this["Ew"]), oc["instance"]["event"](sS["_t"], this["nm"], this["Aw"], this["Ew"], this["id"])
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = r;
                break;
              case E:
                Object["defineProperty"](c["prototype"], "pos", {
                  ["value"](a, b) {
                    var c = hr;
                    return this["enemy"]["pos"](a, b)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = 7;
                break;
              case A:
                Object["defineProperty"](c["prototype"], "attack", {
                  ["value"]() {
                    var a = hr,
                      b = hu,
                      c = a[0],
                      d = a[3],
                      e = a[4],
                      f = b[1],
                      g = "timer",
                      h = "currTimer",
                      i = "Wm",
                      j = "path",
                      k = "length",
                      l = "enemy",
                      m = "height",
                      n = "sw",
                      o = "au";
                    if (Laya["timer"]["currTimer"] - this["Wm"] >= b[176]) {
                      if (!this["path"] || this["path"]["length"] < 2) return;
                      let d, e, p;
                      e = this["enemy"]["width"] / 2, this["path"][this["path"]["length"] - 1]["y"] - this["path"][this["path"]["length"] - 2]["y"] > 0 ? (p = this["enemy"]["height"] - f, d = b[194]) : (p = this["enemy"]["height"] - f, d = b[24]), qs["instance"]()["Qf"](this["enemy"], e, p, d), Laya["timer"]["once"](b[45], this, () => {
                        this["rm"] && (this["sw"]["au"]["Gi"] = !0, this["nm"] ? this["sw"]["au"]["Zi"] -= 1 : this["sw"]["au"]["Ki"] -= 1)
                      }), this["Wm"] = Laya["timer"]["currTimer"]
                    }
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = I;
                break;
              case J:
                Object["defineProperty"](c["prototype"], "Xw", {
                  ["value"](a, b, c) {},
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = u;
                break;
              case 0:
                Object["defineProperty"](c["prototype"], "Zm", {
                  ["get"]() {
                    var a = hr,
                      b = a[0];
                    return this["Km"] + this["fm"]
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true
                });
                Y = q;
                break;
              case 3:
                Object["defineProperty"](c["prototype"], "bw", {
                  ["value"](a) {
                    var b = hr;
                    return Math["min"](a + 1, this["path"]["length"] - 1)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = K;
                break;
              case 8:
                Object["defineProperty"](c["prototype"], "kw", {
                  ["value"]() {
                    var a = hr,
                      b = a[0],
                      c = "path",
                      d = "sw",
                      e = "map",
                      f = "Lm";
                    this["nm"] ? this["path"] = this["sw"]["map"]["Le"] : this["path"] = this["sw"]["map"]["me"], this["Lm"] && this["Lm"]
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = L;
                break;
              case M:
                Object["defineProperty"](c["prototype"], "Tw", {
                  ["value"](a) {
                    var b = hr,
                      c = hu,
                      d = b[0],
                      e = b[11],
                      f = b[3],
                      g = b[5],
                      h = b[4],
                      i = b[7],
                      j = c[12],
                      k = c[45],
                      l = "instance",
                      m = "timer",
                      n = "currTimer",
                      o = "Rm",
                      p = "Rw",
                      q = "floor",
                      r = "Uw",
                      s = "value",
                      t = "min",
                      u = "scale",
                      v = "damageNum",
                      w = "Point",
                      x = "TEMP",
                      y = "enemy",
                      z = "range";
                    let A, B, C, D, E;
                    if (!uq["instance"]()["Ry"]()["showDamageNum"]) return;
                    D = 1;
                    if (Laya["timer"]["currTimer"] - this["Rm"] < c[167]) return this["Rw"] = Math["floor"](this["Rw"] + a), this["Uw"]["value"] = this["Rw"]["toString"](), D = Math["min"](Math["floor"](this["Rw"] / 10), j), void this["Uw"]["scale"](1 + .05 * D, 1 + .05 * D);
                    this["Rm"] = Laya["timer"]["currTimer"];
                    C = rw["instance"]()["getItem"]("damageNum", this);
                    if (D = Math["min"](Math["floor"](a / 10), j), C["value"] = a["toFixed"](0), C["scale"](1 + .05 * D, 1 + .05 * D), oc["instance"]["event"](sS["Ut"], C, t1["kr"]), this["Cm"]) return;
                    Laya["Point"]["TEMP"]["x"] = this["enemy"]["width"] / 2, Laya["Point"]["TEMP"]["y"] = this["enemy"]["height"] / 2, this["enemy"]["localToGlobal"](Laya["Point"]["TEMP"]), C["parent"]["globalToLocal"](Laya["Point"]["TEMP"]), C["pos"](Laya["Point"]["TEMP"]["x"], Laya["Point"]["TEMP"]["y"]);
                    B = new Laya["Point"](C["x"], C["y"]), A = new Laya["Point"](C["x"] + np["range"](-k, k), C["y"] + np["range"](-c[112], -c[81])), E = new Laya["Point"](A["x"], C["y"]);
                    qs["instance"]()["ud"](B, A, E, C, c[176], () => {
                      C["alpha"] = 1, C["removeSelf"](), rw["instance"]()["recover"]("damageNum", C)
                    }), this["Uw"] = C, this["Rw"] = a
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = N;
                break;
              case I:
                Object["defineProperty"](c["prototype"], "back", {
                  ["value"](a, b) {
                    var c = hr,
                      d = c[0],
                      e = c[5],
                      f = c[4],
                      g = c[1],
                      h = c[3],
                      i = "jm",
                      j = "Lm",
                      k = "sw",
                      l = "map",
                      m = "path",
                      n = "max",
                      o = "enemy",
                      p = "min",
                      q = "abs",
                      r = "Tween";
                    let s, t, u, v, w, x, y, z, A;
                    if (this["jm"] || this["Cm"] || this["Lm"] < 1) return;
                    y = this["sw"]["map"]["ye"], w = this["sw"]["map"]["gridHei"], A = this["Lm"] - 1, t = this["path"][A], x = this["path"][this["Lm"]];
                    v = t["x"] * y, u = t["y"] * w;
                    s = Math["max"](0, y - this["enemy"]["width"] / 2 - 1), z = Math["max"](0, w - this["enemy"]["height"] / 2 - 1);
                    if (x["y"] !== t["y"]) u += Math["min"](Math["max"](0, b), z);
                    else {
                      if (x["x"] === t["x"]) return;
                      v += Math["min"](Math["max"](0, a), s)
                    }
                    Math["abs"](this["enemy"]["x"] - v) < 2 && Math["abs"](this["enemy"]["y"] - u) < 2 || (Laya["Tween"]["killAll"](this["enemy"]), this["jm"] = !0, Laya["Tween"]["to"](this["enemy"], {
                      ["x"]: v,
                      ["y"]: u
                    }, hu[45], Laya["Ease"]["cubicOut"], Laya["Handler"]["create"](this, () => {
                      this["enemy"]["pos"](v, u), this["Lm"] = A, this["jm"] = !1
                    }), 0, !0))
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = O;
                break;
              case w:
                Object["defineProperty"](c["prototype"], "rw", {
                  ["value"](a, b, c, d) {
                    var e = hr,
                      f = e[0],
                      g = e[3],
                      h = "ow",
                      i = "Point",
                      j = "lw",
                      k = "nm",
                      l = "se",
                      m = "ee",
                      n = "ye",
                      o = "ie",
                      p = "ae",
                      q = "enemy";
                    let r;
                    if (this["ow"] && -1 != this["ow"]["x"]) return;
                    r = this["sw"]["map"];
                    this["ow"] || (this["ow"] = new Laya["Point"], this["lw"] = new Laya["Point"]), this["ow"]["x"] = (this["nm"] ? r["se"]["x"] : r["ee"]["x"]) * r["ye"], this["ow"]["y"] = (this["nm"] ? r["se"]["y"] : r["ee"]["y"]) * r["ye"], this["lw"]["x"] = (this["nm"] ? r["ie"]["x"] : r["ae"]["x"]) * r["ye"] + this["enemy"]["width"] / 2, this["lw"]["y"] = (this["nm"] ? r["ie"]["y"] : r["ae"]["y"]) * r["ye"] + this["enemy"]["height"] / 2
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = B;
                break;
              case y:
                Object["defineProperty"](c["prototype"], "Ww", {
                  ["value"]() {
                    var a = hr,
                      b = a[0],
                      c = a[4],
                      d = a[3],
                      e = "sw",
                      f = "au",
                      g = "nm",
                      h = "enemy",
                      i = "width",
                      j = "Point",
                      k = "TEMP",
                      l = "Xm";
                    if ((this["sw"]["au"]["Ri"] && this["nm"] || this["sw"]["au"]["Ui"] && !this["nm"]) && Math["random"]() < .01) {
                      let d;
                      d = this["enemy"]["x"] < this["enemy"]["parent"]["width"] / 2 ? this["enemy"]["width"] : 0;
                      Laya["Point"]["TEMP"]["x"] = d, Laya["Point"]["TEMP"]["y"] = 0, this["enemy"]["localToGlobal"](Laya["Point"]["TEMP"]), qs["instance"]()["xg"](Laya["Point"]["TEMP"]["x"], Laya["Point"]["TEMP"]["y"], this["Xm"][np["range"](0, this["Xm"]["length"], !0)], this["enemy"])
                    }
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = F;
                break;
              case P:
                Object["defineProperty"](c["prototype"], "x", {
                  ["get"]() {
                    return this["enemy"]["x"]
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true
                });
                Y = D;
                break;
              case 10:
                Object["defineProperty"](c["prototype"], "hit", {
                  ["value"](a, b) {
                    var c = hr,
                      d = hu,
                      e = c[0],
                      f = c[6],
                      g = c[7],
                      h = c[3],
                      i = c[4],
                      j = c[2],
                      k = "Zi",
                      l = "instance",
                      m = "um",
                      n = "event",
                      o = "ew",
                      p = "width",
                      q = "Ym",
                      r = "indexOf",
                      s = "id",
                      t = "ht";
                    let u;
                    if (this["Zi"] <= 0) return;
                    u = nx["instance"]()["fa"];
                    (!this["um"] || u - this["um"] > d[45]) && (pC["instance"]()["playSound"]("enemy_hit"), this["um"] = u), this["Zi"] -= a, this["Zi"] < 0 && (this["Zi"] = 0), o7["instance"]()["im"](a, this["nm"]), this["Cw"](), this["enemy"]["event"]("onHit"), this["ew"]["width"] = this["aw"] * (this["Zi"] / this["Zm"]), Laya["Tween"]["create"](this["hw"])["to"]("width", this["ew"]["width"])["duration"](d[176])["ease"](Laya["Ease"]["linearIn"]), this["Tw"](a), this["Zi"] <= 0 && this["changeState"](4), b && (b instanceof vE ? (-1 == this["Ym"]["indexOf"](b["id"]) && this["Ym"]["push"](b["id"]), this["Zi"] <= 0 && (this["Ym"]["splice"](this["Ym"]["indexOf"](b["id"]), 1), oc["instance"]["event"](sS["ht"], b["id"], this["Ym"]))) : this["Zi"] <= 0 && oc["instance"]["event"](sS["ht"], b["id"], this["Ym"], !0))
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = M;
                break;
              case o:
                Object["defineProperty"](c["prototype"], "setState", {
                  ["value"](a, b, c) {
                    var d = hr,
                      e = d[0];
                    if (0 == a) this["Nm"] = b;
                    else if (5 == a) {
                      if (!b) return;
                      this["vm"] = !0, this["Sw"](c)
                    } else 4 == a ? (this["qm"] = b, b && this["hit"](c, null)) : 6 == a && (this["_m"] = b)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = j;
                break;
              case K:
                Object["defineProperty"](c["prototype"], "xw", {
                  ["value"]() {
                    var a = hr,
                      b = a[0],
                      c = a[2],
                      d = "enemy";
                    let e, f;
                    f = this["Dw"](), e = this["path"][f];
                    if (e) {
                      let f;
                      f = this["Iw"](this["enemy"], e);
                      f !== Laya["Vector2"]["ZERO"] && (this["enemy"]["x"] -= f["x"], this["enemy"]["y"] -= f["y"])
                    }
                    return f
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = Q;
                break;
              case 5:
                Object["defineProperty"](c["prototype"], "centerY", {
                  ["get"]() {
                    var a = hr,
                      b = "enemy";
                    return this["enemy"]["y"] + this["enemy"]["height"] / 2
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true
                });
                Y = P;
                break;
              case R:
                Object["defineProperty"](c["prototype"], "yw", {
                  ["value"]() {
                    var a = hr,
                      b = a[0],
                      c = "rm";
                    switch (this["curState"]) {
                      case 0:
                        this["rm"] = !1;
                        break;
                      case 1:
                        this["fw"]();
                        break;
                      case 2:
                        this["gw"]();
                        break;
                      case 3:
                        this["dw"]();
                        break;
                      case 4:
                        this["rm"] = !1, this["Lw"]()
                    }
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = t;
                break;
              case S:
                Object["defineProperty"](c["prototype"], "Zi", {
                  ["get"]() {
                    return this["mi"]
                  },
                  ["set"](a) {
                    var b = hr,
                      c = b[0];
                    this["mi"] = a, this["Qm"]["text"] = this["Zi"]["toFixed"](0)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true
                });
                Y = 0;
                break;
              case T:
                Object["defineProperty"](c["prototype"], "Sw", {
                  ["value"](a) {
                    var b = hr,
                      c = b[0],
                      d = "km",
                      e = "sw",
                      f = "map";
                    this["km"]["x"] += a["x"] * this["sw"]["map"]["ye"], this["km"]["y"] += a["y"] * this["sw"]["map"]["gridHei"]
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = U;
                break;
              case U:
                Object["defineProperty"](c["prototype"], "move", {
                  ["value"](a) {
                    var b = hr,
                      c = hu,
                      d = b[0],
                      e = c[123],
                      f = "Hm",
                      g = "Lm",
                      h = "vm",
                      i = "enemy",
                      j = "km",
                      k = "abs";
                    if (this["Hm"] = this["Lm"], !this["_m"])
                      if (this["vm"]) {
                        let f;
                        this["enemy"]["x"] += this["km"]["x"] * a / e, this["enemy"]["y"] += this["km"]["y"] * a / e, this["km"]["x"] *= .9, this["km"]["y"] *= .9;
                        f = this["xw"]();
                        Math["abs"](this["km"]["x"]) < .1 && Math["abs"](this["km"]["y"]) < .1 && (this["vm"] = !1, oc["instance"]["event"](sS["hs"], this, c[9]), this["Lm"] = this["bw"](f))
                      } else this["Nm"] || this["EL"](a);
                    this["Hm"] != this["Lm"] && this["Mw"](), this["Pw"]()
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = V;
                break;
              case G:
                Object["defineProperty"](c["prototype"], "changeState", {
                  ["value"](a) {
                    var b = hr,
                      c = b[0],
                      d = "curState";
                    this["curState"] != a && (this["pw"](), this["curState"] = a, this["yw"]())
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = R;
                break;
              case 2:
                "use strict";
                Y = S;
                break;
              case V:
                Object["defineProperty"](c["prototype"], "Mw", {
                  ["value"]() {
                    var a = hr,
                      b = a[0],
                      c = a[4],
                      d = "Lm",
                      e = "path",
                      f = "length",
                      g = "instance",
                      h = "event",
                      i = "nm",
                      j = "Cm";
                    this["Lm"] == this["path"]["length"] - 3 ? oc["instance"]["event"](sS["kt"], this["nm"]) : this["Lm"] == this["path"]["length"] - 2 ? oc["instance"]["event"](sS["wt"], this["nm"]) : this["Lm"] == this["path"]["length"] - 1 ? this["attack"]() : this["Lm"] >= this["path"]["length"] && (pC["instance"]()["playSound"]("enemy_knife_attack"), this["Cm"] || (this["Cm"] = !0, this["enemy"]["event"]("onDead")), this["gameOver"]())
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = H;
                break;
              case L:
                Object["defineProperty"](c["prototype"], "fw", {
                  ["value"]() {},
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = T;
                break;
              case Q:
                Object["defineProperty"](c["prototype"], "Iw", {
                  ["value"](a, b) {
                    var c = hr,
                      d = c[1],
                      e = c[2],
                      f = "sw",
                      g = "map",
                      h = "Point",
                      i = "TEMP",
                      j = "max",
                      k = "Vector2",
                      l = "ZERO",
                      m = "setValue";
                    let n, o, p, q, r, s, t, u, v, w, x;
                    r = this["sw"]["map"]["ye"], x = this["sw"]["map"]["gridHei"];
                    Laya["Point"]["TEMP"]["setTo"](b["x"] * r, b["y"] * x);
                    v = Laya["Point"]["TEMP"]["x"] - r / 2, o = Laya["Point"]["TEMP"]["x"] + r / 2, t = Laya["Point"]["TEMP"]["y"] - x / 2, s = Laya["Point"]["TEMP"]["y"] + x / 2, n = Math["max"](v - a["x"], 0), q = Math["max"](a["x"] - o, 0), u = Math["max"](t - a["y"], 0), w = Math["max"](a["y"] - s, 0);
                    if (0 === n && 0 === q && 0 === u && 0 === w) return Laya["Vector2"]["ZERO"];
                    p = Math["max"](n, q, u, w);
                    if (p === n) Laya["Vector2"]["TEMP"]["setValue"](-p, 0);
                    else if (p === q) Laya["Vector2"]["TEMP"]["setValue"](p, 0);
                    else if (p === u) Laya["Vector2"]["TEMP"]["setValue"](0, -p);
                    else {
                      if (p !== w) return Laya["Vector2"]["ZERO"];
                      Laya["Vector2"]["TEMP"]["setValue"](0, p)
                    }
                    return Laya["Vector2"]["TEMP"]
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = 1;
                break;
              case O:
                Object["defineProperty"](c["prototype"], "Ow", {
                  ["value"]() {
                    var a = hr,
                      b = hu,
                      c = a[0],
                      d = a[6],
                      e = a[2],
                      f = a[3],
                      g = a[4],
                      h = b[41],
                      i = b[33],
                      j = b[12],
                      k = b[81],
                      l = b[45],
                      m = b[123],
                      n = "Yw",
                      o = "enemy",
                      p = "mask",
                      q = "Tween",
                      r = "create",
                      s = "to",
                      t = "duration",
                      u = "chain",
                      v = "scaleY",
                      w = "delay",
                      x = "tw";
                    let y;
                    this["Yw"] || (this["Yw"] = new Laya["Sprite"], this["Yw"]["graphics"]["drawPoly"](0, 0, [b[69], -h, b[55], i, 2, i, -b[23], -h], "#fff")), this["Yw"]["pos"](j, 0), this["enemy"]["mask"] = this["Yw"];
                    y = this["enemy"]["y"];
                    Laya["Tween"]["create"](this["enemy"])["to"]("y", y + j)["duration"](k)["chain"]()["to"]("scaleY", .9)["duration"](l)["chain"]()["to"]("scaleY", 1)["duration"](l)["chain"]()["delay"](b[246])["to"]("y", y)["duration"](m)["onStart"](() => {
                      this["zm"] = qs["instance"]()["Kg"](this["tw"], b[22], m, () => {
                        this["tw"]["rotation"] = 0, this["changeState"](1)
                      })
                    }, this), Laya["Tween"]["create"](this["Yw"])["to"]("y", -j)["duration"](k)["chain"]()["to"]("y", j)["duration"](m)["delay"](b[247])["then"](() => {
                      this["enemy"]["mask"] = null
                    }, this)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = J;
                break;
              case N:
                Object["defineProperty"](c["prototype"], "dw", {
                  ["value"]() {},
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                Y = z;
                break;
              default:
                break
            }
          }
        } ["bind"](c)();
        return c
      } ["bind"](this)["apply"]();
