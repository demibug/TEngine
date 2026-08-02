      oc = ow, np = class {
        static bs(a, b) {
          var c = hr,
            d = "pow";
          return Math["sqrt"](Math["pow"](a["x"] - b["x"], 2) + Math["pow"](a["y"] - b["y"], 2))
        }
        static Ms(a, b) {
          var c = hr,
            d = "pow";
          return Math["pow"](a["x"] - b["x"], 2) + Math["pow"](a["y"] - b["y"], 2)
        }
        static range(a, b, c = !1) {
          var d = hr,
            e = d[3],
            f = "random";
          return b < a ? (console["error"](`[MathE].range(): 错误的输入! [${a},${b})`), null) : c ? Math["floor"](a + (b - a) * Math["random"]()) : a + (b - a) * Math["random"]()
        }
        static Ps(a) {
          var b = hr,
            c = "length",
            d = "error";
          let e, f, g;
          if (!a || 0 === a["length"]) return console["error"]("[MathE.weightedRandom]: 权重数组不能为空"), -1;
          f = 0;
          for (let d = 0; d < a["length"]; d++) a[d] < 0 ? console["warn"](`[MathE.weightedRandom]: 权重值不能为负数，索引${d}的权重${a[d]}将被忽略`) : f += a[d];
          if (f <= 0) return console["error"]("[MathE.weightedRandom]: 所有权重值都为0或负数"), -1;
          e = Math["random"]() * f;
          g = 0;
          for (let b = 0; b < a["length"]; b++)
            if (!(a[b] < 0) && (g += a[b], e <= g)) return b;
          return a["length"] - 1
        }
        static As(a) {
          var b = hr,
            c = "length";
          let d, e;
          d = 0;
          for (let b = 0; b < a["length"]; b++) d += a[b];
          e = Math["random"]() * d;
          d = 0;
          for (let b = 0; b < a["length"]; b++)
            if (d += a[b], e <= d) return b
        }
        static Es(a, b, c, d, e, f, g) {
          var h = hr,
            i = h[1],
            j = "max",
            k = "min";
          let l, m, n, o, p, q;
          a -= 1;
          o = d, n = d + f, l = e, p = e + g, m = b - Math["max"](o, Math["min"](b, n)), q = c - Math["max"](l, Math["min"](c, p));
          return m * m + q * q <= a * a
        }
        static Bs(a, b, c, d, e) {
          var f = hr,
            g = f[1];
          var h, i, j, k, l;
          k = e * Math["PI"] / hu[95], i = Math["cos"](k), h = Math["sin"](k), l = c / 2, j = d / 2;
          return {
            ["x"]: a + l * i - j * h,
            ["y"]: b + l * h + j * i
          }
        }
        static angle(a, b) {
          var c = hr,
            d = hu;
          let e, f;
          e = b["x"] - a["x"], f = a["y"] - b["y"];
          if (0 === e) return f >= 0 ? 0 : d[95];
          if (0 === f) return e > 0 ? d[88] : d[96];
          return Math["atan2"](e, f) * this["Ds"]
        }
        static Is(a) {
          var b = hu,
            c = b[97],
            d = b[95];
          return (a %= c) > d && (a -= c), a < -d && (a += c), a
        }
        static Cs(a, b) {
          var c = hu,
            d = c[97],
            e = c[95];
          let f;
          f = b - a;
          return f %= d, f > e && (f -= d), f < -e && (f += d), f
        }
        static Ts(a, b, c, d) {
          let e, f;
          f = 2 * (1 - d) * (b["x"] - a["x"]) + 2 * d * (c["x"] - b["x"]), e = 2 * (1 - d) * (b["y"] - a["y"]) + 2 * d * (c["y"] - b["y"]);
          return Math["atan2"](e, f)
        }
        static Rs(a, b, c, d) {
          var e = hr,
            f = e[0];
          return hu[95] * this["Ts"](a, b, c, d) / Math["PI"]
        }
        static Us(a, b, c, d, e) {
          let f, g, h, i;
          f = a["x"] + (b["x"] - a["x"]) * e, g = a["y"] + (b["y"] - a["y"]) * e, i = b["x"] + (c["x"] - b["x"]) * e, h = b["y"] + (c["y"] - b["y"]) * e;
          return d["x"] = f + (i - f) * e, d["y"] = g + (h - g) * e, !(e < 1)
        }
