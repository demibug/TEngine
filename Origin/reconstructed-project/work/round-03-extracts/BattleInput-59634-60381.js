        } ["bind"](this)["apply"](), ph = function() {
          var a = hr;
          let b;
          b = class extends qU {
            constructor() {
              var a = hr,
                b = a[0],
                rsb = "xH",
                rsc = "containerType";
              var c;
              c = arguments;
              super(...c), this["bb"] = new Laya["Point"], this["offsetY"] = 10, this["XH"] = !1, this["GH"] = 0, this["HH"] = 0, this["WH"] = -1, this["jH"] = -1, this["zH"] = -1, this["NH"] = -1, this["qH"] = 0, this["$H"] = 0, this["VH"] = 0, this["QH"] = 0, this["ZH"] = [], this["KH"] = [], this["ow"] = {
                ["xH"]: 0,
                ["containerType"]: 0,
                ["x"]: 0,
                ["y"]: 0
              }, this["pg"] = {
                ["xH"]: 0,
                ["containerType"]: 0,
                ["x"]: 0,
                ["y"]: 0
              }, this["JH"] = !1, this["tW"] = []
            }
          };
          ! function() {
            var a = hr,
              c = hu,
              d = a[7],
              e = a[0],
              f = a[9],
              g = "defineProperty",
              rsi = "value",
              rsj = "enumerable",
              rsk = "configurable",
              rsl = "writable";
            for (let h of l1) {
              if (-1 == h) {} else if (0 == h) {
                Object["defineProperty"](b["prototype"], "onMouseUp", {
                  ["value"](a) {
                    var b = hr,
                      c = b[0],
                      d = b[4],
                      e = "pg",
                      f = "sW",
                      g = "ow",
                      h = "xH",
                      i = "instance";
                    this["aW"](this["pg"], a["stageX"], a["stageY"]), this["sW"] && this["sW"]["onMouseUp"](this["pg"]), this["JH"] && 1 === this["ow"]["xH"] && 1 === this["pg"]["xH"] && this["ow"]["x"] === this["pg"]["x"] && this["ow"]["y"] === this["pg"]["y"] && uq["instance"]()["map"]["Te"](this["ow"]["x"], this["ow"]["y"]) && qs["instance"]()["Ed"](), this["cW"](), this["IH"] = null
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (1 == h) {
                Object["defineProperty"](b["prototype"], "PW", {
                  ["value"](a) {
                    var b = hr,
                      c = b[0],
                      d = b[1],
                      e = b[3],
                      f = "pe",
                      g = "instance",
                      h = "nm",
                      i = "length",
                      j = "1_0",
                      k = "1_1",
                      l = "P_",
                      m = "level",
                      n = "id",
                      o = "S_",
                      p = "skin",
                      q = "alpha",
                      r = "resources/img/battleUI/dragBg2.png";
                    let s, t, u, v, w;
                    var x;
                    w = this["pe"]["pe"];
                    s = this["IH"], v = na["instance"]()["ub"](1, null == (x = null == s ? void 0 : s["nm"]) || x), t = null == v ? void 0 : v["sb"];
                    for (let e = 0; e < w["length"]; e++)
                      for (let f = 0; f < w[e]["length"]; f++) {
                        let i, v;
                        if (u = this["ZH"][e][f], !u) continue;
                        v = !1;
                        i = !s || s["nm"];
                        if (v = a ? i ? "1_0" == w[e][f] || "2_0" == w[e][f] : "1_1" == w[e][f] || "2_1" == w[e][f] : i ? "1_0" == w[e][f] : "1_1" == w[e][f], u["visible"] = v, v && s && t && t[e] && t[e][f]) {
                          let a, d;
                          a = t[e][f];
                          d = !1;
                          if (s instanceof td && a instanceof td) d = s["P_"] === a["P_"] && s["level"] === a["level"] && s["id"] !== a["id"] && a["level"] < 5 && !s["S_"] && !a["S_"];
                          else if (s instanceof qo && a instanceof qo) {
                            let e;
                            e = vc["instance"]()["rE"](s, a);
                            d = null !== e && e["level"] < e["maxLevel"]
                          } else s instanceof om && a instanceof om && (d = s["P_"] === a["P_"] && s["level"] === a["level"] && s["id"] !== a["id"] && a["level"] < 5 && !s["S_"] && !a["S_"]);
                          d ? (u["skin"] = "resources/img/battleUI/dragBg3.png", u["alpha"] = 1) : (u["skin"] = "resources/img/battleUI/dragBg2.png", u["alpha"] = .8)
                        } else u["skin"] = "resources/img/battleUI/dragBg2.png", u["alpha"] = .8
                      }
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (2 == h) {
                Object["defineProperty"](b["prototype"], "_W", {
                  ["value"](a) {
                    var b = hr,
                      c = b[0],
                      d = "vW";
                    na["instance"]()["ub"](3)["sb"][a] instanceof oe ? this["vW"](!1) : this["vW"](!0, 2, a)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (3 == h) {
                Object["defineProperty"](b["prototype"], "cW", {
                  ["value"]() {
                    var a = hr,
                      b = a[0],
                      c = "XH",
                      d = "visible";
                    (this["XH"] || this["IH"]) && (this["pW"]["visible"] = !1, this["yW"]["visible"] = !1, this["fW"]["visible"] = !1, this["bW"](), this["gW"]["visible"] = !1, this["dW"]["visible"] = !1, this["LW"]["visible"] = !1, this["XH"] = !1, this["GH"] = 0, this["WH"] = -1, this["jH"] = -1, this["HH"] = 0, this["zH"] = -1, this["NH"] = -1)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (4 == h) {
                Object["defineProperty"](b["prototype"], "BW", {
                  ["value"]() {
                    var a = hr,
                      b = a[5],
                      c = "ZH",
                      d = "length";
                    for (let e = 0; e < this["ZH"]["length"]; e++)
                      for (let f = 0; f < this["ZH"][e]["length"]; f++) this["ZH"][e][f] && (this["ZH"][e][f]["removeSelf"](), rw["instance"]()["recover"]("dragTip", this["ZH"][e][f]), this["ZH"][e][f] = null)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (5 == h) {
                Object["defineProperty"](b["prototype"], "init", {
                  ["value"]() {
                    var a = hr,
                      b = a[0],
                      c = "pe";
                    this["pe"] = uq["instance"]()["map"], this["qH"] = this["pe"]["ye"], this["$H"] = this["pe"]["gridHei"]
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (6 == h) {
                Object["defineProperty"](b["prototype"], "EW", {
                  ["value"](a) {
                    var b = hr,
                      c = b[0],
                      rs1 = "containerType",
                      d = "pe",
                      e = "Wb",
                      f = "length",
                      g = "instance",
                      h = "WY",
                      i = "visible";
                    let j, k, l, m, n;
                    n = {
                      ["x"]: 0,
                      ["y"]: 0,
                      ["containerType"]: 2
                    };
                    k = this["pe"]["pe"];
                    j = 3 == a["Wb"] || 4 == a["Wb"] ? 1 : 2, n["containerType"] = j;
                    for (let b = 0; b < k["length"]; b++)
                      for (let d = 0; d < k[b]["length"]; d++)
                        if (l = this["ZH"][b][d], l)
                          if (n["x"] = b, n["y"] = d, 4 == a["Wb"]) {
                            let b, c;
                            n["containerType"] = 1;
                            c = r0["instance"]()["WY"](a, n);
                            n["containerType"] = 2;
                            b = r0["instance"]()["WY"](a, n);
                            l["visible"] = c || b
                          } else l["visible"] = r0["instance"]()["WY"](a, n);
                    n["containerType"] = 3;
                    m = na["instance"]()["ub"](3)["sb"];
                    for (let b = 0; b < m["length"]; b++) l = this["KH"][b], l && (n["x"] = b, n["y"] = 0, l["visible"] = r0["instance"]()["WY"](a, n))
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (7 == h) {
                Object["defineProperty"](b["prototype"], "onMouseMove", {
                  ["value"](a) {
                    var b = hr,
                      c = b[0],
                      d = b[4],
                      e = "sW";
                    if (this["IH"] && this["sW"]) {
                      let f;
                      f = this["sW"]["onMouseMove"](a);
                      f && !this["XH"] && this["oW"](f), this["lW"](a["stageX"], a["stageY"])
                    }
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (8 == h) {
                Object["defineProperty"](b["prototype"], "bW", {
                  ["value"]() {
                    var a = hr,
                      b = a[3],
                      c = a[0],
                      d = "Tween",
                      e = "killAll",
                      f = "pW",
                      g = "yW",
                      h = "fW",
                      i = "alpha";
                    Laya["Tween"]["killAll"](this["pW"]), Laya["Tween"]["killAll"](this["yW"]), Laya["Tween"]["killAll"](this["fW"]), this["pW"]["alpha"] = .8, this["yW"]["alpha"] = .8, this["fW"]["alpha"] = .4
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (9 == h) {
                Object["defineProperty"](b["prototype"], "kW", {
                  ["value"]() {
                    var a = hr,
                      b = hu,
                      c = a[0],
                      d = a[3],
                      e = b[123],
                      f = "pW",
                      rtp = "alpha",
                      g = "Tween",
                      h = "to";
                    let i;
                    i = .8;
                    this["pW"]["alpha"] > .4 && (i = 0), Laya["Tween"]["to"](this["pW"], {
                      ["alpha"]: i
                    }, e), Laya["Tween"]["to"](this["yW"], {
                      ["alpha"]: i
                    }, e), Laya["Tween"]["to"](this["fW"], {
                      ["alpha"]: i / 2
                    }, e, null, Laya["Handler"]["create"](this, () => {
                      this["kW"]()
                    }))
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (10 == h) {
                Object["defineProperty"](b["prototype"], "xW", {
                  ["value"]() {
                    var a = hr,
                      b = "IH";
                    return this["IH"] ? this["IH"]["am"]() : null
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              }
              if (c[7] == h) {
                Object["defineProperty"](b["prototype"], "iW", {
                  ["value"]() {
                    var a = hr,
                      b = hu,
                      c = a[0],
                      d = a[8],
                      e = "tW",
                      f = "push",
                      g = "propsBox",
                      h = "getChildAt",
                      i = "width",
                      j = "numChildren",
                      k = "height",
                      l = "propsBoxAi";
                    let m;
                    this["tW"]["length"] = 0, this["tW"]["push"](new ob(this["refreshBox"], 3, 2, this["VH"], this["QH"])), this["tW"]["push"](new oi(this["map"], 1, 1, this["qH"], this["$H"], this["offsetY"]));
                    m = this["DG"]["getChildByName"]("refreshBtn");
                    this["tW"]["push"](new sf(this["propsBox"], 4, 3, m, this["propsBox"]["getChildAt"](0)["width"], this["propsBox"]["getChildAt"](this["propsBox"]["numChildren"] - 1)["width"], this["propsBox"]["getChildAt"](this["propsBox"]["numChildren"] - 1)["y"], this["propsBox"]["getChildAt"](this["propsBox"]["numChildren"] - 1)["height"])), this["tW"]["push"](new sV(this["propsBoxAi"], 4, 4, b[210], b[65], this["propsBoxAi"]["getChildAt"](0)["height"]))
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (c[9] == h) {
                Object["defineProperty"](b["prototype"], "oW", {
                  ["value"](a) {
                    var b = hr,
                      c = b[0],
                      d = b[5],
                      e = b[11],
                      f = "XH",
                      g = "visible",
                      h = "gW",
                      i = "bb",
                      j = "parent";
                    !this["XH"] && a && (this["pW"]["visible"] = !0, this["yW"]["visible"] = !0, this["fW"]["visible"] = !0, this["kW"](), this["gW"]["visible"] = !0, this["bb"]["x"] = a["x"] + a["width"] * (.5 - a["anchorX"]), this["bb"]["y"] = a["y"] + a["height"] * (.5 - a["anchorY"]), this["bb"] = a["parent"]["localToGlobal"](this["bb"]), this["bb"] = this["gW"]["parent"]["globalToLocal"](this["bb"]), this["gW"]["pos"](this["bb"]["x"], this["bb"]["y"]), this["SW"](), this["XH"] = !0)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (c[3] == h) {
                Object["defineProperty"](b["prototype"], "mW", {
                  ["value"](a) {
                    var b = hr,
                      c = hu,
                      d = b[0],
                      e = b[3],
                      f = b[5],
                      g = b[6],
                      h = b[1],
                      i = b[4],
                      j = b[8],
                      k = "GH",
                      l = "HH",
                      m = "WH",
                      n = "zH",
                      o = "jH",
                      p = "NH",
                      q = "map",
                      r = "hitTestPoint",
                      s = "stageX",
                      t = "stageY",
                      u = "floor",
                      v = "mouseX",
                      w = "soldier",
                      x = "nW",
                      y = "wW",
                      z = "farmer",
                      A = "rW",
                      B = "Wb",
                      C = "instance",
                      D = "WY",
                      rug = "containerType",
                      E = "vW",
                      F = "refreshBox",
                      G = "dW",
                      H = "visible",
                      I = "LW",
                      J = "gW",
                      K = "pos",
                      L = "getChildAt";
                    let M, N, O, P, Q, R, S;
                    if (this["GH"] = this["HH"], this["WH"] = this["zH"], this["jH"] = this["NH"], this["map"]["hitTestPoint"](a["stageX"], a["stageY"])) {
                      let a, c;
                      c = Math["floor"](this["map"]["mouseX"] / this["qH"]), a = Math["floor"]((this["map"]["mouseY"] - this["offsetY"]) / this["$H"]);
                      if (this["HH"] = 1, this["zH"] = c, this["NH"] = a, this["GH"] == this["HH"] && this["WH"] == this["zH"] && this["jH"] == this["NH"]) return;
                      if ("soldier" == this["nW"]) this["wW"](!1, c, a);
                      else if ("farmer" == this["nW"]) this["wW"](!0, c, a);
                      else if (this["rW"]) {
                        let b;
                        b = 3 == this["rW"]["Wb"] || 4 == this["rW"]["Wb"] ? 1 : 2, r0["instance"]()["WY"](this["rW"], {
                          ["containerType"]: b,
                          ["x"]: this["zH"],
                          ["y"]: this["NH"]
                        }) ? this["vW"](!0, 1, c, a) : this["vW"](!1)
                      }
                    } else if (this["refreshBox"]["hitTestPoint"](a["stageX"], a["stageY"])) {
                      let a;
                      a = Math["floor"](this["refreshBox"]["mouseX"] / this["VH"]);
                      if (this["HH"] = 2, this["zH"] = a, this["NH"] = 0, this["GH"] == this["HH"] && this["WH"] == this["zH"] && this["jH"] == this["NH"]) return;
                      "soldier" == this["nW"] || "farmer" == this["nW"] ? this["_W"](a) : this["rW"] && (r0["instance"]()["WY"](this["rW"], {
                        ["containerType"]: 3,
                        ["x"]: a,
                        ["y"]: 0
                      }) ? this["vW"](!0, 2, a) : this["vW"](!1))
                    } else this["HH"] = 0, this["dW"]["visible"] = !1;
                    if (!this["dW"]["visible"]) return this["LW"]["visible"] = !1, this["HH"] = 0, this["zH"] = -1, void(this["NH"] = -1);
                    N = this["gW"], M = this["dW"], P = np["bs"](N, M), Q = Math["floor"](P / c[19]), O = np["angle"](N, M);
                    this["LW"]["rotation"] = O - c[88], this["LW"]["pos"](this["gW"]["x"], this["gW"]["y"]), this["LW"]["visible"] = !0;
                    R = this["LW"]["numChildren"];
                    if (R >= Q)
                      for (let a = 0; a < R; a++) this["LW"]["getChildAt"](a)["visible"] = a < Q;
                    else
                      for (let a = 0; a < Q; a++) a < R ? this["LW"]["getChildAt"](a)["visible"] = !0 : (S = rw["instance"]()["getItem"]("dragLine", this), S["pos"](a * (S["width"] + 5), 0), this["LW"]["addChild"](S))
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (c[14] == h) {
                Object["defineProperty"](b["prototype"], "wW", {
                  ["value"](a, b, c) {
                    var d = hr,
                      e = d[0],
                      f = d[1],
                      g = "pe",
                      h = "1_0",
                      i = "1_1",
                      j = "vW";
                    let k, l, m;
                    l = this["pe"]["pe"][b][c], k = this["IH"], m = !k || k["nm"];
                    let n;
                    n = a ? m ? "1_0" === l || "2_0" === l : "1_1" === l || "2_1" === l : m ? "1_0" === l : "1_1" === l, n ? this["vW"](!0, 1, b, c) : this["vW"](!1)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (c[12] == h) {
                Object["defineProperty"](b["prototype"], "MW", {
                  ["value"](a, b) {},
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (c[11] == h) {
                Object["defineProperty"](b["prototype"], "onMouseDown", {
                  ["value"](a) {
                    var b = hr,
                      c = b[4],
                      d = b[0],
                      e = "ow",
                      f = "sW",
                      g = "IH",
                      h = "nW",
                      i = "rW";
                    this["aW"](this["ow"], a["stageX"], a["stageY"]), this["sW"] ? (this["IH"] = this["sW"]["onMouseDown"](this["ow"]), this["IH"] ? this["IH"] instanceof rc ? (this["IH"] instanceof om ? this["nW"] = "farmer" : this["nW"] = "soldier", this["rW"] = null) : this["IH"] instanceof rD && (this["nW"] = this["IH"]["type"], this["rW"] = this["IH"]) : (this["nW"] = null, this["rW"] = null)) : (this["IH"] = null, this["nW"] = null, this["rW"] = null)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              }
              if (c[13] == h) {
                Object["defineProperty"](b["prototype"], "AW", {
                  ["value"]() {
                    var a = hr,
                      b = a[0],
                      c = a[3],
                      d = "instance",
                      e = "P_",
                      f = "level",
                      g = "id",
                      h = "S_",
                      i = "skin",
                      j = "alpha",
                      k = "resources/img/battleUI/dragBg2.png";
                    let l, m, n;
                    m = na["instance"]()["ub"](3)["sb"];
                    n = this["IH"];
                    for (let c = 0; c < m["length"]; c++) {
                      let o;
                      if (l = this["KH"][c], !l) continue;
                      o = !(m[c] instanceof oe);
                      if (l["visible"] = o, o && n && m[c] instanceof rc) {
                        let o, p;
                        p = m[c];
                        o = !1;
                        if (n instanceof td && p instanceof td) o = n["P_"] === p["P_"] && n["level"] === p["level"] && n["id"] !== p["id"] && p["level"] < 5 && !n["S_"] && !p["S_"];
                        else if (n instanceof qo && p instanceof qo) {
                          let c;
                          c = vc["instance"]()["rE"](n, p);
                          o = null !== c && c["level"] < c["maxLevel"]
                        } else n instanceof om && p instanceof om && (o = n["P_"] === p["P_"] && n["level"] === p["level"] && n["id"] !== p["id"] && p["level"] < 5 && !n["S_"] && !p["S_"]);
                        o ? (l["skin"] = "resources/img/battleUI/dragBg3.png", l["alpha"] = 1) : (l["skin"] = "resources/img/battleUI/dragBg2.png", l["alpha"] = .8)
                      } else l["skin"] = "resources/img/battleUI/dragBg2.png", l["alpha"] = .8
                    }
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (c[5] == h) {
                Object["defineProperty"](b["prototype"], "SW", {
                  ["value"]() {
                    var a = hr,
                      b = a[0],
                      c = "nW",
                      d = "PW",
                      e = "AW",
                      f = "rW";
                    "soldier" == this["nW"] ? (this["PW"](!1), this["AW"]()) : "farmer" == this["nW"] ? (this["PW"](!0), this["AW"]()) : this["rW"] && this["EW"](this["rW"])
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (c[4] == h) {
                Object["defineProperty"](b["prototype"], "gameOver", {
                  ["value"]() {
                    var a = hr,
                      b = a[0],
                      c = a[8],
                      d = a[9],
                      e = a[6],
                      f = "DG",
                      g = "off",
                      h = "Event",
                      i = "visible";
                    this["JH"] && (this["DG"]["off"](Laya["Event"]["MOUSE_DOWN"], this, this["onMouseDown"]), this["DG"]["off"](Laya["Event"]["MOUSE_MOVE"], this, this["onMouseMove"]), this["DG"]["off"](Laya["Event"]["MOUSE_UP"], this, this["onMouseUp"]), this["bW"](), this["pW"]["visible"] = !1, this["yW"]["visible"] = !1, this["gW"]["visible"] = !1, this["dW"]["visible"] = !1, this["LW"]["visible"] = !1), this["XH"] = !1, this["GH"] = 0, this["WH"] = -1, this["jH"] = -1, this["HH"] = 0, this["zH"] = -1, this["NH"] = -1, uq["instance"]()["map"]["Re"]()
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (c[1] == h) {
                Object["defineProperty"](b["prototype"], "startGame", {
                  ["value"]() {
                    var a = hr,
                      b = a[0],
                      c = a[9],
                      d = a[8],
                      e = a[6],
                      f = "sW",
                      g = "instance",
                      h = "DG",
                      i = "getChildByName",
                      j = "map",
                      k = "refreshBox",
                      l = "propsBox",
                      m = "propsBoxAi",
                      n = "on",
                      o = "Event";
                    this["JH"] = !0, this["sW"] = sO["instance"](), this["sW"] && this["sW"]["init"](), this["DG"] = sF["instance"]()["En"]("BattleScene")["getChildByName"]("box"), this["map"] = this["DG"]["getChildByName"]("map"), this["refreshBox"] = this["DG"]["getChildByName"]("refreshBox"), this["propsBox"] = this["DG"]["getChildByName"]("propsBox"), this["propsBoxAi"] = this["DG"]["getChildByName"]("propsBoxAi"), this["VH"] = this["refreshBox"]["width"] / this["pe"]["fe"], this["QH"] = this["refreshBox"]["height"], this["iW"](), this["hW"](), this["eW"](), this["DG"]["on"](Laya["Event"]["MOUSE_DOWN"], this, this["onMouseDown"]), this["DG"]["on"](Laya["Event"]["MOUSE_MOVE"], this, this["onMouseMove"]), this["DG"]["on"](Laya["Event"]["MOUSE_UP"], this, this["onMouseUp"])
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (c[10] == h) {
                Object["defineProperty"](b["prototype"], "vW", {
                  ["value"](a, b, c, d) {
                    var e = hr,
                      f = e[0],
                      g = e[5],
                      h = e[3],
                      i = e[1],
                      j = e[11],
                      k = "dW",
                      l = "width",
                      m = "qH",
                      n = "instance",
                      o = "bb",
                      p = "$H",
                      q = "map",
                      r = "localToGlobal",
                      s = "parent",
                      t = "globalToLocal",
                      u = "pos",
                      v = "IH",
                      w = "Point",
                      x = "TEMP",
                      y = "wg",
                      z = "VH";
                    if (this["dW"]["visible"] = a, this["dW"]["width"] = this["qH"] + 7, a)
                      if (1 == b) {
                        let a, b;
                        b = vc["instance"]()["pE"](c, d);
                        a = this["qH"] + 7;
                        if (-1 != b["x"] && -1 != b["y"] && (c = b["x"], d = b["y"], a += this["qH"], this["dW"]["width"] = a), this["bb"]["x"] = c * this["qH"] + this["qH"] / 2, this["bb"]["y"] = d * this["$H"] + this["$H"] / 2, this["map"]["localToGlobal"](this["bb"]), this["dW"]["parent"]["globalToLocal"](this["bb"]), this["dW"]["pos"](this["bb"]["x"], this["bb"]["y"]), "soldier" == this["nW"] && this["IH"]) {
                          let a, b;
                          b = this["IH"]["am"]();
                          a = vc["instance"]()["PA"]["get"](Number(b["name"]["split"]("_")[1]));
                          a && (Laya["Point"]["TEMP"]["setTo"](c * this["qH"] + this["qH"] / 2, d * this["$H"] + this["$H"] / 2), this["map"]["localToGlobal"](Laya["Point"]["TEMP"]), qs["instance"]()["wg"](!0, a["wp"], Laya["Point"]["TEMP"]["x"], Laya["Point"]["TEMP"]["y"]))
                        }
                      } else 2 == b && (this["bb"]["x"] = c * this["VH"] + this["VH"] / 2, this["bb"]["y"] = this["QH"] / 2, this["refreshBox"]["localToGlobal"](this["bb"]), this["dW"]["parent"]["globalToLocal"](this["bb"]), this["dW"]["pos"](this["bb"]["x"], this["bb"]["y"]));
                    else qs["instance"]()["wg"](!1)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (c[2] == h) {
                Object["defineProperty"](b["prototype"], "eW", {
                  ["value"]() {
                    var a = hr,
                      b = a[1],
                      c = a[0],
                      d = "pe",
                      e = "length",
                      f = "ZH",
                      g = "visible";
                    let h;
                    h = this["pe"]["pe"];
                    for (let a = 0; a < h["length"]; a++)
                      for (let c = 0; c < h[a]["length"]; c++) "1_0" == h[a][c] || "2_0" == h[a][c] ? this["ZH"][a][c - 0]["visible"] = !0 : this["ZH"][a][c - 0]["visible"] = !1
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              }
              if (c[8] == h) {
                Object["defineProperty"](b["prototype"], "aW", {
                  ["value"](a, b, c) {
                    var d = hr,
                      e = d[0],
                      f = "xH",
                      g = "containerType";
                    let h;
                    h = this["IH"] instanceof rc;
                    for (const d of this["tW"]) {
                      let i;
                      if (h && (3 === d["xH"] || 4 === d["xH"])) continue;
                      i = d["bH"](b, c);
                      if (null !== i) return a["xH"] = d["xH"], a["containerType"] = d["containerType"], a["x"] = i["x"], void(a["y"] = i["y"])
                    }
                    a["xH"] = 0, a["containerType"] = 0, a["x"] = 0, a["y"] = 0
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (c[6] == h) {
                "use strict";
                continue
              } else if (c[19] == h) {
                Object["defineProperty"](b["prototype"], "uW", {
                  ["value"](a, b, c) {
                    var d = hr,
                      e = hu,
                      f = d[6],
                      g = d[5],
                      h = d[0],
                      i = d[3],
                      j = e[24],
                      k = "pe";
                    let l;
                    l = rw["instance"]()["getItem"]("dragTip", this);
                    return l["size"](j, j), l["pos"](a * this["qH"] + (this["pe"]["ye"] - l["width"]) / 2, (b - Math["floor"](c / 2)) * this["$H"] + (this["pe"]["gridHei"] - l["height"]) / 2), l["alpha"] = .8, l["visible"] = !1, this["pW"]["addChild"](l), l
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (c[57] == h) {
                Object["defineProperty"](b["prototype"], "hW", {
                  ["value"]() {
                    var a = hr,
                      b = hu,
                      c = a[0],
                      d = a[5],
                      e = a[6],
                      f = a[33],
                      g = a[4],
                      h = a[2],
                      i = a[1],
                      j = a[3],
                      k = b[65],
                      l = "pW",
                      m = "Sprite",
                      n = "name",
                      o = "size",
                      p = "map",
                      q = "width",
                      r = "height",
                      s = "pos",
                      t = "visible",
                      u = "instance",
                      v = "event",
                      w = "Ut",
                      x = "Rr",
                      y = "pe",
                      z = "length",
                      A = "ZH",
                      B = "push",
                      C = "yW",
                      D = "refreshBox",
                      E = "VH",
                      F = "alpha",
                      G = "fW",
                      H = "gW",
                      I = "Image",
                      J = "anchorX",
                      K = "anchorY",
                      L = "dW",
                      M = "LW";
                    if (!this["pW"]) {
                      let b;
                      this["pW"] = new Laya["Sprite"], this["pW"]["name"] = "dragMaskMap", this["pW"]["size"](this["map"]["width"], this["map"]["height"] / 2), this["pW"]["pos"](this["map"]["x"], this["map"]["y"] + this["map"]["height"] / 2), this["pW"]["visible"] = !1, oc["instance"]["event"](sS["Ut"], this["pW"], t1["Rr"]);
                      b = this["pe"]["pe"];
                      for (let a = 0; a < b["length"]; a++) {
                        this["ZH"]["push"]([]);
                        for (let d = 0; d < b[a]["length"]; d++) {
                          let e;
                          e = this["uW"](a, d, b[a]["length"]);
                          this["ZH"][a]["push"](e)
                        }
                      }
                    }
                    if (!this["yW"]) {
                      this["yW"] = new Laya["Sprite"], this["yW"]["name"] = "dragMaskRefresh", this["yW"]["size"](this["refreshBox"]["width"], this["refreshBox"]["height"]), this["yW"]["pos"](this["refreshBox"]["x"], this["refreshBox"]["y"]), this["yW"]["visible"] = !1, oc["instance"]["event"](sS["Ut"], this["yW"], t1["Rr"]);
                      for (let a = 0; a < this["pe"]["fe"]; a++) {
                        let b;
                        b = rw["instance"]()["getItem"]("dragTip", this);
                        b["size"](k, k), b["pos"](a * this["VH"] + (this["VH"] - b["width"]) / 2, (this["QH"] - b["height"]) / 2), b["alpha"] = .8, b["visible"] = !1, this["yW"]["addChild"](b), this["KH"]["push"](b)
                      }
                    }
                    this["fW"] || (this["fW"] = new Laya["Sprite"], this["fW"]["name"] = "dragMapTipSp", this["fW"]["pos"](this["map"]["x"], this["map"]["y"]), this["fW"]["alpha"] = .4, oc["instance"]["event"](sS["Ut"], this["fW"], t1["Rr"])), this["fW"]["visible"] = !1, this["gW"] || (this["gW"] = new Laya["Image"]("resources/img/battleUI/dragImg1.png"), this["gW"]["size"](b[0], b[79]), this["gW"]["anchorX"] = .5, this["gW"]["anchorY"] = .5, this["gW"]["visible"] = !1, oc["instance"]["event"](sS["Ut"], this["gW"], t1["Rr"])), this["dW"] || (this["dW"] = new Laya["Image"]("resources/img/battleUI/dragImg2.png"), this["dW"]["size"](b[72], b[70]), this["dW"]["sizeGrid"] = "13,13,13,13,0", this["dW"]["anchorX"] = .5, this["dW"]["anchorY"] = .5, this["dW"]["visible"] = !1, oc["instance"]["event"](sS["Ut"], this["dW"], t1["Rr"])), this["LW"] || (this["LW"] = new Laya["Sprite"], this["LW"]["visible"] = !1, oc["instance"]["event"](sS["Ut"], this["LW"], t1["Rr"]))
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (c[37] == h) {
                Object["defineProperty"](b["prototype"], "lW", {
                  ["value"](a, b) {
                    var c = hr,
                      d = c[0],
                      e = c[4];
                    this["IH"] && this["XH"] && this["mW"]({
                      ["stageX"]: a,
                      ["stageY"]: b
                    })
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
