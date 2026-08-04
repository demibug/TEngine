#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
将 Origin 还原工程 unity-export/config/*.json 转为 Luban 格式的 xlsx 文件，
输出到 E:\\MyWork\\MyTD\\TEngine\\Origin\\config\\ 供 Unity 工程的 Luban 导表使用。

每个 xlsx 采用 Luban 标准格式：
  第1行 ##var    字段名
  第2行 ##type   类型
  第3行 ##group  分组(留空表示所有分组)
  第4行 ##       注释行
  第5行起        数据
"""

import json
import os
import sys

try:
    import openpyxl
except ImportError:
    print("ERROR: 需要 openpyxl，请运行 pip install openpyxl", file=sys.stderr)
    sys.exit(1)

# ── 路径配置 ──────────────────────────────────────────────
SRC_DIR = r"E:\MyWork\MyTD\TEngine\Origin\reconstructed-project\unity-export\config"
OUT_DIR = r"E:\MyWork\MyTD\TEngine\Origin\config"


def load_json(name):
    path = os.path.join(SRC_DIR, name)
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def fmt_num(v):
    """把浮点数格式化为 xlsx 友好字符串，避免 2.0999999999999996 之类长尾。"""
    if isinstance(v, float):
        # 还原原始意图：2.1 / 2.73 / 3.4125 等
        r = round(v, 4)
        if r == int(r):
            return int(r)
        return r
    return v


def fmt_list(values, sep=","):
    """列表 → 单单元格字符串。"""
    parts = []
    for v in values:
        if isinstance(v, float):
            parts.append(str(fmt_num(v)))
        else:
            parts.append(str(v))
    return sep.join(parts)


def new_sheet():
    wb = openpyxl.Workbook()
    ws = wb.active
    return wb, ws


def write_header(ws, fields, types, comments, groups=None):
    """写入 Luban 四行标题头。groups 为 None 时全部留空(=所有分组)。"""
    n = len(fields)
    if groups is None:
        groups = [None] * n
    ws.append(["##var"] + list(fields))
    ws.append(["##type"] + list(types))
    ws.append(["##group"] + list(groups))
    ws.append(["##"] + list(comments))


def save(wb, filename):
    path = os.path.join(OUT_DIR, filename)
    wb.save(path)
    print(f"  ✓ {filename}")


# ──────────────────────────────────────────────────────────
# 逐表转换函数
# ──────────────────────────────────────────────────────────

def gen_boss(data):
    wb, ws = new_sheet()
    fields = ["key", "name", "originalSymbol", "sourceRange", "skillKey",
              "animationKey", "resourcePath", "attackAnimation",
              "followupAnimation", "idleAnimation", "timeline"]
    types = ["string", "string", "string", "string", "string",
             "string", "string", "string",
             "string?", "string", "boss.Timeline"]
    comments = ["键(主键)", "名称", "原始符号", "源码区间", "技能键",
                "动画键", "资源路径", "攻击动画",
                "后续动画", "待机动画", "时间轴"]
    write_header(ws, fields, types, comments)
    for b in data["bosses"]:
        tl = b.get("timeline", {})
        timeline_str = f"{tl.get('effectAtMs','')},{tl.get('completeAtMs','')}"
        ws.append([
            None,
            b["key"], b["name"], b["originalSymbol"], b["sourceRange"],
            b["skillKey"], b["animationKey"], b["resourcePath"],
            b["attackAnimation"],
            b.get("followupAnimation"),
            b["idleAnimation"],
            timeline_str,
        ])
    save(wb, "boss.xlsx")


def gen_buff(data):
    wb, ws = new_sheet()
    fields = ["type", "name", "label", "kind", "channels"]
    types = ["int", "string", "string?", "int", "(list#sep=,),int"]
    comments = ["类型(主键)", "名称", "中文标签", "类别", "通道列表"]
    write_header(ws, fields, types, comments)
    for b in data["buffs"]:
        channels = b.get("channels") or []
        ws.append([
            None,
            b["type"], b["name"], b.get("label"), b["kind"],
            fmt_list(channels) if channels else None,
        ])
    save(wb, "buff.xlsx")


def gen_skill(data):
    wb, ws = new_sheet()
    fields = ["key", "name", "category", "description", "healthMultiplier",
              "speed", "rangeTiles", "cooldownSeconds", "source", "confidence"]
    types = ["string", "string", "string", "string", "int?",
             "int?", "float?", "int?", "string", "string?"]
    comments = ["键(主键)", "名称", "类别", "描述", "血量倍数",
                "速度", "范围(格)", "冷却(秒)", "源码标记", "置信度"]
    write_header(ws, fields, types, comments)
    for s in data["skills"]:
        ws.append([
            None,
            s["key"], s["name"], s["category"], s["description"],
            s.get("healthMultiplier"), s.get("speed"),
            fmt_num(s.get("rangeTiles")) if s.get("rangeTiles") is not None else None,
            s.get("cooldownSeconds"), s["source"], s.get("confidence"),
        ])
    save(wb, "skill.xlsx")


def gen_enemy(data):
    wb, ws = new_sheet()
    fields = ["key", "symbol", "typeIndex", "status", "resource", "deferred",
              "speed", "healthModifier", "levelMultipliers",
              "healthByWave"]
    types = ["string", "string", "int", "string?", "string?", "string?",
             "int", "string?", "(list#sep=,),float?",
             "(list#sep=,),int"]
    comments = ["键(主键)", "符号", "类型索引", "状态", "资源路径", "延迟项",
                "速度", "生命修正", "等级倍数",
                "每波生命值(共享表)"]
    write_header(ws, fields, types, comments)
    # 每波生命值是共享的，写入第一行(Mob0)，其余行留空以表示共享
    health_by_wave = data.get("healthByWave", [])
    hbw_str = fmt_list(health_by_wave) if health_by_wave else None
    types_map = data.get("types", {})
    first = True
    for key in ["Mob0", "Mob1", "Mob2", "Mob3", "Zombie", "Cavalry", "Puppet"]:
        t = types_map.get(key)
        if not t:
            continue
        lm = t.get("levelMultipliers")
        ws.append([
            None,
            t["key"], t["symbol"], t.get("typeIndex"),
            t.get("status"), t.get("resource"), t.get("deferred"),
            t.get("speed"), t.get("healthModifier"),
            fmt_list(lm) if lm else None,
            hbw_str if first else None,
        ])
        first = False
    save(wb, "enemy.xlsx")


def gen_general(data):
    wb, ws = new_sheet()
    fields = ["index", "name", "family", "partWords", "weaponType",
              "baseAttackPower", "attackRange", "attackIntervalSeconds",
              "targetPolicy", "status"]
    types = ["int", "string", "string", "(list#sep=,),string", "int",
             "int", "int", "float", "string", "string?"]
    comments = ["索引(主键)", "名称", "姓氏", "名字拆字", "武器类型",
                "基础攻击力", "攻击范围", "攻击间隔(秒)", "目标策略", "状态"]
    write_header(ws, fields, types, comments)
    for g in data["generals"]:
        pw = g.get("partWords", [])
        ws.append([
            None,
            g["index"], g["name"], g["family"],
            fmt_list(pw) if pw else None,
            g["weaponType"], g["baseAttackPower"], g["attackRange"],
            g["attackIntervalSeconds"], g["targetPolicy"], g.get("status"),
        ])
    save(wb, "general.xlsx")


def gen_map(data):
    wb, ws = new_sheet()
    fields = ["gridWidth", "gridHeight", "width", "height", "mapIndex",
              "blocks", "playerPath", "opponentPath"]
    types = ["int", "int", "int", "int", "int",
             "string?", "(list#sep=;),vector2int", "(list#sep=;),vector2int"]
    comments = ["网格宽", "网格高", "宽", "高", "地图索引",
                "阻挡点", "玩家路径", "对手路径"]
    write_header(ws, fields, types, comments)

    def path_str(path):
        return ";".join(f"{p['x']},{p['y']}" for p in path)

    ws.append([
        None,
        data["gridWidth"], data["gridHeight"], data["width"], data["height"],
        data["mapIndex"], data.get("blocks"),
        path_str(data["playerPath"]),
        path_str(data["opponentPath"]),
    ])
    save(wb, "map.xlsx")


def gen_unit_and_level(data):
    # ── unit.xlsx (多行表) ──
    wb, ws = new_sheet()
    fields = ["index", "text", "animationKey", "rangeCells", "attackDamage",
              "attackIntervalSeconds", "damageMode", "targetPolicy"]
    types = ["int", "string", "string", "float", "int",
             "float", "string", "string"]
    comments = ["索引(主键)", "显示名", "动画键", "攻击距离(格)", "攻击力",
                "攻击间隔(秒)", "伤害模式", "目标策略"]
    write_header(ws, fields, types, comments)
    for u in data["units"]:
        ws.append([
            None,
            u["index"], u["text"], u["animationKey"],
            u["rangeCells"], u["attackDamage"], u["attackIntervalSeconds"],
            u["damageMode"], u["targetPolicy"],
        ])
    save(wb, "unit.xlsx")

    # ── unit_level.xlsx (单例表 one) ──
    wb2, ws2 = new_sheet()
    fields2 = ["maxLevel", "damageLevelMultipliers", "attackSpeedLevelMultipliers"]
    types2 = ["int", "(list#sep=,),float", "(list#sep=,),float"]
    comments2 = ["最大等级", "伤害等级倍数", "攻速等级倍数"]
    write_header(ws2, fields2, types2, comments2)
    ws2.append([
        None,
        data["maxLevel"],
        fmt_list(data["damageLevelMultipliers"]),
        fmt_list(data["attackSpeedLevelMultipliers"]),
    ])
    save(wb2, "unit_level.xlsx")


def gen_wave(data):
    wb, ws = new_sheet()
    fields = ["waveUnitCounts", "bossWaveNumbers", "bossSpawnChances",
              "spawnStrategyWeights", "spawnStrategies"]
    types = ["(list#sep=,),int", "(list#sep=,),int", "(list#sep=,),float",
             "(list#sep=,),int", "(list#sep=;),(list#sep=,),float"]
    comments = ["每波怪物数", "Boss波次号", "Boss出现概率",
                "生成策略权重", "生成策略表"]
    write_header(ws, fields, types, comments)
    strategies = data.get("spawnStrategies", [])
    str_str = ";".join(fmt_list(s) for s in strategies)
    ws.append([
        None,
        fmt_list(data["waveUnitCounts"]),
        fmt_list(data["bossWaveNumbers"]),
        fmt_list(data["bossSpawnChances"]),
        fmt_list(data["spawnStrategyWeights"]),
        str_str,
    ])
    save(wb, "wave.xlsx")


def gen_weapon_registry(data):
    wb, ws = new_sheet()
    fields = ["symbol", "name", "type", "index", "status", "buffDependency"]
    types = ["string", "string", "int", "string", "string?", "string?"]
    comments = ["符号(主键)", "名称", "类型", "索引", "状态", "Buff依赖"]
    write_header(ws, fields, types, comments)
    for w in data["weapons"]:
        ws.append([
            None,
            w["symbol"], w["name"], w["type"], w["index"],
            w.get("status"), w.get("buffDependency"),
        ])
    save(wb, "weapon_registry.xlsx")


def gen_projectile(data):
    wb, ws = new_sheet()
    fields = ["types"]
    types = ["(list#sep=,),string"]
    comments = ["弹道类型列表"]
    write_header(ws, fields, types, comments)
    ws.append([None, fmt_list(data["types"])])
    save(wb, "projectile.xlsx")


def gen_event(data):
    wb, ws = new_sheet()
    fields = ["eventName", "code"]
    types = ["string", "string"]
    comments = ["事件名(主键)", "事件码"]
    write_header(ws, fields, types, comments)
    for name, code in data.items():
        ws.append([None, name, code])
    save(wb, "event.xlsx")


def gen_economy(data):
    wb, ws = new_sheet()
    fields = ["initialGold", "refreshCostStart", "refreshCostIncrement",
              "unitBaseCost", "handSize"]
    types = ["int", "int", "int", "int", "int"]
    comments = ["初始金币", "刷新起始消耗", "刷新递增", "单位基础消耗", "手牌数"]
    write_header(ws, fields, types, comments)
    ws.append([
        None,
        data["initialGold"], data["refreshCostStart"], data["refreshCostIncrement"],
        data["unitBaseCost"], data["handSize"],
    ])
    save(wb, "economy.xlsx")


def gen_result_schema(data):
    wb, ws = new_sheet()
    fields = ["field", "typeDesc"]
    types = ["string", "string"]
    comments = ["字段(主键)", "类型描述"]
    write_header(ws, fields, types, comments)
    for field, desc in data.items():
        ws.append([None, field, desc])
    save(wb, "result_schema.xlsx")


def gen_ai_difficulty(data):
    """ai-difficulty.json 为 4 个难度等级的多维数组，转为逐难度行表。"""
    wb, ws = new_sheet()
    fields = ["difficulty", "decisionIntervalMs", "ni", "ri", "oi",
              "ii", "hi", "ei"]
    types = ["int", "int", "float", "float", "int",
             "(list#sep=,),int", "int", "(list#sep=,),int"]
    comments = ["难度(主键)", "决策间隔(ms)", "ni", "ri", "oi",
                "ii(6值)", "hi", "ei(6值)"]
    write_header(ws, fields, types, comments)
    n = len(data["decisionIntervalMs"])
    ii = data.get("ii", [])
    ei = data.get("ei", [])
    for i in range(n):
        ii_row = ii[i] if i < len(ii) else []
        ws.append([
            None,
            i,
            data["decisionIntervalMs"][i],
            fmt_num(data["ni"][i]),
            fmt_num(data["ri"][i]),
            data["oi"][i] if i < len(data["oi"]) else None,
            fmt_list(ii_row) if ii_row else None,
            data["hi"],
            fmt_list(ei),
        ])
    save(wb, "ai_difficulty.xlsx")


# ──────────────────────────────────────────────────────────
# 主流程
# ──────────────────────────────────────────────────────────

def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    print(f"输出目录: {OUT_DIR}")
    print("开始转换 JSON → xlsx ...\n")

    gen_boss(load_json("bosses.json"))
    gen_buff(load_json("buffs.json"))
    gen_skill(load_json("skills.json"))
    gen_enemy(load_json("enemies.json"))
    gen_general(load_json("generals.json"))
    gen_map(load_json("maps.json"))
    gen_unit_and_level(load_json("units.json"))
    gen_wave(load_json("waves.json"))
    gen_weapon_registry(load_json("weapons.json"))
    gen_projectile(load_json("projectiles.json"))
    gen_event(load_json("events.json"))
    gen_economy(load_json("battle-economy.json"))
    gen_result_schema(load_json("battle-result-schema.json"))
    gen_ai_difficulty(load_json("ai-difficulty.json"))

    print("\n全部完成！共生成 15 个 xlsx 文件。")


if __name__ == "__main__":
    main()
