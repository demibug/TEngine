    vS = function() {
      var a = hr;
      let b = class extends nb {
        constructor() {
          var a = hr,
            b = a[0];
          var c = arguments;
          super(...c), this["rp"] = [], this["yG"] = 0, this["fG"] = hu[123], this["XX"] = 0, this["KX"] = [0, 0], this["cG"] = [0, 0], this["zX"] = [], this["gG"] = new Map, this["dG"] = [], this["LG"] = [{
            ["x"]: 2,
            ["y"]: 3
          }, {
            ["x"]: 3,
            ["y"]: 2
          }, {
            ["x"]: 5,
            ["y"]: 2
          }], this["$Y"] = null, this["sG"] = null, this["mG"] = new Map, this["GX"] = new Map, this["wG"] = 0, this["vG"] = 0, this["_G"] = 0, this["kG"] = !1, this["SG"] = !1, this["xG"] = 0, this["step"] = 1
        }
        init() {
          var a = hr,
            b = a[2],
            c = a[0],
            d = "sG",
            e = "nG",
            f = "instance",
            g = "length";
          if (super["init"](), this["sG"] || (this["sG"] = []), this["bG"] = new ne(this), this["MG"] = new vR(this), !this["nG"]) {
            const d = uq["instance"]()["map"]["pe"];
            this["nG"] = Array["from"](new Array(d["length"]), () => new Array(d[0]["length"] / 2)["fill"](null))
          }
          oc["instance"]["on"](sS["Jt"], this, this["PG"])
        }
      };
      ! function() {
        "use strict";
        var a = hr,
          c = a[0],
          d = "defineProperty",
          qEp = "value",
          qEq = "enumerable",
          qEr = "configurable",
          qEs = "writable";
        Object["defineProperty"](b["prototype"], "startGame", {
          ["value"]() {
            var a = hr,
              b = hu,
              c = a[0],
              d = a[1],
              e = "instance",
              f = "au",
              g = "map",
              h = "Si",
              i = "DG",
              j = "getChildByName",
              k = "ub";
            if (!uq["instance"]()["au"]["ki"]) return;
            const l = uq["instance"]()["map"]["mapIndex"],
              m = uq["instance"]()["au"]["Si"] < 2;
            this["GX"] = this["AG"](l, m), this["dG"] = this["EG"](l), this["BG"](), this["DG"] = sF["instance"]()["En"]("BattleScene")["getChildByName"]("box"), this["map"] = this["DG"]["getChildByName"]("map"), this["PA"] = na["instance"]()["ub"](1, !1), this["hX"] = na["instance"]()["ub"](3, !1), uq["instance"]()["au"]["Ji"] += uq["instance"]()["My"]["hi"], this["IG"](), this["fG"] = [b[118], b[122], b[123], b[176]][Math["min"](3, Math["max"](0, uq["instance"]()["au"]["Si"]))], nx["instance"]()["La"]("AICtr", this, this["update"]), vb["instance"]()["kA"](!1)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "refresh", {
          ["value"]() {
            var a = hr,
              b = a[6],
              c = a[0],
              d = a[2];
            const e = r0["instance"]()["_Y"]({
              ["type"]: 2,
              ["nm"]: !1
            });
            e["success"] || console["warn"]("AI 刷新失败:", e["reason"])
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "CG", {
          ["value"]() {
            var a = hr,
              b = a[0],
              c = "hX";
            for (let d = 0; d < this["hX"]["size"]; d++) {
              const e = this["hX"]["getItem"](d);
              e && (this["QX"](e), e["x_"] && this["$Y"]["delete"](e["id"]))
            }
            this["hX"]["removeAll"]()
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "gameOver", {
          ["value"]() {
            var a = hr,
              b = a[0],
              c = "instance",
              d = "length",
              e = "KX",
              f = "cG";
            uq["instance"]()["au"]["ki"] && (nx["instance"]()["wa"]("AICtr"), this["zX"]["length"] = 0, this["yG"] = 0, this["XX"] = 0, this["KX"][0] = 0, this["KX"][1] = 0, this["cG"][0] = 0, this["cG"][1] = 0, this["step"] = 1, this["sG"]["length"] = 0, this["kG"] = !1, this["SG"] = !1)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "update", {
          ["value"](a) {
            var b = hr,
              c = b[0],
              d = "yG";
            this["yG"] += a, this["yG"] >= this["fG"] && (this["yG"] = 0, this["TG"](a))
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "TG", {
          ["value"](a) {
            var b = hr,
              c = b[0],
              d = b[4],
              e = "step",
              f = "instance",
              g = "au",
              h = "XX",
              i = "Xi",
              j = "bG",
              k = "rp",
              l = "length",
              m = "KX",
              n = "MG",
              o = "nG",
              p = "cG";
            if (1 === this["step"])
              if (uq["instance"]()["au"]["Ji"] >= uq["instance"]()["au"]["gi"]) this["refresh"](), this["XX"] = 0, this["step"] = 2;
              else {
                if (Math["random"]() <= uq["instance"]()["My"]["ni"][uq["instance"]()["au"]["Si"]]) return void this["UG"]();
                this["YO"]()
              }
            else if (2 === this["step"]) uq["instance"]()["au"]["Xi"] || (uq["instance"]()["au"]["Xi"] = !0), this["bG"]["YX"](), this["XX"] >= 5 && (this["rp"]["length"] = 0, this["KX"][0] = 0, this["KX"][1] = 0, this["step"] = 3);
            else if (3 === this["step"]) this["KX"][0] < this["PA"]["sb"]["length"] ? this["bG"]["ZX"]() : this["step"] = 4;
            else if (4 === this["step"]) {
              this["rp"] = this["rp"]["filter"](a => null !== this["uG"](a["id"])), this["sG"]["length"] = 0, this["MG"]["tG"](), this["MG"]["iG"](), this["MG"]["hG"]();
              for (const a of this["nG"]) a["fill"](null);
              this["MG"]["aG"](), this["cG"][0] = 0, this["cG"][1] = 0, this["step"] = 5
            } else 5 === this["step"] && (this["cG"][0] < this["nG"]["length"] ? this["MG"]["lG"]() : this["step"] = 1)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "FG", {
          ["value"](a, b) {
            var c = hr,
              d = c[0];
            return a + (b ? "_s" : "_f")
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "AG", {
          ["value"](a, b) {
            var c = hr,
              d = c[0],
              e = c[1],
              f = "mG";
            const g = this["FG"](a, b);
            let h = this["mG"]["get"](g);
            return h || (h = b ? qj["kX"](a) : qj["yX"](a), this["mG"]["set"](g, h)), h
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "EG", {
          ["value"](a) {
            var b = hr,
              c = b[1],
              d = "gG";
            let e = this["gG"]["get"](a);
            return e || (e = qj["xX"](a), this["gG"]["set"](a, e)), e
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "BG", {
          ["value"]() {
            var a = hr,
              b = a[0],
              c = "wG";
            const d = uq["instance"]()["map"]["me"];
            this["wG"] = d ? d["length"] : 0, this["vG"] = Math["floor"](.15 * this["wG"]), this["_G"] = Math["ceil"](.85 * this["wG"])
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "WX", {
          ["value"](b = "1_1", c) {
            var d = hr,
              e = hu,
              f = d[0],
              g = d[4],
              h = d[1],
              i = d[2],
              j = e[276],
              k = "zX",
              l = "length",
              m = "instance",
              n = "map",
              o = "pe",
              p = "Ys",
              qFv = "DX",
              qFw = "TX",
              qFx = "OG";
            const q = this["PA"]["sb"];
            this["zX"]["length"] = 0;
            for (let a = 0; a < q["length"]; a++)
              for (let c = 0; c < q[a]["length"]; c++) null == q[a][c] && uq["instance"]()["map"]["pe"][a][c] === b && this["zX"]["push"]({
                ["x"]: a,
                ["y"]: c
              });
            if (0 === this["zX"]["length"]) return !1;
            const r = uq["instance"]()["au"]["Si"];
            if (r < 2) return np["Ys"](this["zX"]), !0;
            const s = uq["instance"]()["map"]["pe"],
              t = uq["instance"]()["map"]["me"],
              u = this["wG"],
              v = this["vG"],
              w = this["_G"],
              x = [
                [1, 0],
                [-1, 0],
                [0, 1],
                [0, -1]
              ],
              y = (a, b, c, d) => {
                var e = hr,
                  f = "abs";
                if (!t || 0 === u) return j;
                let g = j;
                const h = d > u ? u : d;
                for (let d = c < 0 ? 0 : c; d < h; d++) {
                  const c = Math["abs"](a - t[d]["x"]) + Math["abs"](b - t[d]["y"]);
                  c < g && (g = c)
                }
                return g
              },
              z = this["zX"]["map"](a => {
                return {
                  ["c"]: a,
                  ["DX"]: (b = a["x"], d = a["y"], x["reduce"]((a, [c, e]) => {
                    const f = b + c,
                      g = d + e;
                    return f >= 0 && g >= 0 && f < s["length"] && g < s[0]["length"] && "0_1" === s[f][g] ? a + 1 : a
                  }, 0)),
                  ["TX"]: y(a["x"], a["y"], v, w),
                  ["OG"]: 3 === r ? qj["bX"](this["GX"], a["x"], a["y"], c) : 0
                };
                var b, d
              });
            if (z["sort"]((a, b) => 2 === r ? b["DX"] !== a["DX"] ? b["DX"] - a["DX"] : a["TX"] - b["TX"] : b["OG"] !== a["OG"] ? b["OG"] - a["OG"] : b["DX"] !== a["DX"] ? b["DX"] - a["DX"] : a["TX"] - b["TX"]), 2 === r && z["length"] > 3) {
              const a = z["splice"](0, Math["min"](5, z["length"]));
              np["Ys"](a), z["unshift"](...a)
            }
            return this["zX"] = z["map"](a => a["c"]), !0
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "uG", {
          ["value"](a) {
            var b = hr,
              c = b[0];
            const d = vc["instance"]()["uM"](a);
            return d && 0 !== d["l_"] ? d : null
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "YG", {
          ["value"](a) {
            var b = hr,
              c = b[0],
              d = "l_";
            const e = na["instance"]()["ub"](a["l_"], a["nm"])["eb"](a);
            return e ? {
              ["containerType"]: a["l_"],
              ["x"]: e["x"],
              ["y"]: e["y"]
            } : null
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "pG", {
          ["value"](a, b, c) {
            var d = hr,
              e = d[0],
              f = d[2],
              g = d[5],
              qFJ = "nm",
              h = "success";
            const i = this["uG"](a);
            if (!i) return !1;
            const j = this["YG"](i);
            if (!j) return !1;
            const k = r0["instance"]()["_Y"]({
              ["type"]: 1,
              ["DY"]: j["containerType"],
              ["IY"]: j["x"],
              ["CY"]: j["y"],
              ["AY"]: 1,
              ["targetX"]: b,
              ["targetY"]: c,
              ["nm"]: i["nm"]
            });
            return k["success"] || console["warn"]("AI 设置士兵位置失败:", k["reason"]), k["success"]
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "jX", {
          ["value"](a, b, c) {
            var d = hr,
              e = d[0];
            this["pG"](a["id"], b, c)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "QX", {
          ["value"](a) {
            var b = hr,
              c = b[0],
              d = "instance",
              e = "id";
            vb["instance"]()["ZP"]["indexOf"](hu[10]) >= 0 && (uq["instance"]()["au"]["Ji"] += a["level"]), a["x_"] ? vc["instance"]()["HP"](a["id"]) : vc["instance"]()["WP"](a["id"])
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "PG", {
          ["value"]() {
            var a = hr,
              b = a[0],
              c = "instance",
              d = "My",
              e = "ei",
              f = "au",
              g = "ii",
              h = "Si";
            for (let i = 0; i < uq["instance"]()["My"]["ei"]["length"]; i++)
              if (uq["instance"]()["au"]["li"] === uq["instance"]()["My"]["ei"][i]) {
                uq["instance"]()["au"]["Ji"] += uq["instance"]()["My"]["ii"][uq["instance"]()["au"]["Si"]][i], console["log"]("ai加钱", uq["instance"]()["My"]["ii"][uq["instance"]()["au"]["Si"]][i]);
                break
              }
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "IG", {
          ["value"]() {
            var a = hr,
              b = "$Y";
            this["$Y"] || (this["$Y"] = new Map), this["$Y"]["clear"]()
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "YO", {
          ["value"]() {
            var a = hr,
              b = a[0],
              c = "instance",
              d = "xG",
              e = "length";
            const f = nx["instance"]()["fa"];
            if (f - this["xG"] < hu[101]) return;
            const g = vb["instance"]()["KP"];
            if (0 === g["length"]) return;
            const h = g["filter"](a => !a["Gb"]());
            0 !== h["length"] && (this["Yb"](h[np["range"](0, h["length"], !0)]), this["xG"] = f)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "Yb", {
          ["value"](a) {
            var b = hr,
              c = b[0],
              d = b[9],
              e = b[1],
              f = b[3],
              g = "QY",
              h = "type",
              i = "use",
              j = "KY",
              k = "instance",
              l = "range",
              m = "length",
              n = "log",
              o = "props",
              p = "Ye",
              q = "txt";
            rB["VY"] = this, rB["cX"] = 2;
            let r = rB["QY"];
            switch (a["type"]) {
              case 3:
              case 4:
              case 10:
                r = nO["use"](a);
                break;
              case 5:
                r = rB["KY"](3, 3);
                break;
              case 6:
                r = pM["use"](a);
                break;
              case 2:
                r = o0["use"](a);
                break;
              case 7: {
                const a = uq["instance"]()["map"]["pe"];
                r = rB["KY"](np["range"](0, a["length"], !0), np["range"](a[0]["length"] / 2, a[0]["length"], !0));
                break
              }
              case 8:
              case 9:
                r = tE["use"](a)
            }
            r !== rB["QY"] ? (console["log"]("✅AI成功使用道具 -", uq["instance"]()["props"]["Ye"][a["type"]]["txt"]), a["Yb"](r, na["instance"]())) : console["log"]("❌AI使用道具失败 -", uq["instance"]()["props"]["Ye"][a["type"]]["txt"])
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "UG", {
          ["value"]() {
            var a = hr,
              b = a[0],
              c = "kG";
            if (this["kG"]) return;
            this["kG"] = !0;
            const d = sk["AX"](2, this["GX"]);
            for (const c of d) oc["instance"]["event"](sS["At"], !1, c["x"], c["y"])
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "XG", {
          ["value"]() {
            var a = hr,
              b = a[0],
              c = "SG";
            if (this["SG"]) return;
            this["SG"] = !0;
            vb["instance"]()["_A"](!1, 1)["Yb"](rB["QY"])
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        })
      } ["bind"](b)();
      return b
    } ["bind"](this)["apply"](),
    vT = function() {
      var a = hr;
      let b = class extends qU {
        constructor() {
          var a = hr,
            b = a[0];
          var c = arguments;
          super(...c), this["GG"] = .7, this["HG"] = !1, this["WG"] = ["刀", "弓", "黄", "忠", "铲"], this["jG"] = new Set(["黄", "忠"]), this["zG"] = !1, this["NG"] = !1, this["qG"] = !1, this["$G"] = !1, this["VG"] = "把文字拖到上边  排兵布阵", this["QG"] = hu[101], this["ZG"] = !1
        }
      };
      ! function() {
        "use strict";
        var a = hr,
          c = a[0],
          d = "defineProperty",
          qGy = "value",
          qGz = "enumerable",
          qGA = "configurable",
          qGB = "writable";
        Object["defineProperty"](b["prototype"], "init", {
          ["value"]() {},
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "KG", {
          ["value"]() {
            var a = hr,
              b = a[0],
              c = "ci";
            this["HG"] = !0, this["zG"] = !1, this["NG"] = !1, this["qG"] = !1;
            const d = uq["instance"]()["au"];
            this["$G"] = d["ci"], d["ci"] = !0, d["Si"] = 0, d["Ai"]["Bi"] = 0, d["xi"] = this["GG"], console["log"]("[TutorialMgr] 新手教程已启动，AI 难度 0，AI 攻击力 70%，无波数限制，不出现 Boss")
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "JG", {
          ["value"]() {
            var a = hr,
              b = hu,
              c = a[0],
              d = a[3],
              e = "instance";
            if (!this["HG"]) return;
            const f = uq["instance"]()["au"];
            f["delayTime"] = b[277], f["Xi"] = !0, console["log"]("[TutorialMgr] 游戏已加载，等待玩家完成第一次随机刷新"), Laya["timer"]["once"](b[214], this, () => {
              oc["instance"]["event"](sS["ds"], "点击下方红色按钮  征召士兵")
            })
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "tH", {
          ["value"](a) {
            var b = hr,
              c = b[0],
              d = "instance",
              e = "bO",
              f = "WG";
            return !this["HG"] || this["NG"] ? vN["instance"]()["bO"](!0) : this["zG"] ? a >= 0 && a < this["WG"]["length"] ? this["WG"][a] : vN["instance"]()["bO"](!0) : this["sH"]()
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "sH", {
          ["value"]() {
            var a = hr,
              b = hu,
              c = "instance",
              d = "bO",
              e = "has";
            const f = this["jG"];
            let g = vN["instance"]()["bO"](!0),
              h = 0;
            for (; f["has"](g) && h < b[22];) g = vN["instance"]()["bO"](!0), h += 1;
            return f["has"](g) ? (console["warn"]("[TutorialMgr] 牌库连续刷到黄忠字，回退为基础兵种"), "刀") : g
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "iH", {
          ["value"]() {
            var a = hr,
              b = a[0],
              c = "NG",
              d = "zG",
              e = "log";
            if (this["HG"] && !this["NG"]) {
              if (!this["zG"]) return this["zG"] = !0, console["log"]("[TutorialMgr] 第一次随机刷新完成，显示放置指引"), void this["hH"]();
              this["NG"] = !0, console["log"]("[TutorialMgr] 第二次固定刷新完成")
            }
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "gameOver", {
          ["value"]() {
            var a = hr,
              b = a[0],
              c = "HG",
              d = "instance";
            this["HG"] && (this["eH"](), oc["instance"]["event"](sS["ds"], null), uq["instance"]()["au"]["ci"] = this["$G"], this["HG"] = !1, this["zG"] = !1, this["NG"] = !1, this["qG"] = !1, console["log"]("[TutorialMgr] 新手教程已结束"))
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "aH", {
          ["value"]() {
            var a = hr,
              b = a[0];
            this["qG"] = !0, uq["instance"]()["au"]["Yi"] = !0, console["log"]("[TutorialMgr] 放置指引结束，开始出怪！")
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "hH", {
          ["value"]() {
            var a = hr,
              b = a[0],
              c = a[3];
            this["eH"](), oc["instance"]["event"](sS["ds"], this["VG"]), this["ZG"] = !0, Laya["timer"]["once"](this["QG"], this, this["nH"])
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "nH", {
          ["value"]() {
            var a = hr,
              b = a[0],
              c = "ZG";
            this["ZG"] && (this["ZG"] = !1, oc["instance"]["event"](sS["ds"], null), this["qG"] || this["aH"]())
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "eH", {
          ["value"]() {
            var a = hr,
              b = a[3],
              c = "ZG";
            this["ZG"] && (Laya["timer"]["clear"](this, this["nH"]), this["ZG"] = !1)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        })
      } ["bind"](b)();
      return b
    } ["bind"](this)["apply"](),
