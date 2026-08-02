    uo = function() {
      let a = class {
        constructor() {
          var a = hr,
            b = hu,
            c = a[0],
            d = a[3],
            e = a[2],
            f = a[1],
            g = b[1],
            m7r = "Ci",
            m7s = "num",
            m7t = "range",
            m7u = "pos";
          this["li"] = 0, this["ci"] = !1, this["ui"] = g, this["pi"] = [], this["yi"] = g, this["fi"] = 10, this["gi"] = 10, this["di"] = !1, this["Li"] = "无", this["mi"] = 3, this["wi"] = 3, this["_gold"] = 0, this["props"] = null, this["ki"] = !0, this["Si"] = 0, this["xi"] = 1, this["bi"] = 3, this["Mi"] = 3, this["Pi"] = 0, this["Ai"] = {
            ["id"]: 0,
            ["rank"]: "军士.壹",
            ["level"]: 0,
            ["Ei"]: [],
            ["Bi"]: 0,
            ["win"]: 0,
            ["lose"]: 0,
            ["Di"]: 0
          }, this["Ii"] = {
            ["Ci"]: !1,
            ["num"]: 0,
            ["range"]: 0,
            ["pos"]: {
              ["x"]: 0,
              ["y"]: 0
            }
          }, this["Ti"] = {
            ["Ci"]: !1,
            ["num"]: 0,
            ["range"]: 0,
            ["pos"]: {
              ["x"]: 0,
              ["y"]: 0
            }
          }, this["Ri"] = !1, this["Ui"] = !1, this["Fi"] = !1, this["Oi"] = !1, this["delayTime"] = b[100], this["Yi"] = !1, this["Xi"] = !1, this["Gi"] = !1, this["Hi"] = !1, this["Wi"] = !1, this["ji"] = [], this["zi"] = [], this["Ni"] = [], this["qi"] = [], this["$i"] = {}, this["Vi"] = {}, this["Qi"] = !1
        }
      };
      ! function() {
        "use strict";
        var b = hr,
          c = b[0],
          d = b[1],
          e = "defineProperty",
          m7z = "get",
          m7A = "set",
          m7B = "enumerable",
          m7C = "configurable",
          m7D = "value",
          m7E = "writable";
        Object["defineProperty"](a["prototype"], "Zi", {
          ["get"]() {
            return this["mi"]
          },
          ["set"](a) {
            var b = hr,
              c = "mi",
              d = "instance",
              e = "event";
            let f = a - this["mi"];
            this["mi"] = a, oc["instance"]["event"](sS["Ct"], !0, f), this["mi"] <= 0 && oc["instance"]["event"](sS["l"], !1)
          },
          ["enumerable"]: false,
          ["configurable"]: true
        });
        Object["defineProperty"](a["prototype"], "gold", {
          ["get"]() {
            return this["_gold"]
          },
          ["set"](a) {
            var b = hr,
              c = b[3];
            this["_gold"] = a, oc["instance"]["event"](sS["Dt"])
          },
          ["enumerable"]: false,
          ["configurable"]: true
        });
        Object["defineProperty"](a["prototype"], "Ki", {
          ["get"]() {
            return this["bi"]
          },
          ["set"](a) {
            var b = hr,
              c = b[0],
              d = "bi",
              e = "instance",
              f = "event";
            if (!this["ki"]) return;
            let g = a - this["bi"];
            this["bi"] = a, oc["instance"]["event"](sS["Ct"], !1, g), this["bi"] <= 0 && oc["instance"]["event"](sS["l"], !0)
          },
          ["enumerable"]: false,
          ["configurable"]: true
        });
        Object["defineProperty"](a["prototype"], "Ji", {
          ["get"]() {
            return this["Pi"]
          },
          ["set"](a) {
            this["Pi"] = a
          },
          ["enumerable"]: false,
          ["configurable"]: true
        });
        Object["defineProperty"](a["prototype"], "startGame", {
          ["value"]() {
            var a = hr,
              b = a[0],
              c = "delayTime";
            this["Qi"] = !1, this["ki"] ? this["delayTime"] = hu[100] : this["delayTime"] = 0
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](a["prototype"], "gameOver", {
          ["value"]() {
            var a = hr,
              b = a[0],
              c = a[3],
              d = "Ci",
              e = "length";
            this["fi"] = 10, this["gi"] = 10, this["li"] = 0, this["mi"] = this["wi"], this["_gold"] = 0, this["props"] = null, this["bi"] = this["Mi"], this["Pi"] = 0, this["Ii"]["Ci"] = !1, this["Ti"]["Ci"] = !1, this["Ri"] = !1, this["Ui"] = !1, this["Fi"] = !1, this["Oi"] = !1, this["Yi"] = !1, this["Xi"] = !1, this["xi"] = 1, this["Gi"] = !1, this["Hi"] = !1, this["Wi"] = !1, this["zi"]["length"] = 0, this["Ni"]["length"] = 0, this["qi"]["length"] = 0, this["$i"] = {}, this["Vi"] = {}
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        })
      } ["bind"](a)();
      return a
    } ["bind"](this)["apply"](),
