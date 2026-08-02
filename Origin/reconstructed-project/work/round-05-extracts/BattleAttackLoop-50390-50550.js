          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "vH", {
          ["value"]() {
            var a = hr,
              b = a[0],
              c = a[3],
              d = "dH",
              e = "li",
              f = "gH",
              g = "event",
              h = "sw",
              i = "enemy",
              j = "rh",
              k = "length",
              l = "pH",
              m = "kH";
            this["cH"] = 0, this["dH"]["li"] += 1, this["gH"]["event"](sS["Ft"], !0), this["gH"]["event"](sS["Jt"]), vT["instance"]()["HG"] || this["sx"]["jB"](), this["dH"]["ci"] ? this["dH"]["li"] <= this["sw"]["enemy"]["rh"]["length"] ? this["pH"] = this["sw"]["enemy"]["rh"][this["dH"]["li"] - 1] : this["pH"] = this["sw"]["enemy"]["rh"][this["sw"]["enemy"]["rh"]["length"] - 1] + 2 * (this["dH"]["li"] - this["sw"]["enemy"]["rh"]["length"]) : this["pH"] = this["sw"]["enemy"]["rh"][this["dH"]["li"] - 1], this["yH"] = this["kH"](), this["fH"] = this["kH"]()
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "_H", {
          ["value"]() {
            var a = hr,
              b = a[0],
              c = "cH",
              d = "sx",
              e = "jL",
              f = "sw",
              g = "map",
              h = "oe",
              i = "uH";
            this["cH"] < this["lH"] || (this["cH"] = 0, this["sx"]["jL"](this["sw"]["map"]["oe"], !0, this["yH"] === this["uH"]), this["sx"]["jL"](this["sw"]["map"]["oe"], !1, this["fH"] === this["uH"]), this["uH"] += 1, this["uH"] >= this["pH"] && (this["uH"] = 0, this["rH"] = 3))
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "kH", {
          ["value"]() {
            var a = hr,
              b = a[0];
            return qx["instance"]()["hu"]() ? np["range"](0, this["pH"], !0) : -1
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "LH", {
          ["value"]() {
            var a = hr,
              b = a[0],
              c = "PA",
              d = "instance",
              e = "BM";
            this["PA"] = vc["instance"]()["PA"], this["BM"] = vc["instance"]()["BM"]
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "wH", {
          ["value"](a) {
            var b = hr,
              c = hu,
              d = b[0],
              e = b[4],
              f = c[123],
              g = "values",
              h = "q_",
              i = "__",
              j = "Oc",
              k = "width",
              l = "height",
              m = "UnitAttack",
              n = "currentState",
              o = "lx",
              p = "sx",
              q = "qx",
              r = "wp",
              s = "nm",
              t = "length",
              u = "Wm",
              v = "z_",
              w = "changeState",
              x = "UnitIdle",
              y = "attack",
              z = "general";
            const A = Date["now"]();
            for (let a of this["PA"]["values"]()) {
              if (!a["q_"] || a["__"]) continue;
              const b = a["Oc"]["x"] + a["Oc"]["width"] / 2,
                c = a["Oc"]["y"] + a["Oc"]["height"] / 2;
              if ("UnitAttack" != a["currentState"]) a["lx"] = this["sx"]["qx"](b, c, a["wp"], a["nm"]), a["lx"] && a["lx"]["length"] > 0 && A - a["Wm"] >= f * a["z_"] && a["changeState"]("UnitAttack");
              else if ("UnitAttack" == a["currentState"]) {
                if (a["__"]) {
                  a["changeState"]("UnitIdle");
                  continue
                }
                if (A - a["Wm"] >= f * a["z_"]) {
                  if (a["Wm"] = A, a["lx"] = this["sx"]["qx"](b, c, a["wp"], a["nm"]), !a["lx"] || a["lx"]["length"] <= 0) {
                    a["changeState"]("UnitIdle");
                    continue
                  }
                  a["attack"]()
                }
              }
            }
            for (let a of this["BM"]["values"]()) {
              if (!a["q_"]) continue;
              const c = a["general"]["x"] + a["general"]["width"] / 2,
                d = a["general"]["y"] + a["general"]["height"] / 2;
              if ("UnitAttack" != a["currentState"]) a["lx"] = this["sx"]["qx"](c, d, a["wp"], a["nm"]), a["lx"] && a["lx"]["length"] > 0 && A - a["Wm"] >= f * a["z_"] && a["changeState"]("UnitAttack");
              else if ("UnitAttack" == a["currentState"] && A - a["Wm"] >= f * a["z_"]) {
                if (a["Wm"] = A, a["lx"] = this["sx"]["qx"](c, d, a["wp"], a["nm"]), !a["lx"] || a["lx"]["length"] <= 0) {
                  a["changeState"]("GeneralIdle");
                  continue
                }
                a["attack"]()
              }
            }
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "gameOver", {
          ["value"]() {
            var a = hr,
              b = a[6],
              c = a[0];
            Laya["timer"]["clearAll"](this), nx["instance"]()["wa"]("BattleMgr"), this["dH"]["li"] = 0, this["cH"] = 0, this["uH"] = 0, this["pH"] = 0
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        })
      } ["bind"](b)();
      return b
    } ["bind"](this)["apply"]();
  w1_dO: for (let f of lL) {
    if (hk && bW) {
      gS += "_hasUsedFreeBulldozer"
    }
    if (hk && bY) {
      gM = "随机道具"
    }
    if (hk && b9) {
      gR = 659
    }
    if (hk && bI) {
      gM += "aB"
    }
    if (hk && bG) {
      gS += "MH"
    }
