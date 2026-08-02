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
