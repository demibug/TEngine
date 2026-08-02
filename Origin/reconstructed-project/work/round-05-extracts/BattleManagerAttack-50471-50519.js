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
