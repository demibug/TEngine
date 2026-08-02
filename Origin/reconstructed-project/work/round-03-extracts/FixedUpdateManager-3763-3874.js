        } ["bind"](this)["apply"](), qU = class {
          static instance() {
            var a = hr,
              b = "Instance";
            return this["Instance"] || (this["Instance"] = new this), this["Instance"]
          }
        }, pV = function() {
          var a = hr;
          let b;
          b = class c extends qU {
            constructor() {
              var a = hr,
                b = a[0];
              var c;
              c = arguments;
              super(...c), this["ya"] = !1, this["delta"] = 0, this["fa"] = 0, this["ga"] = new Map, this["da"] = 0, this["serverTime"] = 0
            }
            La(a, b, c) {
              var d = hr,
                e = d[1],
                f = "ga";
              this["ga"]["has"](a) && this["ga"]["delete"](a), this["ga"]["set"](a, function() {
                var a = hr,
                  m87 = "ma",
                  m88 = "caller";
                let d;
                d = {
                  ["ma"]: 0,
                  ["caller"]: 0
                };
                d["ma"] = c;
                d["caller"] = b;
                return d
              } ["apply"]())
            }
            wa(a) {
              var b = hr;
              this["ga"]["delete"](a)
            }
            update() {
              var a = hr,
                b = a[0],
                d = a[3],
                e = "da",
                f = "min";
              let g, h;
              if (this["ya"]) return;
              h = Laya["timer"]["currTimer"];
              g = h - this["da"];
              if (!(g <= 0)) {
                for (g = Math["min"](g, c["va"]), this["delta"] = g; g > 0;) {
                  let d;
                  d = Math["min"](c["_a"], g);
                  for (let c of this["ga"]) c[1]["ma"]["call"](c[1]["caller"], d);
                  this["fa"] += d, g -= d
                }
                this["da"] = h
              }
            }
            pause(a = !0) {
              var b = hr,
                c = b[3];
              this["ya"] = !0, a && Laya["timer"]["pause"]()
            }
          };
          ! function() {
            "use strict";
            var a = hr,
              c = a[0],
              d = "defineProperty",
              m9k = "value",
              m9l = "enumerable",
              m9m = "configurable",
              m9n = "writable";
            Object["defineProperty"](b["prototype"], "init", {
              ["value"]() {
                var a = hr;
                Laya["timer"]["frameLoop"](1, this, this["update"]), this["da"] = 0
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            });
            Object["defineProperty"](b["prototype"], "resume", {
              ["value"]() {
                var a = hr;
                Laya["timer"]["resume"](), this["ya"] = !1
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            });
            Object["defineProperty"](b["prototype"], "ka", {
              ["value"]() {
                var a = hr;
                return np["Gs"](uq["instance"]()["player"]["registerTime"], Date["now"]()) + 1
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            });
            Object["defineProperty"](b["prototype"], "Sa", {
              ["value"]() {
                return (new Date)["getDay"]()
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            })
          } ["bind"](b)();
          return b
        } ["bind"](this)["apply"]();
