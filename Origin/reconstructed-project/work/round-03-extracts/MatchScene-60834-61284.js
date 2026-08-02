        } = Laya), nd = function() {
          var a = hr;
          let b;
          b = class extends rg {
            constructor() {
              var a = hr,
                b = hu,
                c = a[0];
              var d;
              d = arguments;
              super(...d), this["bb"] = new Laya["Point"](0, 0), this["VZ"] = [], this["QZ"] = !1, this["ZZ"] = [
                [],
                [],
                [],
                []
              ], this["KZ"] = [0, 0], this["JZ"] = b[267], this["tK"] = b[122], this["sK"] = 6, this["iK"] = b[132], this["hK"] = !1, this["eK"] = 0, this["aK"] = !1, this["nK"] = 0
            }
          };
          ! function() {
            var a = hr,
              c = hu,
              d = a[6],
              e = a[0],
              f = "defineProperty",
              ryx = "value",
              ryy = "enumerable",
              ryz = "configurable",
              ryA = "writable";
            for (let g of m2) {
              if (-1 == g) {} else if (0 == g) {
                Object["defineProperty"](b["prototype"], "onOpened", {
                  ["value"]() {
                    var a = hr,
                      b = a[3],
                      c = "JZ";
                    let d;
                    d = (this["width"] - hu[61]) / 6;
                    this["JZ"] = Math["min"](d, this["JZ"]), this["init"](), this["reset"](), nx["instance"]()["La"]("MatchScene", this, this["update"])
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (1 == g) {
                "use strict";
                continue
              } else if (2 == g) {
                Object["defineProperty"](b["prototype"], "init", {
                  ["value"]() {
                    var a = hr,
                      b = hu,
                      c = a[5],
                      d = a[0],
                      e = a[2],
                      f = a[3],
                      g = "redAvatar",
                      h = "instance",
                      i = "text",
                      j = "rank",
                      k = "player";
                    let l, m;
                    this["redAvatar"]["texture"] = null, tR["instance"]()["_u"](this["redAvatar"]);
                    l = uq["instance"]();
                    this["redRank"]["text"] = l["rank"]["yu"]["rank"];
                    m = l["player"]["roundDay"] - 1;
                    this["redWinRate"]["text"] = m > 0 ? (l["player"]["winDay"] / m * b[81])["toFixed"](1) + "%" : "0.0%", this["title"]["text"] = "匹配中", Laya["timer"]["once"](b[45], this, () => {
                      this["rK"]()
                    })
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (3 == g) {
                Object["defineProperty"](b["prototype"], "update", {
                  ["value"](a) {
                    var b = hr,
                      c = b[0],
                      d = b[5],
                      e = "eK",
                      f = "PI",
                      g = "skewX",
                      h = "sin",
                      i = "QZ",
                      j = "aK",
                      k = "nK";
                    if (this["hK"] && (this["eK"] += a / hu[123] * Math["PI"], this["flagBlue"]["skewX"] = 2 * Math["sin"](this["eK"]), this["flagRed"]["skewX"] = 2 * Math["sin"](this["eK"] + Math["PI"])), this["QZ"]) {
                      let a, e;
                      a = Laya["timer"]["currTimer"];
                      e = !0;
                      this["ZZ"]["forEach"]((d, f) => {
                        var g = hr,
                          h = hu,
                          i = g[3],
                          j = g[0],
                          k = g[2],
                          l = g[11],
                          m = g[10],
                          n = g[4],
                          o = h[43],
                          p = "addChild",
                          q = "JZ",
                          r = "alpha",
                          s = "to";
                        if (d["length"] > 0) {
                          let b, t;
                          e = !1;
                          t = f <= 1, b = t ? 0 : 1;
                          if (a - this["KZ"][b] > this["iK"]) {
                            let c, e;
                            e = d["shift"]();
                            let u;
                            this["VZ"]["push"](e), 0 === f ? u = this["propBlueLine1"] : 1 === f ? u = this["propBlueLine2"] : 2 === f ? u = this["propRedLine1"] : 3 === f && (u = this["propRedLine2"]), u["addChild"](e);
                            c = t ? this["bluePropEffect"] : this["redPropEffect"];
                            e["addChild"](c), c["size"](this["JZ"], this["JZ"]), c["pos"](o, o), c["scale"](1.5, 1.5), c["alpha"] = .8, Laya["Tween"]["create"](c)["to"]("scaleX", 1)["to"]("scaleY", 1)["to"]("alpha", 0)["duration"](this["iK"] - h[81])["then"](() => {
                              this["addChild"](c)
                            }, this), this["KZ"][b] = a
                          }
                        }
                      }), e && (this["QZ"] = !1, this["aK"] = !0, this["nK"] = 0)
                    }
                    this["aK"] && (this["nK"] += a, this["nK"] > this["tK"] && (this["oK"](), this["aK"] = !1))
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (4 == g) {
                Object["defineProperty"](b["prototype"], "Pn", {
                  ["value"]() {
                    var a = hr,
                      b = a[0],
                      c = a[3],
                      d = "instance";
                    nx["instance"]()["wa"]("match"), this["reset"](), sF["instance"]()["Pn"]("MatchScene")
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (5 == g) {
                Object["defineProperty"](b["prototype"], "oK", {
                  ["value"]() {
                    var a = hr,
                      b = hu,
                      c = a[3],
                      d = a[5],
                      e = a[2],
                      f = a[6],
                      g = a[4],
                      h = a[0],
                      i = a[11],
                      j = a[10],
                      k = b[81],
                      l = "visible",
                      m = "instance",
                      n = "then",
                      o = "Tween",
                      p = "create",
                      q = "to",
                      r = "duration",
                      s = "alpha",
                      t = "parallel",
                      u = "yK";
                    this["title"]["visible"] = !1, this["xBtn"]["visible"] = !1, nx["instance"]()["pause"](!1);
                    (0 === uq["instance"]()["player"]["round"] ? sE["instance"]()["xj"]() : sE["instance"]()["startGame"]())["then"](a => {
                      a["addChild"](this), a["scale"](1.03, 1.03), Laya["Tween"]["create"](a)["to"]("scaleX", 1)["to"]("scaleY", 1)["duration"](b[223])["ease"](pQ["sineOut"]), Laya["Tween"]["create"](this["bg"])["to"]("alpha", 0)["delay"](k)["duration"](b[132])["parallel"](this["propBlueLine1"])["to"]("alpha", 0)["duration"](k)["parallel"](this["propBlueLine2"])["to"]("alpha", 0)["duration"](k)["parallel"](this["propRedLine1"])["to"]("alpha", 0)["duration"](k)["parallel"](this["propRedLine2"])["to"]("alpha", 0)["duration"](k), this["pK"](), this["yK"](this["flagRed"], !1), this["yK"](this["flagBlue"])["then"](() => {
                        this["Pn"](), nx["instance"]()["resume"]()
                      })
                    })
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              }
              if (6 == g) {
                Object["defineProperty"](b["prototype"], "yK", {
                  ["value"](a, b = !0) {
                    var c = hr,
                      d = hu,
                      e = c[3],
                      f = c[4],
                      g = d[132],
                      h = "to",
                      i = "duration",
                      j = "ease",
                      k = "backIn",
                      l = "skewX",
                      m = "chain",
                      n = "linear";
                    let o;
                    this["hK"] = !1;
                    o = b ? -1 : 1;
                    return Laya["Tween"]["create"](a)["to"]("x", (a["width"] + d[81]) * o)["duration"](d[176])["ease"](pQ["backIn"])["parallel"]()["to"]("skewX", -d[1] * o)["duration"](d[175])["ease"](pQ["backIn"])["chain"]()["to"]("skewX", -5 * o)["duration"](g)["ease"](pQ["linear"])["chain"]()["to"]("skewX", 0)["duration"](g)["ease"](pQ["linear"])
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (7 == g) {
                Object["defineProperty"](b["prototype"], "cK", {
                  ["value"](a, b = !0) {
                    var c = hr,
                      d = hu,
                      e = c[3],
                      f = d[132],
                      g = "skewX",
                      h = "to",
                      i = "duration",
                      j = "ease",
                      k = "linear",
                      l = "chain";
                    let m;
                    m = b ? -1 : 1;
                    return a["skewX"] = -d[45] * m, Laya["Tween"]["create"](a)["to"]("x", 0)["duration"](d[176])["ease"](pQ["backOut"])["parallel"]()["to"]("skewX", 10 * m)["duration"](d[175])["ease"](pQ["linear"])["chain"]()["to"]("skewX", -5 * m)["duration"](f)["ease"](pQ["linear"])["chain"]()["to"]("skewX", 0)["duration"](f)["ease"](pQ["linear"])
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (8 == g) {
                Object["defineProperty"](b["prototype"], "lK", {
                  ["value"]() {
                    var a = hr,
                      b = hu,
                      c = a[3],
                      d = a[6],
                      e = a[1],
                      f = a[5],
                      g = a[4],
                      h = a[2],
                      i = b[45],
                      j = b[112],
                      k = "instance",
                      l = "playSound",
                      m = "vs1",
                      n = "visible",
                      o = "vs2",
                      p = "Tween",
                      q = "create",
                      r = "to",
                      s = "rotation",
                      t = "duration",
                      u = "ease",
                      v = "cubicOut",
                      w = "then",
                      x = "scaleX",
                      y = "scaleY",
                      z = "vsLight";
                    pC["instance"]()["playSound"]("match_drum"), this["vs1"]["visible"] = !0, this["vs2"]["visible"] = !0, Laya["Tween"]["create"](this["vs1"])["to"]("x", b[289])["to"]("rotation", i)["duration"](j)["ease"](pQ["cubicOut"]), Laya["Tween"]["create"](this["vs2"])["to"]("x", b[312])["to"]("rotation", -i)["duration"](j)["ease"](pQ["cubicOut"])["then"](() => {
                      pC["instance"]()["playSound"]("swords_clash"), Laya["Tween"]["create"](this["vs1"])["to"]("scaleX", 1.1)["to"]("scaleY", 1.1)["duration"](10), Laya["Tween"]["create"](this["vs2"])["to"]("scaleX", 1.1)["to"]("scaleY", 1.1)["duration"](10)["then"](() => {
                        Laya["Tween"]["create"](this["vs1"])["to"]("x", b[313])["to"]("scaleX", .8)["to"]("scaleY", .8)["duration"](10), Laya["Tween"]["create"](this["vs2"])["to"]("x", b[307])["to"]("scaleX", .8)["to"]("scaleY", .8)["duration"](10)
                      }), Laya["Tween"]["create"](this["vsLight"])["to"]("scaleX", 1)["to"]("scaleY", 1)["duration"](i)["then"](() => {
                        this["vsLight"]["scale"](.5, .5), this["vsLight"]["skin"] = "resources/img/matchUI/vsLight1.png", Laya["Tween"]["create"](this["vsLight"])["to"]("scaleX", .6)["to"]("scaleY", .6)["duration"](b[22])["chain"]()["to"]("scaleX", 1)["to"]("scaleY", 1)["to"]("alpha", 0)["duration"](b[1])
                      })
                    })
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (9 == g) {
                Object["defineProperty"](b["prototype"], "pK", {
                  ["value"]() {
                    var a = hr,
                      b = hu,
                      c = a[1],
                      d = a[6],
                      e = b[43],
                      f = b[175],
                      g = "Tween",
                      h = "create",
                      i = "to",
                      j = "rotation",
                      k = "duration";
                    Laya["Tween"]["create"](this["vs1"])["to"]("x", -b[81])["to"]("rotation", -e)["duration"](f), Laya["Tween"]["create"](this["vs2"])["to"]("x", b[314])["to"]("rotation", e)["duration"](f)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (10 == g) {
                Object["defineProperty"](b["prototype"], "rK", {
                  ["value"]() {
                    var a = hr,
                      b = a[5],
                      c = a[0],
                      d = a[2],
                      e = a[6],
                      f = "text",
                      g = "blueRank",
                      h = "visible",
                      i = "blueWinRate",
                      j = "instance",
                      k = "rank",
                      l = "then",
                      m = "cK",
                      n = "forEach",
                      o = "sK",
                      p = "uK";
                    let q, r, s;
                    this["title"]["text"] = "匹配完成", this["blueRank"]["visible"] = !0, this["blueWinRate"]["visible"] = !0;
                    s = uq["instance"]()["au"]["Ai"];
                    this["blueRank"]["text"] = s["rank"], tR["instance"]()["wu"](s["rank"])["then"](c => {
                      this["blueAvatar"]["texture"] = c
                    }), this["blueWinRate"]["text"] = s["Di"]["toFixed"](1) + "%", this["lK"](), this["cK"](this["flagRed"]), this["cK"](this["flagBlue"], !1)["then"](() => {
                      this["hK"] = !0
                    });
                    q = !0;
                    r = vb["instance"]();
                    r["qP"]["forEach"]((a, b) => {
                      b >= this["sK"] || (this["uK"](a, !0, !1), q = !1)
                    }), r["$P"]["forEach"]((a, b) => {
                      b >= this["sK"] || (this["uK"](a, !0, !0), q = !1)
                    }), r["QP"]["forEach"]((a, b) => {
                      b >= this["sK"] || (this["uK"](a, !1, !1), q = !1)
                    }), r["ZP"]["forEach"]((a, b) => {
                      b >= this["sK"] || (this["uK"](a, !1, !0), q = !1)
                    }), q && (this["nK"] = -hu[123], this["aK"] = !0)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (c[7] == g) {
                Object["defineProperty"](b["prototype"], "uK", {
                  ["value"](a, b, c) {
                    var d = hr,
                      e = d[0],
                      f = d[3],
                      g = d[2],
                      h = d[1],
                      i = "instance",
                      j = "Image",
                      k = "size",
                      l = "JZ";
                    let m, n, o, p, q, r, s;
                    p = b ? "resources/img/matchUI/propBoxRed.png" : "resources/img/matchUI/propBoxBlue.png";
                    n = b ? vb["instance"]()["LA"](a) : vb["instance"]()["vA"](a);
                    q = "resources/img/props/" + uq["instance"]()["props"]["Ye"][a]["name"] + "_" + n + ".png", s = new Laya["Image"](p);
                    s["size"](this["JZ"], this["JZ"]);
                    r = new Laya["Image"](q), o = Math["min"](hu[65], this["JZ"]);
                    r["size"](o, o), r["pivot"](o / 2, o / 2), s["addChild"](r), r["pos"](this["JZ"] / 2, this["JZ"] / 2);
                    m = 0;
                    m += b ? 2 : 0, m += c ? 1 : 0, this["ZZ"][m]["push"](s), this["KZ"][m] = Laya["timer"]["currTimer"], this["QZ"] = !0
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (c[9] == g) {
                Object["defineProperty"](b["prototype"], "reset", {
                  ["value"]() {
                    var a = hr,
                      b = hu,
                      c = a[3],
                      d = a[0],
                      e = a[11],
                      f = a[10],
                      g = a[5],
                      h = a[6],
                      i = a[2],
                      j = a[1],
                      k = b[81],
                      l = b[43],
                      m = "Tween",
                      n = "killAll",
                      o = "flagBlue",
                      p = "flagRed",
                      q = "addChild",
                      r = "forEach",
                      s = "destroy",
                      t = "length",
                      u = "VZ",
                      v = "title",
                      w = "text",
                      x = "skin",
                      y = "blueRank",
                      z = "blueWinRate",
                      A = "visible",
                      B = "width",
                      C = "vs1",
                      D = "rotation",
                      E = "vs2",
                      F = "vsLight",
                      G = "alpha";
                    Laya["Tween"]["killAll"](this["flagBlue"]), Laya["Tween"]["killAll"](this["flagRed"]), this["hK"] = !1, this["addChild"](this["bluePropEffect"]), this["addChild"](this["redPropEffect"]), this["QZ"] = !1, this["ZZ"]["forEach"](a => {
                      a["forEach"](a => {
                        a["destroy"](!0)
                      }), a["length"] = 0
                    }), this["VZ"]["forEach"](a => {
                      a["destroy"](!0)
                    }), this["VZ"]["length"] = 0, this["aK"] = !1, this["nK"] = 0, this["title"]["text"] = "开始匹配", this["blueAvatar"]["skin"] = "", this["blueRank"]["text"] = "", this["blueWinRate"]["text"] = "", this["blueRank"]["visible"] = !1, this["blueWinRate"]["visible"] = !1, this["flagBlue"]["x"] = -this["flagBlue"]["width"], this["flagRed"]["x"] = this["flagRed"]["width"], this["vs1"]["visible"] = !1, this["vs1"]["x"] = -k, this["vs1"]["rotation"] = -l, this["vs2"]["visible"] = !1, this["vs2"]["x"] = Laya["stage"]["width"] + k, this["vs2"]["rotation"] = l, this["vsLight"]["skin"] = "resources/img/matchUI/vsLight0.png", this["vsLight"]["scale"](0, 0), this["vsLight"]["alpha"] = 1, this["propBlueLine1"]["alpha"] = 1, this["propBlueLine2"]["alpha"] = 1, this["propRedLine1"]["alpha"] = 1, this["propRedLine2"]["alpha"] = 1, this["title"]["visible"] = !0, this["bg"]["alpha"] = 1
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (c[3] == g) {
                Object["defineProperty"](b["prototype"], "onClosed", {
                  ["value"](a) {
                    var b = hr;
                    nx["instance"]()["wa"]("MatchScene")
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                continue
              } else if (c[14] == g) {
                Object["defineProperty"](b["prototype"], "onAwake", {
                  ["value"]() {
                    var a = hr,
                      b = hu,
                      c = a[0],
                      d = a[10],
                      e = a[3],
                      f = a[2],
                      g = b[43],
                      h = "destroy",
                      i = "pivot",
                      j = "bg_1",
                      k = "height",
                      l = "bg_2",
                      m = "stage",
                      n = "getChildAt";
                    this["zIndex"] = t1["Br"], this["placeholder1"]["destroy"](), this["placeholder2"]["destroy"](), this["placeholder3"]["destroy"](), this["placeholder4"]["destroy"](), this["bluePropEffect"]["pivot"](g, g), this["redPropEffect"]["pivot"](g, g), this["xBtn"]["on"](Laya["Event"]["CLICK"], this, this["Pn"]), this["bg_1"]["height"] = this["bg_2"]["height"] = Laya["stage"]["height"], this["bg_1"]["getChildAt"](0)["height"] = this["bg_2"]["getChildAt"](0)["height"] = Laya["stage"]["height"]
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
        } ["bind"](this)["apply"]();
