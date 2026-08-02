      pe = function() {
        let a;
        a = class extends ro {
          constructor() {
            var a = hr,
              b = a[0];
            var c;
            c = arguments;
            super(...c), this["QE"] = {
              ["ug"]: null,
              ["p1"]: null,
              ["p2"]: null,
              ["time"]: 0
            }, this["ZE"] = 0
          }
          init(a) {
            var b = hr,
              c = b[2],
              d = b[0],
              e = "Qm",
              f = "visible",
              g = "iw",
              h = "ew",
              i = "width";
            super["init"](a), this["KE"](), this["Qm"]["text"] = this["Zi"]["toFixed"](0), this["Qm"]["visible"] = !1, this["iw"]["visible"] = !1, this["kw"](), this["enemy"]["visible"] = !1, this["Hw"](() => {
              this["changeState"](1), this["ew"]["width"] = 0, this["iw"]["visible"] = !0, this["ew"]["width"] = this["aw"]
            })
          }
          hit(a, b) {
            var c = hr,
              d = hu,
              e = c[1],
              f = c[0],
              g = c[6],
              h = d[22],
              i = d[45],
              j = "tw",
              k = "to",
              l = "rotation",
              m = "duration",
              n = "chain";
            super["hit"](a, b), this["tw"] && Laya["Tween"]["create"](this["tw"])["to"]("rotation", Math["max"](-h, 5 * -a))["duration"](i)["chain"]()["to"]("rotation", Math["min"](h, 5 * a))["duration"](i)["chain"]()["to"]("rotation", 0)["duration"](i)
          }
          Lw() {
            var a = hr,
              b = a[0],
              c = a[5],
              d = a[3],
              e = a[4],
              f = a[1],
              g = "type",
              h = "instance",
              i = "enemy",
              j = "width",
              k = "height",
              pei = "alpha",
              l = "au";
            let m;
            if (this["Cm"]) return;
            super["Lw"]();
            m = "#000000";
            4 == this["type"] && (m = "#c1f6cb"), qs["instance"]()["Ag"](this["enemy"]["parent"], this["enemy"]["x"] + this["enemy"]["width"] / 2, this["enemy"]["y"] + this["enemy"]["height"] / 2, m, 1), Laya["Tween"]["to"](this["enemy"], {
              ["alpha"]: 0
            }, hu[81], null, Laya["Handler"]["create"](this, () => {
              if (this["enemy"]["alpha"] = 1, this["enemy"]["visible"] = !1, 1 != this["type"]) {
                let a;
                a = this["nm"] ? uq["instance"]()["au"]["Ii"] : uq["instance"]()["au"]["Ti"];
                a["Ci"] && a["num"] < 3 && np["bs"](a["pos"], {
                  ["x"]: this["enemy"]["x"] + this["enemy"]["width"] / 2,
                  ["y"]: this["enemy"]["y"] + this["enemy"]["height"] / 2
                }) < a["range"] && this["sB"]()
              }
              this["gameOver"]()
            }))
          }
          gameOver() {
            var a = hr,
              b = a[6],
              c = a[0],
              d = a[5],
              e = a[3],
              f = "tw";
            nx["instance"]()["wa"]("blownUp"), Laya["Tween"]["killAll"](this["tw"]), super["gameOver"](), this["enemy"]["filters"] = null, this["ZE"] = 0, this["tw"]["scale"](1, 1), this["tw"]["skewX"] = 0, this["tw"]["alpha"] = 1, this["tw"]["rotation"] = 0, this["tw"]["removeSelf"](), this["tw"]["recover"](), this["tw"] = null
          }
        };
        ! function() {
          "use strict";
          var b = hr,
            c = b[0],
            d = "defineProperty",
            pet = "enumerable",
            peu = "configurable",
            pev = "value",
            pew = "writable";
          Object["defineProperty"](a["prototype"], "On", {
            ["get"]() {
              var a = hr,
                b = a[0],
                c = "Sm",
                d = "bm";
              let e;
              e = this["Sm"] + this["xm"];
              return this["bm"] = e / this["Sm"], this["tw"]["bm"](this["bm"]), e
            },
            ["enumerable"]: false,
            ["configurable"]: true
          });
          Object["defineProperty"](a["prototype"], "KE", {
            ["value"]() {
              var a = hr,
                b = a[6],
                c = a[0],
                d = a[2],
                e = a[36],
                f = "type",
                g = "Zi",
                h = "ph",
                i = "ew",
                j = "skin",
                k = "tw",
                l = "enemy",
                m = "sp";
              let n;
              n = uq["instance"]()["Dy"](this["type"], this["nm"]);
              4 == this["type"] ? (this["Zi"] = n["ph"] / 2, this["ew"]["skin"] = "resources/img/gameObject/enemy/hp3.png") : (this["Zi"] = n["ph"], this["ew"]["skin"] = "resources/img/gameObject/enemy/hp2.png"), this["Km"] = n["ph"], this["tw"] = this["enemy"]["getChildByName"]("sp"), this["tw"] || (this["tw"] = new ve(this["lm"], this["JE"]), this["tw"]["name"] = "sp", this["enemy"]["addChild"](this["tw"])), this["tw"]["play"]("animation", !0)
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](a["prototype"], "tB", {
            ["value"]() {
              var a = hr,
                b = hu,
                c = a[0],
                d = b[171],
                e = "to",
                f = "scaleY",
                g = "duration",
                h = "chain";
              Laya["Tween"]["create"](this["tw"])["to"]("scaleY", .98)["duration"](d)["chain"]()["to"]("scaleY", 1.02)["duration"](d)["chain"]()["to"]("scaleY", 1)["duration"](d)["then"](this["tB"], this)
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](a["prototype"], "sB", {
            ["value"]() {
              var a = hr,
                b = a[0],
                c = a[3],
                d = a[4],
                e = "nm",
                f = "instance",
                g = "au",
                h = "Vy",
                i = "pos",
                j = "enemy",
                k = "localToGlobal",
                l = "Qy";
              let m, n, o, p;
              m = this["nm"] ? uq["instance"]()["au"]["Ii"] : uq["instance"]()["au"]["Ti"];
              this["Vy"]["x"] = m["pos"]["x"], this["Vy"]["y"] = m["pos"]["y"], this["enemy"]["parent"]["localToGlobal"](this["Vy"]), this["Qy"]["x"] = this["enemy"]["width"] / 2, this["Qy"]["y"] = this["enemy"]["height"], this["enemy"]["localToGlobal"](this["Qy"]);
              n = this["Lm"], o = this["enemy"]["x"], p = this["enemy"]["y"];
              qs["instance"]()["vg"](this["Vy"]["x"], this["Vy"]["y"], this["Qy"]["x"], this["Qy"]["y"], hu[167], () => {
                oc["instance"]["event"](sS["ut"], this["nm"], o, p, n)
              }, "#05fe77", "resources/img/gameObject/enemy/soulHead.png")
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](a["prototype"], "Xw", {
            ["value"](a, b, c) {
              var d = hr,
                e = d[3],
                f = d[0],
                g = d[6],
                h = "enemy",
                i = "QE",
                j = "ug",
                k = "p2";
              let l, m;
              m = this["enemy"]["x"] + this["enemy"]["width"] / 2, l = this["enemy"]["y"] + this["enemy"]["height"] / 2;
              this["QE"]["ug"] = {
                ["x"]: this["enemy"]["x"],
                ["y"]: this["enemy"]["y"]
              }, this["QE"]["p2"] = {
                ["x"]: this["enemy"]["x"] + (m - b) / 2,
                ["y"]: this["enemy"]["y"] + (l - c) / 2
              }, this["QE"]["p1"] = {
                ["x"]: this["QE"]["ug"]["x"] + (this["QE"]["p2"]["x"] - this["QE"]["ug"]["x"]) / 2,
                ["y"]: this["QE"]["ug"]["y"] - 3 * (hu[61] - a)
              }, this["QE"]["time"] = 0, this["ZE"] = 1, this["hit"](this["Zi"] - .1, null), this["tw"]["rotation"] = np["angle"]({
                ["x"]: b,
                ["y"]: c
              }, {
                ["x"]: m,
                ["y"]: l
              }), nx["instance"]()["La"]("blownUp" + this["id"], this, this["Gw"])
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          });
          Object["defineProperty"](a["prototype"], "Gw", {
            ["value"](a) {
              var b = hr,
                c = b[0],
                d = "ZE",
                e = "QE",
                f = "time";
              1 == this["ZE"] && (this["QE"]["time"] += a / hu[132], np["Us"](this["QE"]["ug"], this["QE"]["p1"], this["QE"]["p2"], this["enemy"], this["QE"]["time"]) && (this["hit"](1, null), this["ZE"] = 0))
            },
            ["enumerable"]: false,
            ["configurable"]: true,
            ["writable"]: true
          })
        } ["bind"](a)();
        return a
      } ["bind"](this)["apply"]();
