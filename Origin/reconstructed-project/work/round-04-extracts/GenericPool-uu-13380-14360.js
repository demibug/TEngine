          nLj = "writable";
        Object["defineProperty"](b["prototype"], "init", {
          ["value"]() {
            var a = hr,
              b = a[4],
              c = "jy";
            this["jy"] = new Map;
            let d = Object["keys"](us);
            for (let e = 0; e < d["length"]; e++) this["jy"]["set"](d[e], Laya["loader"]["getRes"](`prefab/${d[e]}.lh`))
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](b["prototype"], "zy", {
          ["value"](a) {
            var b = hr;
            return this["jy"]["get"](a)
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        })
      } ["bind"](b)();
      return b
    } ["bind"](this)["apply"](),
    uu = function() {
      let a = class b extends qU {
        static hitTex(a) {
          var c = hr;
          return b["Ny"] + a + ".png"
        }
        getItem(a, b) {
          var c = hr;
          return Laya["Pool"]["getItemByCreateFun"](a, this[`${a}CreateFunc`], this)
        }
        recover(a, b) {
          var c = hr;
          b && Laya["Pool"]["recover"](a, b)
        }
        mobCreateFunc() {
          var a = hr,
            b = a[0],
            c = a[4],
            d = a[5],
            e = a[1],
            f = "instance",
            g = "map",
            h = "ye",
            i = "getChildByName",
            j = "pos",
            k = "width",
            l = "height",
            m = "zIndex";
          let n = ut["instance"]()["zy"]("mob")["create"]();
          n["size"](uq["instance"]()["map"]["ye"], uq["instance"]()["map"]["gridHei"]);
          let o = n["getChildByName"]("shadow");
          o["pos"]((n["width"] - o["width"]) / 2, n["height"] - o["height"]), o["zIndex"] = t1["Jn"];
          let p = n["getChildByName"]("hpBgImg");
          return p["pos"]((uq["instance"]()["map"]["ye"] - p["width"]) / 2, 3), p["zIndex"] = t1["vr"], n
        }
        bossCreateFunc() {
          var a = hr,
            b = a[0],
            c = a[4];
          let d = ut["instance"]()["zy"]("boss")["create"]();
          return d["getChildByName"]("hpBgImg")["zIndex"] = t1["vr"], d
        }
        soldierCreateFunc() {
          var a = hr,
            b = hu,
            c = a[4],
            d = a[6],
            e = a[0],
            f = a[5],
            g = a[3],
            h = a[2],
            i = a[1],
            j = b[43],
            k = b[1],
            l = "size",
            m = "instance",
            n = "map",
            o = "Image",
            p = "name",
            q = "anchorX",
            r = "anchorY",
            s = "pos",
            t = "alpha",
            u = "zIndex",
            v = "addChild",
            w = "width";
          let x = new Laya["Sprite"];
          x["size"](uq["instance"]()["map"]["ye"], uq["instance"]()["map"]["gridHei"]);
          let y = new Laya["Image"]("resources/img/gameObject/soldier/shadow2.png");
          y["name"] = "shadow", y["size"](b[62], b[2]), y["anchorX"] = .5, y["anchorY"] = .5, y["pos"](b[60], b[49]), y["alpha"] = .2, y["zIndex"] = t1["Jn"], x["addChild"](y);
          let z = new Laya["Image"]("resources/img/gameObject/enemy/chaos0.png");
          z["name"] = "chaos", z["size"](b[16], b[48]), z["anchorX"] = .5, z["anchorY"] = .5, z["pos"](j, j), z["alpha"] = .5, z["visible"] = !1, x["addChild"](z);
          let A = new Laya["FontClip"]("resources/img/gameObject/bitmapFont/number5.png");
          return A["name"] = "lvl", A["value"] = "1", A["size"](k, k), A["pos"](x["width"] - A["width"], 0), A["zIndex"] = t1["wr"], A["interval"] = b[45], A["sheet"] = "12345", x["addChild"](A), x
        }
        bowEffectCreateFunc() {
          var a = hr,
            b = a[5];
          let c = new Laya["Image"]("resources/img/gameOverUI/arrow1.png");
          return c["size"](9, hu[60]), c["anchorX"] = .5, c["anchorY"] = .5, c
        }
        damageNumCreateFunc() {
          var a = hr,
            b = a[0],
            c = "instance";
          let d = ut["instance"]()["zy"]("damageNum")["create"]();
          return d["x"] = uq["instance"]()["map"]["ye"] / 2, d
        }
        bigFireEffCreateFunc() {
          var a = hr,
            b = a[6],
            c = "Image",
            d = "size",
            e = "ye",
            f = "gridHei";
          let g = new Laya["Image"]("resources/img/effect/bigFireUpAround_01.png"),
            h = new Laya["Image"]("resources/img/effect/bigFireDownAround_01.png");
          const i = uq["instance"]()["map"];
          return g["size"](i["ye"], i["gridHei"]), h["size"](i["ye"], i["gridHei"]), h["name"] = "downImg", g["addChild"](h), g
        }
        mergeEffCreateFunc() {
          var a = hr,
            b = hu,
            c = a[3],
            d = a[44],
            e = a[5],
            f = a[4],
            g = a[2],
            h = b[65],
            i = b[43],
            j = b[201],
            k = b[202],
            l = "Image",
            m = "size",
            n = "pos",
            o = "anchorX",
            p = "anchorY",
            q = "name",
            r = "addChild";
          let s = new Laya["Image"]("resources/img/gameObject/soldier/mergeEff1.png");
          s["size"](b[200], h), s["pos"](h, i), s["anchorX"] = .5, s["anchorY"] = .5;
          let t = new Laya["Image"]("resources/img/gameObject/soldier/mergeEff2.png");
          t["name"] = "mergeEffImg2", t["size"](j, k), t["pos"](h, b[35]), t["anchorX"] = .5, t["anchorY"] = .5, s["addChild"](t);
          let u = new Laya["Label"];
          return u["name"] = "label", u["color"] = "#FFF83D", u["fontSize"] = i, u["strokeColor"] = "#fc1915", u["stroke"] = 10, u["size"](j, k), u["align"] = "center", u["valign"] = "middle", t["addChild"](u), s
        }
        textEffCreateFunc() {
          var a = hr,
            b = hu,
            c = a[2],
            d = a[3],
            e = a[4];
          let f = new Laya["Text"];
          return f["name"] = "txt", f["fontSize"] = b[23], f["align"] = "center", f["valign"] = "middle", f["size"](b[201], b[202]), f["color"] = "#ffffff", f["stroke"] = 4, f["alpha"] = 0, f["anchor"](.5, .5), f
        }
        smokeEffCreateFunc() {
          var a = hr,
            b = hu,
            c = a[5],
            d = b[81];
          let e = new Laya["Image"]("resources/img/effect/smoke1.png");
          return e["anchorX"] = .5, e["anchorY"] = .5, e["size"](d, d), e
        }
        rocketEffCreateFunc() {
          var a = hr,
            b = hu,
            c = a[5],
            d = b[51];
          let e = new Laya["Image"]("resources/img/effect/explode0.png");
          return e["anchorX"] = .5, e["anchorY"] = .5, e["size"](d, d), e
        }
        liHuaEffCreateFunc() {
          var a = hr,
            b = hu,
            c = a[5],
            d = b[91];
          let e = new Laya["Image"]("resources/img/effect/hitEffect/lihuahit0.png");
          return e["anchorX"] = .5, e["anchorY"] = .5, e["size"](d, d), e
        }
        taiChiEffCreateFunc() {
          var a = hr,
            b = hu,
            c = a[5],
            d = b[51];
          let e = new Laya["Image"]("resources/img/effect/hitEffect/taiChiEff_01.png");
          return e["anchorX"] = .5, e["anchorY"] = .5, e["size"](d, d), e
        }
        coldDaoQiEffCreateFunc() {
          var a = hr,
            b = hu,
            c = a[3];
          let d = new Laya["Image"]("resources/img/effect/iceSlashEff01.png");
          return d["size"](b[203], b[204]), d["scale"](1, 1), d["anchor"](.5, .85), d
        }
        enemyKnifeAttackEffCreateFunc() {
          var a = hr,
            b = hu,
            c = a[2],
            d = a[5],
            e = "Image",
            f = "name",
            g = "size",
            h = "anchorX",
            i = "anchorY",
            j = "addChild";
          let k = new Laya["Image"],
            l = new Laya["Image"]("resources/img/gameObject/enemy/knife.png");
          l["name"] = "knife", l["size"](b[6], b[34]), l["anchorX"] = .5, l["anchorY"] = 1;
          let m = new Laya["Image"]("resources/img/gameObject/enemy/knifeLight.png");
          return m["size"](b[37], b[71]), m["anchorX"] = .5, m["anchorY"] = .5, m["name"] = "knifeLight", k["addChild"](l), k["addChild"](m), k
        }
        electricEffCreateFunc() {
          var a = hr,
            b = hu,
            c = a[5],
            d = b[81];
          let e = new Laya["Image"]("resources/img/effect/electric1.png");
          return e["size"](d, d), e["anchorX"] = .5, e["anchorY"] = .5, e
        }
        fireEffCreateFunc() {
          var a = hr,
            b = hu,
            c = a[5],
            d = b[43];
          let e = new Laya["Image"]("resources/img/props/fire0.png");
          return e["size"](d, d), e["anchorX"] = .5, e["anchorY"] = .5, e
        }
        groundFireEffCreateFunc() {
          var a = hr,
            b = hu,
            c = a[5],
            d = b[65];
          let e = new Laya["Image"]("resources/img/effect/fireGround_01.png");
          return e["size"](d, d), e["anchorX"] = .5, e["anchorY"] = .5, e
        }
        goldCreateFunc() {
          var a = hr,
            b = hu,
            c = a[5],
            d = b[22];
          let e = new Laya["Image"]("resources/img/battleUI/gold.png");
          return e["size"](d, d), e["anchorX"] = .5, e["anchorY"] = .5, e
        }
        dragLineCreateFunc() {
          var a = hr;
          let b = new Laya["Image"]("resources/img/battleUI/dragImg3.png");
          return b["size"](hu[1], 10), b["anchorY"] = .5, b
        }
        redPointCreateFunc() {
          var a = hr,
            b = a[5];
          let c = new Laya["Image"]("resources/img/effect/redPoint.png");
          return c["size"](7, 7), c["anchorX"] = .5, c["anchorY"] = .5, c
        }
        flagEffCreateFunc() {
          var a = hr,
            b = a[5];
          let c = new Laya["Image"]("resources/img/gameOverUI/flagEff0.png");
          return c["anchorX"] = .5, c["anchorY"] = .5, c
        }
        goldUpImgCreateFunc() {
          var a = hr,
            b = hu,
            c = b[65],
            d = b[35];
          const e = new ur("resources/img/battleUI/goldUpImg.png", c, d);
          return e["size"](c, d), e["anchor"](.5, .5), e
        }
        mobDeadCreateFunc() {
          var a = hr,
            b = hu,
            c = a[31],
            d = a[1],
            e = a[2],
            f = a[5],
            g = a[3],
            h = b[65],
            i = "size",
            j = "anchorX",
            k = "anchorY",
            l = "Image",
            m = "name",
            n = "pos",
            o = "width",
            p = "height",
            q = "addChild";
          let r = new Laya["Sprite"];
          r["size"](h, h), r["anchorX"] = .5, r["anchorY"] = .5;
          let s = new Laya["Image"]("resources/img/effect/mobDead0.png");
          s["name"] = "ink", s["size"](b[70], b[64]), s["anchorX"] = .5, s["anchorY"] = .5, s["pos"](r["width"] / 2, r["height"] / 2), r["addChild"](s);
          let t = new Laya["Image"]("resources/img/effect/mobDead1.png");
          return t["name"] = "img", t["size"](b[205], b[85]), t["anchorX"] = .5, t["anchorY"] = .5, t["pos"](r["width"] / 2, r["height"] / 2), r["addChild"](t), r
        }
        daoQiHitCreateFunc() {
          var a = hr,
            b = hu,
            c = a[5];
          let d = new Laya["Image"]("resources/img/effect/hitEffect/DaoQiHit0.png");
          return d["size"](b[17], b[62]), d["anchorX"] = .5, d["anchorY"] = .5, d
        }
        createHitEffectBox() {
          var a = hr,
            b = hu,
            c = a[5],
            d = b[65];
          const e = new Laya["Box"];
          return e["size"](d, d), e["anchorX"] = .5, e["anchorY"] = .5, e["mouseEnabled"] = !1, e
        }
        knifeHitCreateFunc() {
          var a = hr,
            c = hu,
            d = a[4],
            e = a[2],
            f = a[5],
            g = c[64],
            h = c[71],
            i = c[43],
            j = c[45],
            k = c[63],
            l = c[22],
            m = "hitTex",
            n = "name",
            o = "pos",
            p = "size",
            q = "anchorX",
            r = "anchorY",
            s = "addChild";
          const t = this["createHitEffectBox"](),
            u = new ur(b["hitTex"]("blood0"), g, h);
          u["name"] = "blood", u["pos"](i, j), u["size"](g, h), u["anchorX"] = .5, u["anchorY"] = .5;
          const v = new ur(b["hitTex"]("knife0"), k, l, !0);
          return v["name"] = "img", v["pos"](i, j), v["size"](k, l), v["anchorX"] = .5, v["anchorY"] = .5, v["mouseEnabled"] = !1, t["addChild"](u), t["addChild"](v), t
        }
        bowHitCreateFunc() {
          var a = hr,
            c = hu,
            d = a[2],
            e = a[5],
            f = c[22],
            g = c[35],
            h = c[43],
            i = c[42],
            j = c[52],
            k = c[27],
            l = c[47],
            m = "hitTex",
            n = "name",
            o = "pos",
            p = "size",
            q = "anchorX",
            r = "anchorY",
            s = "addChild";
          const t = this["createHitEffectBox"](),
            u = new ur(b["hitTex"]("bow0"), f, g, !0);
          u["name"] = "img0", u["pos"](h, h), u["size"](f, g), u["anchorX"] = .5, u["anchorY"] = .5, t["addChild"](u);
          const v = new ur(b["hitTex"]("bow1"), i, j, !0);
          v["name"] = "img1", v["pos"](h, h), v["size"](i, j), v["anchorX"] = .5, v["anchorY"] = .5, t["addChild"](v);
          const w = new ur(b["hitTex"]("bow2"), k, l, !0);
          return w["name"] = "img2", w["pos"](h, h), w["size"](k, l), w["anchorX"] = .5, w["anchorY"] = .5, t["addChild"](w), t
        }
        pikeHitCreateFunc() {
          var a = hr,
            c = hu,
            d = a[2],
            e = a[5],
            f = c[33],
            g = c[42];
          const h = this["createHitEffectBox"](),
            i = new ur(b["hitTex"]("pike0"), f, g, !0);
          return i["name"] = "img1", i["pos"](42.2, 42.744), i["size"](f, g), i["anchorX"] = .54, i["anchorY"] = .549, h["addChild"](i), h
        }
        cavalryHitCreateFunc() {
          var a = hr,
            c = hu,
            d = a[6],
            e = a[2],
            f = a[5],
            g = c[30],
            h = c[21],
            i = c[41],
            j = c[15],
            k = c[43],
            l = "hitTex",
            m = "name",
            n = "pos",
            o = "size",
            p = "anchorX",
            q = "anchorY",
            r = "addChild";
          const s = this["createHitEffectBox"](),
            t = new ur(b["hitTex"]("cavalry0"), g, h);
          t["name"] = "img1", t["pos"](i, j), t["size"](g, h), t["anchorX"] = .7, t["anchorY"] = .7, s["addChild"](t);
          const u = new ur(b["hitTex"]("cavalry1"), k, i);
          return u["name"] = "img2", u["pos"](h, j), u["size"](k, i), u["anchorX"] = .7, u["anchorY"] = .7, s["addChild"](u), s
        }
        bubbleCreateFunc() {
          var a = hr,
            b = a[3],
            c = a[5];
          let d = new Laya["Image"](`resources/img/gameObject/enemy/bubble${np["range"](0,2,!0)}.png`);
          return d["anchorX"] = .5, d["anchorY"] = .5, d
        }
        generalMergeTipCreateFunc() {
          var a = hr,
            b = hu,
            c = a[34],
            d = a[1],
            e = a[5],
            f = "Image",
            g = "size",
            h = "anchorX",
            i = "anchorY";
          let j = new Laya["Image"]("resources/img/battleUI/mergeTip1.png");
          j["size"](b[206], b[78]), j["anchorX"] = .5, j["anchorY"] = .5;
          let k = new Laya["Image"]("resources/img/battleUI/mergeTip2.png");
          return k["name"] = "img", k["size"](b[207], b[64]), k["anchorX"] = .5, k["anchorY"] = .5, k["pos"](b[208], b[45]), j["addChild"](k), j
        }
        propsCreateFunc() {
          var a = hr,
            b = a[5],
            c = a[3],
            d = "size",
            e = "instance",
            f = "map",
            g = "ye",
            h = "gridHei";
          let i = new Laya["Sprite"];
          i["size"](uq["instance"]()["map"]["ye"], uq["instance"]()["map"]["gridHei"]);
          let j = new Laya["Image"];
          return j["name"] = "props", j["pos"](uq["instance"]()["map"]["ye"] / 2, uq["instance"]()["map"]["gridHei"] / 2), j["size"](uq["instance"]()["map"]["ye"], uq["instance"]()["map"]["gridHei"]), j["anchorX"] = .5, j["anchorY"] = .5, i["addChild"](j), i
        }
        tigerRoarsCreateFunc() {
          var a = hr,
            b = hu,
            c = a[5];
          let d = new Laya["Image"]("resources/img/effect/tiger0.png");
          return d["size"](b[72], b[71]), d["anchorX"] = .5, d["anchorY"] = .5, d
        }
        wolfRoarsCreateFunc() {
          var a = hr,
            b = hu,
            c = a[5];
          let d = new Laya["Image"]("resources/img/effect/wolf0.png");
          return d["size"](b[68], b[91]), d["anchorX"] = .5, d["anchorY"] = .5, d
        }
        soundWavesCreateFunc() {
          var a = hr,
            b = hu,
            c = a[5],
            d = b[209];
          let e = new Laya["Image"]("resources/img/effect/soundWave2.png");
          return e["size"](d, d), e["anchorX"] = .5, e["anchorY"] = .5, e
        }
        bloodEffCreateFunc() {
          var a = hr,
            b = hu,
            c = a[5];
          let d = new Laya["Image"]("resources/img/effect/hitEffect/blood0.png");
          return d["size"](b[64], b[71]), d["anchorX"] = .5, d["anchorY"] = .5, d
        }
        fallEffCreateFunc() {
          var a = hr,
            b = hu,
            c = a[5];
          let d = new Laya["Image"]("resources/img/effect/fallEff0.png");
          return d["size"](b[210], b[52]), d["anchorX"] = .5, d["anchorY"] = 1, d
        }
        diedaoEffCreateFunc() {
          var a = hr,
            b = hu,
            c = a[5];
          let d = new Laya["Image"]("resources/img/effect/diedao0.png");
          return d["size"](b[91], b[52]), d["anchorX"] = .5, d["anchorY"] = 1, d
        }
        longDanLiangYinQiangHitEffCreateFunc() {
          var a = hr,
            b = hu,
            c = a[5];
          let d = new Laya["Image"]("resources/img/effect/hitEffect/longDanLiangYinQiangHitEff_0.png");
          return d["size"](b[73], b[80]), d["anchorX"] = .5, d["anchorY"] = .5, d
        }
        rainCreateFunc() {
          var a = hr,
            b = a[5];
          let c = new Laya["Image"]("resources/img/gameObject/enemy/rain.png");
          return c["size"](3, hu[74]), c["anchorX"] = .5, c["anchorY"] = 1, c
        }
        footprintCreateFunc() {
          var a = hr,
            b = hu,
            c = a[5],
            d = b[13],
            e = b[3];
          const f = new ur("resources/img/props/footprint.png", d, e);
          return f["size"](d, e), f["anchorX"] = .5, f["anchorY"] = .5, f
        }
        loveHeartCreateFunc() {
          var a = hr,
            b = hu,
            c = a[5],
            d = b[17],
            e = b[62];
          const f = new ur("resources/img/gameObject/enemy/heart.png", d, e);
          return f["size"](d, e), f["anchorX"] = .5, f["anchorY"] = .5, f
        }
        generalBgCreateFunc() {
          var a = hr,
            b = hu,
            c = a[45],
            d = "Image",
            e = "size";
          let f = new Laya["Image"]("resources/img/gameObject/soldier/generalBg1.png");
          f["size"](b[200], b[65]);
          let g = new Laya["Image"]("resources/img/gameObject/soldier/generalBg2.png");
          return g["size"](b[211], b[92]), g["pos"](-4, -4), f["addChild"](g), f
        }
        thunderStrikeEffCreateFunc() {
          var a = hr,
            b = hu,
            c = a[5];
          let d = new Laya["Image"]("resources/img/effect/thunder0.png");
          return d["anchorX"] = .5, d["anchorY"] = 1, d["size"](b[212], b[213]), d
        }
        arrowDownCreateFunc() {
          var a = hr,
            b = hu,
            c = a[5],
            d = b[1],
            e = b[41];
          const f = new ur("resources/img/effect/arrowDown.png", d, e);
          return f["size"](d, e), f["anchorX"] = .5, f["anchorY"] = .5, f
        }
        arrowUpCreateFunc() {
          var a = hr,
            b = hu,
            c = a[5],
            d = b[1],
            e = b[41];
          const f = new ur("resources/img/effect/arrowUp.png", d, e);
          return f["size"](d, e), f["anchorX"] = .5, f["anchorY"] = .5, f
        }
        meteorCreateFunc() {
          var a = hr,
            b = hu;
          let c = new Laya["Image"]("resources/img/props/meteor_2.png");
          return c["size"](b[87], b[51]), c["anchor"](.27, .53), c
        }
        fireParticlCreateFunc() {
          var a = hr,
            b = hu,
            c = a[5],
            d = b[22];
          const e = new ur("resources/img/props/fireParticl.png", d, d);
          return e["size"](d, d), e["anchorX"] = .5, e["anchorY"] = .5, e
        }
        shopAdLightCreateFunc() {
          var a = hr,
            b = hu,
            c = b[28];
          let d = new Laya["Image"]("resources/img/shop/light1.png");
          return d["size"](c, c), d["anchor"](.5, .5), d
        }
        pointFlashEffCreateFunc() {
          var a = hr;
          let b = new Laya["Image"]("resources/img/shop/lottery/whiteStar.png");
          return b["size"](10, 10), b["anchor"](.5, .5), b
        }
        weaponFragmentRewardCreateFunc() {
          var a = hr,
            b = hu,
            c = a[3],
            d = a[2],
            e = a[4],
            f = b[52],
            g = "Image",
            h = "size",
            i = "name",
            j = "pos",
            k = "addChild";
          let l = new Laya["Image"];
          l["size"](f, f), l["skin"] = "resources/img/weaponBag/fragment0.png";
          let m = new Laya["Image"];
          m["name"] = "icon", m["size"](f, f), m["anchor"](.5, .5), m["pos"](l["width"] / 2, l["height"] / 2), l["addChild"](m);
          let n = new Laya["Text"];
          return n["name"] = "num", n["fontSize"] = b[21], n["size"](f, f), n["pos"](0, b[59]), n["align"] = "center", n["valign"] = "middle", n["stroke"] = 5, l["addChild"](n), l
        }
        starFlyCreateFunc() {
          var a = hr,
            b = hu,
            c = a[5],
            d = b[1];
          let e = new Laya["Image"]("resources/img/shop/lottery/yellowStar.png");
          return e["size"](d, d), e["anchorX"] = .5, e["anchorY"] = .5, e
        }
      };
      ! function() {
        "use strict";
        var b = hr,
          c = b[14],
          d = b[21],
          e = b[16],
          f = b[19],
          g = b[13],
          h = b[15],
          i = "defineProperty",
          nO4 = "value",
          nO5 = "enumerable",
          nO6 = "configurable",
          nO7 = "writable";
        Object["defineProperty"](a["prototype"], "init", {
          ["value"]() {},
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](a["prototype"], "mapImgCreateFunc", {
          ["value"]() {
            var a = hr;
            return ut["instance"]()["zy"]("mapItem")["create"]()
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](a["prototype"], "setSoldierEffCreateFunc", {
          ["value"]() {
            var a = hr;
            return ut["instance"]()["zy"]("setSoldierEff")["create"]()
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](a["prototype"], "crackEffCreateFunc", {
          ["value"]() {
            var a = hr,
              b = a[6];
            return ut["instance"]()["zy"]("crackEff")["create"]()
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](a["prototype"], "shovelGrassCreateFunc", {
          ["value"]() {
            var a = hr;
            return ut["instance"]()["zy"]("shovelGrass")["create"]()
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](a["prototype"], "trailCreateFunc", {
          ["value"]() {
            var a = hr;
            return ut["instance"]()["zy"]("trail")["create"]()
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](a["prototype"], "talkBoxCreateFunc", {
          ["value"]() {
            var a = hr;
            return ut["instance"]()["zy"]("talkBox")["create"]()
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](a["prototype"], "rankItemCreateFunc", {
          ["value"]() {
            var a = hr,
              b = a[6];
            return ut["instance"]()["zy"]("rankItem")["create"]()
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](a["prototype"], "goldUpCreateFunc", {
          ["value"]() {
            var a = hr,
              b = a[4];
            return ut["instance"]()["zy"]("goldUp")["create"]()
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](a["prototype"], "dragTipCreateFunc", {
          ["value"]() {
            var a = hr;
            return new Laya["Image"]("resources/img/battleUI/dragBg2.png")
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](a["prototype"], "heartCreateFunc", {
          ["value"]() {
            var a = hr;
            return ut["instance"]()["zy"]("heart")["create"]()
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](a["prototype"], "starRotateEffCreateFunc", {
          ["value"]() {
            var a = hr;
            return new Laya["Image"]("resources/img/effect/starRotate0.png")
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](a["prototype"], "treasureCreateFunc", {
          ["value"]() {
            var a = hr,
              b = a[6];
            return ut["instance"]()["zy"]("treasure")["create"]()
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](a["prototype"], "lotteryItemCreateFunc", {
          ["value"]() {
            var a = hr;
            return ut["instance"]()["zy"]("lotteryItem")["create"]()
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](a["prototype"], "weaponFragmentCreateFunc", {
          ["value"]() {
            var a = hr;
            return ut["instance"]()["zy"]("weaponFragment")["create"]()
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        });
        Object["defineProperty"](a["prototype"], "attChangeTipCreateFunc", {
          ["value"]() {
            var a = hr;
            return ut["instance"]()["zy"]("attChangeTip")["create"]()
          },
          ["enumerable"]: false,
          ["configurable"]: true,
          ["writable"]: true
        })
      } ["bind"](a)();
      return a
    } ["bind"](this)["apply"]();
  let uv = 0,
    uw = mr;
  w1_cj: while (uv < 7) {
    k4["push"](1);
    if (b7) k5 = 0;
    var ux = 0;
    var uy = 1;
    if (_2 * _2 + V * V + bc * bc + G * G >= G * bc + _2 * V + V * G + bc * _2) ux += 3;
    if (bS) ux += 2;
    if (_6 * _6 + q * q + R * R + t * t >= _6 * q + q * R + t * _6 + R * t) uy += 2;
    if (cg) uy += 0;
    ++uv;
    switch (uw) {
      case 1:
        tk["boundsMap"] = new Map;
        uw = 6;
        break;
      case 5:
        tZ["Bd"] = ["resources/img/shop/light1.png", "resources/img/shop/light2.png", "resources/img/shop/light3.png"], tZ["Sg"] = "$trailFadeRecover";
        uw = 0;
        break;
      case 6:
        uu["Ny"] = "resources/img/effect/hitEffect/";
        uw = 2;
        break;
      case 3:
        tk = function() {
          var a = hr;
          let b;
          b = class c extends Laya["Sprite"] {
            constructor(a) {
              var b = hr,
                c = b[4],
                d = "spine";
              let e;
              super(), this["Id"] = 1, this["spine"] = this["addComponent"](Laya["Spine2DRenderNode"]), this["spine"]["source"] = a;
              e = this["getSpineBounds"]();
              this["size"](e["width"], e["height"])
            }
            getSpineBounds() {
              var a = hr,
                b = a[1],
                d = "boundsMap",
                e = "spine",
                f = "source";
              let g;
              g = c["boundsMap"]["get"](this["spine"]["source"]);
              return g || (g = this["getBounds"](), c["boundsMap"]["set"](this["spine"]["source"], g), g)
            }
            play(a, b, c, d, e, f, g) {
              var h = hr;
              this["Cd"](() => {
                this["spine"]["play"](a, b, c, d, e, f, g)
              })
            }
            setInitPlaybackRate(a) {
              this["Id"] = a
            }
            offset(a, b) {
              var c = hr;
              this["spine"]["offset"] = new Laya["Vector2"](a, b)
            }
            setIsFastMode(a) {
              var b = hr,
                c = "spine";
              this["Cd"](() => {
                a ? this["spine"]["changeFast"]() : this["spine"]["changeNormal"]()
              })
            }
            Cd(a, b = hu[61]) {
              var c = hr,
                d = c[0],
                e = c[7];
              this["Rd"]() ? a() : b <= 0 || this["destroyed"] || Laya["timer"]["frameOnce"](1, this, () => this["Cd"](a, b - 1))
            }
            destroy(a = !0) {
              var b = hr;
              Laya["timer"]["clearAll"](this), super["destroy"](a)
            }
            setAutoAdjust(a) {
              var b = hr;
              this["spine"]["autoAdjust"] = a
            }
            playbackRate(a) {
              var b = hr;
              this["spine"]["playbackRate"](this["Id"] * a)
            }
            showSkinByName(a) {
              var b = hr;
              this["spine"]["showSkinByName"](a)
            }
            on(a, b, c, d) {
              return super["on"](a, b, c, d)
            }
            off(a, b) {
              return super["off"](a, b)
            }
            offAll(a) {
              return super["offAll"](a)
            }
          };
          ! function() {
            "use strict";
            var a = hr,
              c = a[0],
              d = "defineProperty",
              nPO = "value",
              nPP = "enumerable",
              nPQ = "configurable",
              nPR = "writable";
            Object["defineProperty"](b["prototype"], "stop", {
              ["value"]() {
                var a = hr;
                this["spine"]["stop"]()
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            });
            Object["defineProperty"](b["prototype"], "Td", {
              ["value"]() {
                var a = hr;
                Laya["timer"]["clearAll"](this), this["stop"]()
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            });
            Object["defineProperty"](b["prototype"], "Rd", {
              ["value"]() {
                var a = hr,
                  b = "spine";
                return !!this["spine"] && (!!this["spine"]["templet"] || null != this["spine"]["getSkeleton"]())
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            })
          } ["bind"](b)();
          return b
        } ["bind"](this)["apply"]();
        uw = 1;
        break;
      case 0:
        qs = tZ, oR = class extends Laya["Dialog"] {}, void({
          ["regClass"]: nG
        } = Laya), nT = function() {
          let a;
          a = class extends oR {};
          ! function() {
            "use strict";
            var b = hr,
              c = b[0],
              d = "defineProperty",
              nPZ = "value",
              nP0 = "enumerable",
              nP1 = "configurable",
              nP2 = "writable";
            Object["defineProperty"](a["prototype"], "onAwake", {
              ["value"]() {
                var a = hr,
                  b = a[0],
                  c = a[3],
                  d = "okBtn",
                  e = "on",
                  f = "Event",
                  g = "CLICK",
                  h = "xBtn";
                this["okBtn"]["on"](Laya["Event"]["CLICK"], this, this["Dd"]), this["xBtn"]["on"](Laya["Event"]["CLICK"], this, this["Tn"]), qs["instance"]()["Tf"]([this["okBtn"], this["xBtn"]])
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            });
            Object["defineProperty"](a["prototype"], "onOpened", {
              ["value"](a) {
                var b = hr;
                r2["instance"]()["yn"]()
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            });
            Object["defineProperty"](a["prototype"], "Dd", {
              ["value"]() {
                var a = hr;
                r2["instance"]()["hc"]()
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            });
            Object["defineProperty"](a["prototype"], "Tn", {
              ["value"]() {
                var a = hr,
                  b = a[0],
                  c = "instance";
                r2["instance"]()["gn"](), sF["instance"]()["Tn"]("AuthorizeDialog")
              },
              ["enumerable"]: false,
              ["configurable"]: true,
              ["writable"]: true
            })
          } ["bind"](a)();
          return a
        } ["bind"](this)["apply"]();
        uw = 4;
        break;
      case 2:
        rw = uu, tZ = function() {
          var a = hr,
            b = hu,
            c = b[1],
            d = b[215],
            e = b[61],
            f = b[100];
          let g;
          g = class h extends qU {
