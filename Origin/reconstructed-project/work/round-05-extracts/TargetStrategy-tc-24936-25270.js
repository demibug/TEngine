      pI = s9, tc = class a {
        static produce(a, b) {
          var c = hr,
            d = hu,
            e = c[0],
            f = "Lk",
            g = "lk",
            h = "pk",
            i = "ck",
            j = "uk",
            k = "rk";
          switch (a) {
            case d[81]:
              const l = Laya["Pool"]["getItemByCreateFun"](`HitEnemyStrategy${a}`, () => {
                const b = new oE;
                return b["gk"] = "HitEnemyStrategy" + a, b["dk"] = a, b
              });
              if (b) {
                let a;
                a = b;
                "Lk" in a && (Array["isArray"](a["Lk"]) ? l["lk"] = a["Lk"] : "number" == typeof a["Lk"] && (l["lk"] = [a["Lk"]])), "pk" in a && (l["pk"] = a["pk"]), "ck" in a && (l["ck"] = a["ck"]), l["uk"] = "uk" in a ? a["uk"] : "requestRemove"
              } else l["pk"] = 0, l["lk"] = [], l["ck"] = !0;
              return l["yk"] = !1, l["fk"] = !1, l;
            case d[94]:
            default:
              return tS["rk"];
            case d[90]:
              return ts["rk"];
            case d[89]:
              return pI["rk"]
          }
        }
        static copyFrom(b) {
          var c = hr;
          let d;
          d = a["produce"](b["dk"]);
          return Object["assign"](d, b), d
        }
        static recover(a) {
          var b = hr,
            c = b[0];
          let d;
          if (!a) return;
          if (void 0 === a["dk"]) return;
          a instanceof oE && (a["lk"] = [], a["pk"] = -1, a["ck"] = !0);
          d = a instanceof oE ? a["gk"] : "";
          d && Laya["Pool"]["recover"](d, a)
        }
      };
      continue
    } else if (h1 == b) {
      qz = function() {
        let a;
        a = class extends sl {
          constructor() {
            var a = hr,
              b = a[0];
            var c;
            c = arguments;
            super(...c), this["cP"] = hu[109], this["uP"] = 0
          }
          gameOver() {
            var a = hr;
            super["gameOver"](), this["cP"] = 0
          }
        };
        ! function() {
          "use strict";
          var b = hr,
            c = "defineProperty",
            oJ6 = "value",
            oJ7 = "enumerable",
            oJ8 = "configurable",
            oJ9 = "writable";
          Object["defineProperty"](a["prototype"], "update", {
            ["value"](a) {
              this["kP"](a)
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](a["prototype"], "kP", {
            ["value"](a) {
              var b = hr,
                c = b[0],
                d = b[3],
                e = "uP",
                f = "Point",
                g = "TEMP";
              this["uP"] += a, this["uP"] < this["cP"] || (Laya["Point"]["TEMP"]["x"] = 0, Laya["Point"]["TEMP"]["y"] = 0, this["props"]["localToGlobal"](Laya["Point"]["TEMP"]), oc["instance"]["event"](sS["jt"], this["nm"], Laya["Point"]["TEMP"]["x"], Laya["Point"]["TEMP"]["y"]), this["uP"] = 0)
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          })
        } ["bind"](a)();
        return a
      } ["bind"](this)["apply"]();
      continue
    } else if (h2 == b) {
      pU = function() {
        var a = hr;
        let b;
        b = class {
          constructor(a, b, c, d = 0) {
            var e = hr,
              f = e[0],
              g = e[2],
              h = "nm",
              i = "Kx",
              j = "fill";
            this["nm"] = !1, this["Kx"] = !1, this["type"] = a, this["nm"] = b, d ? (this["Kx"] = !0, this["Jx"] = Array["from"](new Array(c), () => new Array(d)["fill"](null))) : this["tb"] = Array(c)["fill"](null)
          }
        };
        ! function() {
          "use strict";
          var a = hr,
            c = a[0],
            d = a[5],
            e = "defineProperty",
            oKq = "get",
            oKr = "enumerable",
            oKs = "configurable",
            oKt = "value",
            oKu = "writable";
          Object["defineProperty"](b["prototype"], "sb", {
            ["get"]() {
              var a = hr,
                b = a[0];
              return this["Kx"] ? this["Jx"] : this["tb"]
            },
            ["enumerable"]: false,
            ["configurable"]: true
          });
          Object["defineProperty"](b["prototype"], "size", {
            ["get"]() {
              var a = hr,
                b = a[0],
                c = "Jx",
                d = "length";
              return this["Kx"] ? this["Jx"]["length"] * this["Jx"][0]["length"] : this["tb"]["length"]
            },
            ["enumerable"]: false,
            ["configurable"]: true
          });
          Object["defineProperty"](b["prototype"], "hb", {
            ["value"](a, b) {
              if (4 === this["type"]) {
                if (0 === b) {
                  if (0 === a || 1 === a) return a
                } else if (1 === b && a >= 0 && a < 6) return a + 2;
                return -1
              }
              return a
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](b["prototype"], "getItem", {
            ["value"](a, b = 0) {
              var c = hr,
                d = c[0],
                e = "Kx",
                f = "tb";
              if (4 === this["type"] && !this["Kx"]) {
                let e;
                e = this["hb"](a, b);
                return e < 0 || e >= this["tb"]["length"] ? null : this["tb"][e]
              }
              return this["Kx"] ? this["Jx"][a][b] : this["tb"][a]
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](b["prototype"], "setItem", {
            ["value"](a, b, c) {
              var d = hr,
                e = d[0],
                f = "Kx",
                g = "tb";
              if (4 === this["type"] && !this["Kx"]) {
                let f;
                f = this["hb"](b, c || 0);
                return void(f >= 0 && f < this["tb"]["length"] && (this["tb"][f] = a))
              }
              this["Kx"] ? this["Jx"][b][c] = a : this["tb"][b] = a
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](b["prototype"], "removeItem", {
            ["value"](a, b = 0) {
              var c = hr,
                d = c[0],
                e = "Kx",
                f = "tb";
              if (4 === this["type"] && !this["Kx"]) {
                let e;
                e = this["hb"](a, b);
                return void(e >= 0 && e < this["tb"]["length"] && (this["tb"][e] = null))
              }
              this["Kx"] ? this["Jx"][a][b] = null : this["tb"][a] = null
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](b["prototype"], "removeAll", {
            ["value"]() {
              var a = hr,
                b = a[0],
                c = "Jx",
                d = "length",
                e = "tb";
              if (this["Kx"])
                for (let a = 0; a < this["Jx"]["length"]; a++)
                  for (let b = 0; b < this["Jx"][a]["length"]; b++) this["Jx"][a][b] = null;
              else
                for (let a = 0; a < this["tb"]["length"]; a++) this["tb"][a] = null
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](b["prototype"], "eb", {
            ["value"](a) {
              var b = hr,
                c = b[0],
                d = "Jx",
                e = "length",
                f = "tb";
              if (this["Kx"]) {
                for (let b = 0; b < this["Jx"]["length"]; b++)
                  for (let c = 0; c < this["Jx"][b]["length"]; c++)
                    if (this["Jx"][b][c] === a) return {
                      ["x"]: b,
                      ["y"]: c
                    }
              } else
                for (let c = 0; c < this["tb"]["length"]; c++)
                  if (this["tb"][c] === a) {
                    if (4 === this["type"]) {
                      if (0 === c || 1 === c) return {
                        ["x"]: c,
                        ["y"]: 0
                      };
                      if (c >= 2 && c < 8) return {
                        ["x"]: c - 2,
                        ["y"]: 1
                      }
                    }
                    return {
                      ["x"]: c,
                      ["y"]: 0
                    }
                  } return null
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          })
        } ["bind"](b)();
        return b
      } ["bind"](this)["apply"](), na = function() {
        let a;
        a = class extends qU {
          constructor() {
            var a = hr,
              b = a[0];
            var c;
            c = arguments;
            super(...c), this["ab"] = new Map, this["nb"] = new Map
          }
          static hb(a, b) {
            if (0 === b) {
              if (0 === a || 1 === a) return a
            } else if (1 === b && a >= 0 && a < 6) return a + 2;
            return -1
          }
          static pb(a) {
            return 0 === a ? {
              ["x"]: 0,
              ["y"]: 0
            } : 1 === a ? {
              ["x"]: 1,
              ["y"]: 0
            } : a >= 2 && a < 8 ? {
              ["x"]: a - 2,
              ["y"]: 1
            } : null
          }
        };
        ! function() {
          "use strict";
          var b = hr,
            c = b[0],
            d = "defineProperty",
            oK3 = "value",
            oK4 = "enumerable",
            oK5 = "configurable",
            oK6 = "writable";
          Object["defineProperty"](a["prototype"], "init", {
            ["value"]() {
              var a = hr,
                b = "ob";
              this["ob"](!0), this["ob"](!1)
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](a["prototype"], "ob", {
            ["value"](a) {
              var b = hr,
                c = b[0],
                d = "instance",
                e = "map",
                f = "cb",
                g = "length";
              let h;
              h = uq["instance"]()["map"]["pe"];
              this["cb"](1, a, h["length"], h[0]["length"]), this["cb"](2, a, h["length"], h[0]["length"]), this["cb"](3, a, uq["instance"]()["map"]["fe"]), this["cb"](4, a, 6, 2), this["cb"](5, a, 2, 1)
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](a["prototype"], "cb", {
            ["value"](a, b, c, d = 0) {
              var e = hr,
                f = e[0];
