        } ["bind"](this)["apply"](), tw = function() {
          var a = hr;
          let b;
          b = class c extends qU {
            constructor() {
              var a = hr,
                b = a[0];
              var c;
              c = arguments;
              super(...c), this["ly"] = {
                ["ph"]: 0,
                ["speed"]: 0,
                ["Lh"]: 0,
                ["mh"]: 0
              }, this["uy"] = 0, this["py"] = null, this["_map"] = null, this["yy"] = null, this["gy"] = null, this["Ly"] = null, this["my"] = null, this["_props"] = null, this["_player"] = null, this["wy"] = null, this["vy"] = null, this["ky"] = null, this["Sy"] = null, this["_stamina"] = null
            }
            Ay() {
              var a = hr,
                b = a[4],
                d = a[0],
                e = "Ey";
              let f, g;
              var h;
              g = this["player"];
              if (np["Gs"](g["registerTime"], Date["now"]()) < 1) {
                let d;
                d = Math["floor"]((g["roundDay"] - 1) / 7) % c["Ey"]["length"];
                return c["Ey"][d]
              }
              f = ((new Date)["getDay"]() + 6) % 7;
              return null != (h = c["By"][f]) ? h : 0
            }
            gameOver(a) {
              var b = hr,
                c = b[0],
                d = "gameOver";
              this["map"]["gameOver"](), this["enemy"]["gameOver"](), this["Oc"]["gameOver"](), this["eh"]["gameOver"](), this["au"]["gameOver"](), this["player"]["gameOver"](a), this["uy"] = 0
            }
            Dy(a, b) {
              var c = hr,
                d = c[0],
                e = c[1],
                f = c[3],
                g = c[4],
                h = "enemy",
                i = "uh",
                j = "length",
                k = "au",
                l = "max",
                m = "ly",
                n = "ph",
                o = "min",
                p = "player",
                q = "round",
                r = "rank",
                s = "speed";
              let t;
              var u, v;
              a >= this["enemy"]["uh"]["length"] && (a = this["map"]["oe"]);
              t = this["au"]["li"];
              if (t = Math["max"](1, t), this["au"]["ci"] && t > this["au"]["ui"]) this["ly"]["ph"] = this["enemy"]["uh"][a]["ph"][0] * Math["pow"](1.5, t - 1);
              else {
                let b, f, g, s;
                s = this["enemy"]["uh"][a]["ph"], g = this["au"]["pi"], b = Math["min"](t, s["length"]) - 1, f = Math["max"](0, Math["min"](t - 1, g["length"] - 1));
                this["player"]["round"] < 10 && t <= 10 ? this["ly"]["ph"] = s[b] * (null != (u = g[f]) ? u : 1) * this["enemy"]["oh"][this["player"]["round"]] : this["ly"]["ph"] = s[b] * (null != (v = g[f]) ? v : 1), t > 10 && (this["ly"]["ph"] += this["ly"]["ph"] * this["rank"]["Va"]["get"](this["rank"]["yu"]["id"])["addHp"])
              }
              return this["ly"]["speed"] = this["enemy"]["uh"][a]["speed"], this["ly"]
            }
            Iy(a, b) {
              var c = hr,
                d = c[0],
                e = c[3],
                f = "ly",
                g = "ph",
                h = "enemy",
                i = "dh",
                j = "speed",
                k = "Lh",
                l = "mh";
              return this["ly"]["ph"] = this["enemy"]["dh"][a]["ph"] * this["Dy"](this["map"]["oe"], b)["ph"], this["ly"]["speed"] = this["enemy"]["dh"][a]["speed"], this["ly"]["Lh"] = this["enemy"]["dh"][a]["Lh"], this["ly"]["mh"] = this["enemy"]["dh"][a]["mh"], this["ly"]
            }
            Cy(a, b, c = !0) {
              var d = hr,
                e = d[11],
                f = d[3],
                g = "Ty",
                h = "Point",
                i = "TEMP",
                j = "boolean",
                k = "setTo",
                l = "width",
                m = "height";
              if (!this["Ty"]) {
                let d;
                d = c ? Laya["Point"]["TEMP"] : new Laya["Point"];
                return b && "boolean" == typeof b && b ? d["setTo"](a["width"] / 2, a["height"] / 2) : b && "boolean" != typeof b ? d["setTo"](b["x"], b["y"]) : d["setTo"](0, 0), d
              }
              let n;
              return b ? "boolean" == typeof b ? (n = Laya["Point"]["TEMP"], n["setTo"](a["width"] / 2, a["height"] / 2)) : n = b : (n = Laya["Point"]["TEMP"], n["setTo"](0, 0)), this["Ty"]["globalToLocal"](a["localToGlobal"](n, !c))
            }
            Uy(a) {
              var b = hr,
                c = b[0],
                d = "player",
                e = "lowPrProps",
                f = "props",
                g = "Ye",
                h = "Ge",
                i = "He",
                j = "qe",
                k = "map",
                l = "$e";
              if (-1 == this["player"]["lowPrProps"]["indexOf"](a) && (this["player"]["lowPrProps"]["push"](a), this["props"]["Ye"][a]["Ge"] = .5 * this["props"]["Ye"][a]["Ge"], this["props"]["Ye"][a]["He"] = .2 * this["props"]["Ye"][a]["He"], this["props"]["ra"](a))) {
                let b, c;
                c = this["props"]["Ye"][a]["qe"];
                c && (this["props"]["Ye"][a]["qe"] = c["map"](a => .5 * a));
                b = this["props"]["Ye"][a]["$e"];
                b && (this["props"]["Ye"][a]["$e"] = b["map"](a => .2 * a))
              }
            }
          };
          ! function() {
            var a = hr,
              c = hu,
              d = a[2],
              e = a[3],
              f = a[0],
              g = "defineProperty",
              nFt = "value",
              nFu = "enumerable",
              nFv = "configurable",
              nFw = "writable",
              nFx = "get";
            w1_ch: for (let h of mo) {
              switch (h) {
                case 0:
                  "use strict";
                  break;
                case 1:
                  Object["defineProperty"](b["prototype"], "init", {
                    ["value"]() {
                      var a = hr,
                        b = a[0],
                        c = "player",
                        d = "init";
                      this["player"]["init"](), this["props"]["init"](this["player"]["lowPrProps"]), this["rank"]["init"](), this["map"]["init"](this["Ay"]()), this["bc"]["init"](), this["Oc"]["init"](), this["by"]["init"]()
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case 2:
                  Object["defineProperty"](b["prototype"], "map", {
                    ["get"]() {
                      var a = hr,
                        b = "_map";
                      return this["_map"] || (this["_map"] = new op), this["_map"]
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true
                  });
                  break;
                case 3:
                  Object["defineProperty"](b["prototype"], "Py", {
                    ["get"]() {
                      var a = hr,
                        b = "Sy";
                      return this["Sy"] || (this["Sy"] = new rF), this["Sy"]
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true
                  });
                  break;
                case 4:
                  Object["defineProperty"](b["prototype"], "rank", {
                    ["get"]() {
                      var a = hr,
                        b = "ky";
                      return this["ky"] || (this["ky"] = new ps), this["ky"]
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true
                  });
                  break;
                case 5:
                  Object["defineProperty"](b["prototype"], "au", {
                    ["get"]() {
                      var a = hr,
                        b = "my";
                      return this["my"] || (this["my"] = new uo), this["my"]
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true
                  });
                  break;
                case 6:
                  Object["defineProperty"](b["prototype"], "bc", {
                    ["get"]() {
                      var a = hr,
                        b = "vy";
                      return this["vy"] || (this["vy"] = new q2), this["vy"]
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true
                  });
                  break;
                case 7:
                  Object["defineProperty"](b["prototype"], "startGame", {
                    ["value"]() {
                      var a = hr,
                        b = a[0],
                        c = "startGame";
                      this["map"]["startGame"](this["Ay"]()), this["enemy"]["startGame"](), this["Oc"]["startGame"](), this["eh"]["startGame"](), this["au"]["startGame"](), this["player"]["startGame"]()
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case 8:
                  Object["defineProperty"](b["prototype"], "xy", {
                    ["value"]() {
                      return this["uy"] += 1
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case 9:
                  Object["defineProperty"](b["prototype"], "Ry", {
                    ["value"]() {
                      var a = hr;
                      return this["player"]["settingData"]
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case 10:
                  Object["defineProperty"](b["prototype"], "enemy", {
                    ["get"]() {
                      var a = hr,
                        b = "yy";
                      return this["yy"] || (this["yy"] = new sD), this["yy"]
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true
                  });
                  break;
                case c[7]:
                  Object["defineProperty"](b["prototype"], "player", {
                    ["get"]() {
                      var a = hr,
                        b = "_player";
                      return this["_player"] || (this["_player"] = new pa), this["_player"]
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true
                  });
                  break;
                case c[9]:
                  Object["defineProperty"](b["prototype"], "props", {
                    ["get"]() {
                      var a = hr,
                        b = "_props";
                      return this["_props"] || (this["_props"] = new sh), this["_props"]
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true
                  });
                  break;
                case c[3]:
                  Object["defineProperty"](b["prototype"], "My", {
                    ["get"]() {
                      var a = hr,
                        b = "wy";
                      return this["wy"] || (this["wy"] = new un), this["wy"]
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true
                  });
                  break;
                case c[14]:
                  Object["defineProperty"](b["prototype"], "Ua", {
                    ["value"]() {
                      var a = hr,
                        b = "_props";
                      this["_props"] = new sh, this["_props"]["init"](this["player"]["lowPrProps"])
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true,
                    ["writable"]: true
                  });
                  break;
                case c[12]:
                  Object["defineProperty"](b["prototype"], "Oc", {
                    ["get"]() {
                      var a = hr,
                        b = "gy";
                      return this["gy"] || (this["gy"] = new tG), this["gy"]
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true
                  });
                  break;
                case c[11]:
                  Object["defineProperty"](b["prototype"], "by", {
                    ["get"]() {
                      var a = hr,
                        b = "py";
                      return this["py"] || (this["py"] = new ns), this["py"]
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true
                  });
                  break;
                case c[13]:
                  Object["defineProperty"](b["prototype"], "eh", {
                    ["get"]() {
                      var a = hr,
                        b = "Ly";
                      return this["Ly"] || (this["Ly"] = new nu), this["Ly"]
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true
                  });
                  break;
                case c[5]:
                  Object["defineProperty"](b["prototype"], "stamina", {
                    ["get"]() {
                      var a = hr,
                        b = "_stamina";
                      return this["_stamina"] || (this["_stamina"] = new r9), this["_stamina"]
                    },
                    ["enumerable"]: false,
                    ["configurable"]: true
                  });
                  break;
                default:
                  break
              }
            }
          } ["bind"](b)();
          return b
        } ["bind"](this)["apply"]();
