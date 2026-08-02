    vj = class vZ {
      static NB(a, b, c) {
        var d = hr,
          e = d[2],
          f = d[16],
          g = d[0],
          h = "instance",
          i = "XS";
        let j = !1;
        const k = Laya["Pool"]["getItemByCreateFun"](a, () => {
          const a = new Laya["Sprite"],
            f = new b(c["tS"]);
          return f["sx"] = vi["instance"](), f["sw"] = uq["instance"](), f["eS"] = a, f["init"](), j = !0, f
        });
        return k["id"] = this["qB"]++, j && c["XS"] && "SimpleDynamicArrow" === c["type"]["IS"] && k["TS"](c["XS"]), k
      }
      static $B(a) {
        var b = hr,
          c = "getPrototypeOf",
          d = "name";
        let e = a;
        for (; e && e !== Function["prototype"];) {
          const a = e["IS"];
          if (a) return a;
          e = Object["getPrototypeOf"](e)
        }
        const f = a["name"];
        if (f && "type" !== f) return f;
        const g = Object["getPrototypeOf"](a);
        return g && g !== Function["prototype"] && g["name"] ? g["name"] : "BulletUnknown"
      }
      static register(a, b) {
        var c = hr;
        this["HL"]["set"](a, b)
      }
      static GS(a, b) {
        var c = hr;
        this["HL"]["set"](a, () => new b(""))
      }
      static WL(a) {
        var b = hr;
        const c = this["HL"]["get"](a);
        if (!c) throw new Error(`BulletFactory: 未为类型 ${a} 注册创建器`);
        return c()
      }
      static produce(a) {
        var b = hr,
          c = b[0],
          d = "XS",
          e = "tS",
          f = "type",
          g = "QB",
          h = "eS";
        a["XS"] && (a["XS"]["tS"] || (a["XS"]["tS"] = ""), a["tS"] = a["XS"]["tS"]);
        const i = a["tS"] || "",
          j = this["$B"](a["type"]),
          k = vZ["VB"] + "_" + j + "_" + i;
        let l;
        return l = this["NB"](k, a["type"], a), a["QB"] && a["QB"]["forEach"](a => {
          l["cS"](a)
        }), l["Zk"] || oc["instance"]["event"](sS["bt"], l["eS"]), l["eS"]["zIndex"] = t1["hr"], l
      }
      static recover(a) {
        var b = hr,
          c = b[0],
          d = "eS",
          e = "recover";
        const f = this["$B"](a["constructor"]),
          g = a["tS"],
          h = vZ["VB"] + "_" + f + "_" + g;
        Laya["Tween"]["killAll"](a["eS"]), a["recover"](), a["eS"]["removeSelf"](), Laya["Pool"]["recover"](h, a)
      }
    };
  w1_da: for (let a of lo) {
    switch (a) {
      case 0:
        vj["HL"] = new Map, vj["VB"] = "bullet_pool", vj["qB"] = 0;
        break;
      default:
        break
    }
  }
  var vk = vj,
