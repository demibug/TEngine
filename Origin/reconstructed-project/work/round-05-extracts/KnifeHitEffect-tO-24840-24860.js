      tO = class extends r6 {
        constructor() {
          var a;
          a = arguments;
          super(...a), this["Ex"] = !1
        }
        onReset(a) {
          super["onReset"](a)
        }
        Cw(a) {
          var b = hr,
            c = b[0],
            d = "instance",
            e = "width",
            f = "height";
          let g, h;
          super["Cw"](a);
          h = this["Px"](a), g = a["enemy"];
          this["Ex"] ? qs["instance"]()["Tg"](g, g["width"] / 2, g["height"] / 2, -h) : qs["instance"]()["Dg"](g, g["width"] / 2, g["height"] / 2, -h)
        }
      };
