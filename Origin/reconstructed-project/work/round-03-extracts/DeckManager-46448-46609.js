    vN = function() {
      var a = hr;
      let b = class extends qU {
        constructor() {
          var a = hr,
            b = a[0];
          var c = arguments;
          super(...c), this["vO"] = !1, this["_O"] = []
        }
      };
      ! function() {
        "use strict";
        var a = hr,
          c = a[0],
          d = "defineProperty",
          qql = "value",
          qqm = "enumerable",
          qqn = "configurable",
          qqo = "writable";
        Object["defineProperty"](b["prototype"], "init", {
          ["value"]() {
            var a = hr,
              b = a[0],
              c = "instance",
              d = "eh";
            this["kO"] = uq["instance"]()["eh"]["ah"], this["SO"] = uq["instance"]()["eh"]["nh"]
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "startGame", {
          ["value"]() {
            var a = hr,
              b = a[0],
              c = "eh",
              d = "slice",
              e = "SO",
              f = "push";
            const g = uq["instance"]();
            this["kO"] = g["eh"]["ah"]["slice"](), this["SO"] = g["eh"]["nh"]["slice"](), g["Oc"]["Yc"]["forEach"]((c, d) => {
              var e = hr;
              let g = !0;
              for (let b = 0; b < c["length"]; b++) this["kO"]["includes"](c[b]) || (g = !1);
              g && this["_O"]["push"](d)
            });
            const h = g["My"]["oi"][g["au"]["Si"]];
            if (h > 0)
              for (let a = 0; a < h; a++) this["SO"]["push"]("铲");
            this["xO"]()
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "bO", {
          ["value"](a) {
            var b = hr,
              c = b[0],
              d = b[4],
              e = "instance",
              f = "au",
              g = "Li",
              h = "length",
              i = "range",
              j = "splice";
            if (uq["instance"]()["au"]["Li"]["length"] >= 2) return uq["instance"]()["au"]["Li"][np["range"](0, uq["instance"]()["au"]["Li"]["length"], !0)];
            let k = a ? this["kO"] : this["SO"];
            if (!k || 0 === k["length"]) return "刀";
            let l = np["range"](0, k["length"], !0),
              m = k[l];
            if ("刀" != k[l] && "枪" != k[l] && "弓" != k[l] && "骑" != k[l] && "铲" != k[l] && "农" != k[l] && (k["splice"](l, 1), a && uq["instance"]()["au"]["Fi"] || !a && uq["instance"]()["au"]["Oi"])) {
              const a = k["indexOf"](m);
              a >= 0 && k["splice"](a, 1)
            }
            return m
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "xO", {
          ["value"]() {
            var a = hr,
              b = a[6],
              c = a[4],
              d = "kO",
              e = "push";
            if (uq["instance"]()["player"]["roundDay"] > 3) return;
            let f = 0;
            for (let a = 0; a < this["kO"]["length"]; a++) "铲" == this["kO"][a] && f++;
            f = Math["floor"](f / 5);
            for (let b = 0; b < f; b++) this["kO"]["push"]("铲"), this["SO"]["push"]("铲")
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "dP", {
          ["value"](a) {
            var b = hr,
              c = b[0],
              d = "length",
              e = "instance",
              f = "au";
            let g = ["刀", "弓", "枪", "骑", "铲", "农"],
              h = a ? this["kO"] : this["SO"],
              i = h["length"],
              j = !0;
            for (let a = 0; a < i; a++) {
              j = !0;
              for (let b = 0; b < g["length"]; b++)
                if (h[a] == g[b]) {
                  j = !1;
                  break
                } j && Math["random"]() < .5 && h["push"](h[a])
            }
            a ? uq["instance"]()["au"]["Fi"] = !0 : uq["instance"]()["au"]["Oi"] = !0
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "gameOver", {
          ["value"]() {
            var a = hr,
              b = a[0];
            this["kO"] = null, this["SO"] = null, this["_O"] = []
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "MO", {
          ["value"](a) {
            var b = hr;
            return -1 !== uq["instance"]()["player"]["mergedGenerals"]["indexOf"](a)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "PO", {
          ["value"](a) {
            var b = hr,
              c = b[0],
              d = "NE",
              qqZ = "description";
            const e = vM["iE"](a),
              f = e["NE"] ? e["NE"][0] : null;
            return vM["ZU"](e), f ? {
              ["skillName"]: f["name"],
              ["description"]: f["description"]
            } : null
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        })
      } ["bind"](b)();
      return b
    } ["bind"](this)["apply"](),
