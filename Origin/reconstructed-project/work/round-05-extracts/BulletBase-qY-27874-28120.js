      qY = function() {
        var a = hr;
        let b;
        b = class {
          constructor(a) {
            var b = hr,
              c = b[0],
              oYy = "Hk",
              oYz = "Wk";
            this["Xk"] = {
              ["Gk"]: void 0,
              ["Hk"]: void 0,
              ["Wk"]: void 0
            }, this["jk"] = !1, this["zk"] = !1, this["Nk"] = !0, this["qk"] = [], this["$k"] = new Set, this["Hk"] = 0, this["Vk"] = !1, this["Wk"] = 1, this["Qk"] = new Laya["Point"], this["Zk"] = !1, this["Kk"] = ts["rk"], this["ak"] = !0, this["Jk"] = !1, a || (a = ""), this["tS"] = a
          }
        };
        ! function() {
          var a = hr,
            c = hu,
            d = a[0],
            e = a[6],
            f = a[1],
            g = a[2],
            h = a[5],
            i = c[57],
            j = c[1],
            k = c[19],
            l = c[10],
            m = c[3],
            n = c[13],
            o = c[7],
            p = c[5],
            q = c[4],
            r = c[8],
            s = c[11],
            t = c[9],
            u = c[14],
            v = c[2],
            w = c[12],
            x = c[6],
            y = "defineProperty",
            oYH = "value",
            oYI = "enumerable",
            oYJ = "configurable",
            oYK = "writable",
            oYL = "get",
            oYM = "set";
          let z = 0,
            A = u8;
          w1_cT: while (z < c[37]) {
            ++z;
            switch (A) {
              case 8:
                Object["defineProperty"](b["prototype"], "vS", {
                  ["value"](a) {
                    var b = hr,
                      c = b[0];
                    return this["_S"](a) && !this["qk"]["some"](b => !b["kS"](a))
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                A = i;
                break;
              case 4:
                Object["defineProperty"](b["prototype"], "MS", {
                  ["value"]() {},
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                A = j;
                break;
              case k:
                Object["defineProperty"](b["prototype"], "oS", {
                  ["value"]() {
                    var a = hr;
                    this["eS"]["visible"] = !1
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                A = l;
                break;
              case 6:
                Object["defineProperty"](b["prototype"], "y", {
                  ["get"]() {
                    return this["eS"]["y"]
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true
                });
                A = 2;
                break;
              case m:
                Object["defineProperty"](b["prototype"], "sS", {
                  ["get"]() {
                    var a = hr,
                      b = a[0],
                      c = "iS",
                      d = "hS";
                    return this["jk"] || (this["iS"] || (this["hS"] = 0), this["hS"] = this["iS"]["_p"]), this["hS"]
                  },
                  ["set"](a) {
                    var b = hr,
                      c = b[0];
                    this["jk"] = !0, this["hS"] = a
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true
                });
                A = n;
                break;
              case o:
                Object["defineProperty"](b["prototype"], "off", {
                  ["value"](a, b) {
                    var c = hr;
                    return this["eS"]["off"](a, b)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                A = p;
                break;
              case 0:
                Object["defineProperty"](b["prototype"], "_S", {
                  ["value"](a) {
                    return !0
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                A = 4;
                break;
              case q:
                Object["defineProperty"](b["prototype"], "cS", {
                  ["value"](a, b = !1) {
                    var c = hr,
                      d = c[0];
                    return a["uS"] = b, this["qk"]["push"](a), a
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                A = 3;
                break;
              case 2:
                Object["defineProperty"](b["prototype"], "pos", {
                  ["value"](a, b) {
                    var c = hr;
                    return this["eS"]["pos"](a, b)
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                A = 5;
                break;
              case r:
                Object["defineProperty"](b["prototype"], "bS", {
                  ["value"]() {
                    var a = hr,
                      b = a[0];
                    this["MS"](), this["eS"]["event"](n4["Ok"])
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                A = s;
                break;
              case 10:
                Object["defineProperty"](b["prototype"], "resetData", {
                  ["value"](a) {
                    var b = hr,
                      c = b[0],
                      d = "zk",
                      e = "iS",
                      f = "sS",
                      g = "fS",
                      h = "gS",
                      i = "create";
                    if (this["zk"] = !1, this["Jk"] = !1, this["rS"] = !1, this["iS"] = a["iS"], void 0 !== a["sS"] && (this["sS"] = a["sS"]), void 0 !== a["fS"] && (this["Wk"] = a["fS"]), !1 === this["onReset"](a)) return this["zk"] = !0, this["gS"] = nN["create"](), void this["aS"](!0);
                    a["gS"] ? this["gS"] = a["gS"] : this["gS"] = nN["create"](), this["gS"]["dS"] = this, this["gS"]["A_"]()
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                A = 7;
                break;
              case j:
                Object["defineProperty"](b["prototype"], "gameOver", {
                  ["value"]() {
                    this["dispose"]()
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                A = t;
                break;
              case l:
                Object["defineProperty"](b["prototype"], "lS", {
                  ["value"]() {
                    var a = hr;
                    this["eS"]["visible"] = !0
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                A = q;
                break;
              case t:
                "use strict";
                A = m;
                break;
              case 7:
                Object["defineProperty"](b["prototype"], "LS", {
                  ["value"]() {
                    var a = hr,
                      b = a[0],
                      c = a[3],
                      d = "mS",
                      e = "wS",
                      f = "eS";
                    this["mS"] || this["Jk"] || this["zk"] || (this["mS"] = !0, this["rS"] || (this["wS"](), this["qk"]["forEach"](a => a["wS"](this)), this["eS"]["event"](n4["Rk"]), this["Qk"]["setTo"](this["eS"]["x"], this["eS"]["y"]), this["gS"]["kk"]()))
                  },
                  ["enumerable"]: false,
                  ["configurable"]: true,
                  ["writable"]: true
                });
                A = 8;
                break;
              case u:
                Object["defineProperty"](b["prototype"], "init", {
                  ["value"]() {
                    var a = hr,
                      b = a[0];
                    this["yS"] = this["Kk"], this["A_"]()
                  },
