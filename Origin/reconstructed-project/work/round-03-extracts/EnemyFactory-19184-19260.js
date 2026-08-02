      s0 = function() {
        let a;
        a = class b {
          constructor() {
            this["HL"] = new Map
          }
          static instance() {
            var a = hr,
              c = "_instance";
            return this["_instance"] || (this["_instance"] = new b, this["_instance"]["init"]()), this["_instance"]
          }
          register(a, b) {
            var c = hr;
            this["HL"]["set"](a, b)
          }
          WL(a) {
            var b = hr;
            let c;
            c = this["HL"]["get"](a);
            if (!c) throw new Error(`EnemyFactory: 未为类型 ${a} 注册创建器`);
            return c()
          }
          produce(a) {
            var b = hr;
            return Laya["Pool"]["createByClass"](a)
          }
          recover(a) {
            var b = hr;
            Laya["Pool"]["recoverByClass"](a)
          }
        };
        ! function() {
          "use strict";
          var b = hr;
          Object["defineProperty"](a["prototype"], "init", {
            ["value"]() {},
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          })
        } ["bind"](a)();
        return a
      } ["bind"](this)["apply"](), ss = class {
        static jL(a) {
          var b = hr;
          return s0["instance"]()["WL"](a)
        }
        static zL() {
          return [...this["NL"]]
        }
        static qL() {
          return [...this["$L"]]
        }
      };
      continue
    } else if (4 == b) {
      continue
    } else if (5 == b) {
      ss["NL"] = ["Mob0", "Mob1", "Mob2", "Mob3", "Zombie", "Cavalry", "Puppet"], ss["$L"] = ["ZhangLiang", "ZhangBao", "ZhangJiao", "SunShangXiang", "ZhenFu", "DiaoChan", "HuaXiong", "LvBu", "DongZhuo", "DianWei", "XiaHouDun", "CaoCao"];
      continue
    } else if (6 == b) {
      qe = function() {
        var a = hr;
        let b;
        b = class c extends Laya["Sprite"] {
          constructor(a) {
            var b = hr,
              c = hu,
              d = b[0],
              e = b[5],
              f = b[2],
              g = b[8],
              h = c[65],
              i = "animId",
              j = "frameImg";
            let k;
            super(), this["Ud"] = "", this["Fd"] = "", this["playing"] = !1, this["loop"] = !0, this["Id"] = 1, this["Od"] = 1, this["Yd"] = 0, this["segmentEndMs"] = -1, this["Xd"] = 0, this["frameIndex"] = -1, this["Gd"] = !1, this["autoAdjust"] = !1, this["activeClip"] = null, this["Hd"] = 1, this["Wd"] = 0, this["jd"] = 0, this["zd"] = 1, this["Nd"] = "", this["animId"] = String(a);
