using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameBattle;
using GameConfig.battle;
using Luban;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 将指定的 Laya 战斗布局确定性转换为 Unity Prefab。
/// </summary>
public static class LayaBattlePrefabImporter
{
    internal const string LAYA_SOURCE_ROOT = "Assets/AssetRaw/Battle/Source/Laya";
    internal const string LAYOUT_ROOT = "Assets/AssetRaw/Battle/Source/Laya/Layout";
    internal const string EXTRACTED_SPRITE_ROOT = "Assets/AssetRaw/Battle/Sprites/Extracted";
    internal const string PREFAB_OUTPUT_ROOT = "Assets/AssetRaw/Battle/Prefabs";

    private static readonly HashSet<string> SupportedNodeTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "Scene",
        "Box",
        "Sprite",
        "Image",
    };

    private static readonly HashSet<string> SceneWorldNodes = new HashSet<string>(StringComparer.Ordinal)
    {
        "bg",
        "box",
    };

    private static readonly HashSet<string> BoxWorldNodes = new HashSet<string>(StringComparer.Ordinal)
    {
        "mapBgImg",
        "mapBgImgNew",
        "mapTitle",
        "map",
        "gameObjectBox",
    };

    private static readonly HashSet<string> MapWorldNodes = new HashSet<string>(StringComparer.Ordinal)
    {
        "road",
        "highGround",
        "bound",
        "divide",
        "end1",
        "end2",
        "pathTip0",
        "pathTip1",
    };

    private static readonly HashSet<string> OldUiNodes = new HashSet<string>(StringComparer.Ordinal)
    {
        "refreshBox",
        "refreshBtn",
        "xBtn",
        "goldBg",
        "shovelAd",
        "deckBtn",
        "specialPropsBox",
        "propsBoxBg",
        "propsBox",
        "propsBoxAi",
        "effectBox",
        "round",
    };

    private const float PIXELS_PER_UNIT = 80f;
    private const float SORTING_DEPTH_STEP = 0.001f;

    // 阿斗 Prefab 资源路径（地图嵌套引用的血量表现模板）。
    private const string ADOU_PREFAB_ASSET = "Assets/AssetRaw/Battle/Prefabs/Targets/ADou.prefab";

    // 枪兵武器表现常量（集中定义，避免魔法数字散落）。
    private const string PIKE_BODY_SKIN = "resources/img/gameObject/soldier/pike.png";
    private const string PIKE_TIP_SKIN = "resources/img/gameObject/soldier/pikeEff1.png";
    private const int PIKE_SORTING_ORDER = 20;

    /// <summary>
    /// 兵种表现 Profile 配置数据文件（由 Luban 导出，LayaBattlePrefabImporter 唯一真源）。
    /// 偏移值均为世界单位，直接映射到 Prefab 节点 localPosition。
    /// </summary>
    private const string SOLDIER_VISUAL_CONFIG_ASSET =
        "Assets/AssetRaw/Configs/bytes/battle_tbsoldiervisual.bytes";

    /// <summary>
    /// 士兵 Body 排序层（与现有实现保持一致）。
    /// </summary>
    private const int SOLDIER_BODY_SORTING_ORDER = 10;

    /// <summary>
    /// 士兵受击特效挂点默认排序层（与现有实现保持一致）。
    /// </summary>
    private const int SOLDIER_EFFECT_SORTING_ORDER = 0;

    /// <summary>
    /// 生成 BattleMap0 的静态世界视觉。
    /// </summary>
    [MenuItem("Tools/Battle/生成 BattleMap0")]
    public static void GenerateBattleMap0()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        LayaAtlasExtractor.ExtractAll();

        GameObject mapRoot = new GameObject("BattleMap0");
        try
        {
            Transform backgroundRoot = CreateChild(mapRoot.transform, "BackgroundRoot");
            Transform boardRoot = CreateChild(mapRoot.transform, "BoardRoot");
            Transform runtimeRoot = CreateChild(mapRoot.transform, "RuntimeRoot");
            CreateChild(runtimeRoot, "EnemyRoot");
            CreateChild(runtimeRoot, "SoldierRoot");
            CreateChild(runtimeRoot, "ProjectileRoot");
            CreateChild(runtimeRoot, "EffectRoot");

            LayaImportNode sceneLayout = ParseLayout($"{LAYOUT_ROOT}/Scene/BattleScene.ls");
            GameObject sceneTree = BuildUnityTree(sceneLayout, new LayaImportContext());
            try
            {
                MoveRequiredChild(
                    sceneTree.transform,
                    "bg",
                    backgroundRoot,
                    "Background",
                    new Vector3(-4f, 7.5f, 0f));
                MoveRequiredChild(
                    sceneTree.transform,
                    "box",
                    boardRoot,
                    "ImportedBoard",
                    new Vector3(-4f, 7.5f, 0f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sceneTree);
            }

            OrganizeBoardRoot(boardRoot);

            LayaImportNode themeLayout = ParseLayout($"{LAYOUT_ROOT}/Prefab/mapBg0.lh");
            GameObject themeRoot = BuildUnityTree(themeLayout, new LayaImportContext());
            themeRoot.name = "ThemeRoot";
            themeRoot.transform.SetParent(backgroundRoot, false);
            themeRoot.transform.localPosition = new Vector3(-4f, 5f, 0f);
            OrganizeThemeRoot(themeRoot.transform);

            SavePrefabDeterministic(
                mapRoot,
                $"{PREFAB_OUTPUT_ROOT}/World/BattleMap0.prefab");
            Debug.Log("[LayaBattlePrefabImporter] 已生成 BattleMap0 静态视觉。");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(mapRoot);
        }
    }

    /// <summary>
    /// 由 mob.lh 壳体与 mob_0 主体生成以脚底中心为原点的敌人 Prefab。
    /// </summary>
    [MenuItem("Tools/Battle/生成 Mob0")]
    public static void GenerateMob0()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        LayaAtlasExtractor.ExtractAll();

        GameObject mobRoot = new GameObject("Mob0");
        try
        {
            LayaImportContext context = new LayaImportContext();
            LayaImportNode shellLayout = ParseLayout($"{LAYOUT_ROOT}/Prefab/mob.lh");
            GameObject visualRoot = BuildUnityTree(shellLayout, context);
            visualRoot.name = "VisualRoot";
            visualRoot.transform.SetParent(mobRoot.transform, false);
            visualRoot.transform.localPosition = new Vector3(-0.5f, 1f, 0f);

            JObject bodyData = new JObject
            {
                ["_$type"] = "Image",
                ["name"] = "Body",
                ["x"] = 14.5f,
                ["y"] = 21f,
                ["width"] = 51f,
                ["height"] = 50f,
                ["skin"] = "resources/img/gameObject/enemy/mob_0.png",
            };
            LayaImportNode bodyNode = ParseNode(
                bodyData,
                "<generated:Mob0>",
                "/Mob0/VisualRoot/Body[0]");
            GameObject body = CreateUnityNode(bodyNode, visualRoot.transform, context);
            SpriteRenderer bodyRenderer = body.GetComponent<SpriteRenderer>();
            bodyRenderer.drawMode = SpriteDrawMode.Simple;
            float bodyScale = bodyRenderer.sprite.pixelsPerUnit / PIXELS_PER_UNIT;
            body.transform.localScale = new Vector3(bodyScale, bodyScale, 1f);

            SetRequiredSpriteSorting(visualRoot.transform, "shadow", 0);
            SetRequiredSpriteSorting(visualRoot.transform, "Body", 10);
            SetRequiredSpriteSorting(visualRoot.transform, "hpBgImg", 20);
            SetRequiredSpriteSorting(visualRoot.transform, "hpBgImg/hpImg1", 21);
            SetRequiredSpriteSorting(visualRoot.transform, "hpBgImg/hpImg2", 22);
            SetRequiredSpriteSorting(visualRoot.transform, "stun", 30);

            // 修正血条锚点：源 mob.lh 中 hpImg1/hpImg2 的 x/y 是相对 hpBgImg 左上角的
            // 居中偏移（(62-54)/2=4, (11-5)/2=3），但 ApplyTransform 对嵌套 Image 子节点
            // 也按中心化公式换算，导致填充条中心被推到背景右边缘（双重中心化）。
            // 这里在生成后重置子节点相对偏移为 0（与背景中心重合），并让背景水平中心
            // 对齐 Body 可见边界中心（Body 中心 x=0.5），使血条不再偏斜。
            Transform hpBgTransform = FindRequiredChild(visualRoot.transform, "hpBgImg");
            Vector3 hpBgPosition = hpBgTransform.localPosition;
            hpBgTransform.localPosition = new Vector3(0.5f, hpBgPosition.y, hpBgPosition.z);
            CenterHealthBarFill(FindRequiredChild(hpBgTransform, "hpImg1"));
            CenterHealthBarFill(FindRequiredChild(hpBgTransform, "hpImg2"));

            // TODO: 待 Buff/Debuff 系统接入后，由状态表现层根据眩晕状态控制该节点显隐。
            FindRequiredChild(visualRoot.transform, "stun").gameObject.SetActive(false);

            Transform hitEffectPoint = CreateChild(mobRoot.transform, "HitEffectPoint");
            hitEffectPoint.localPosition = new Vector3(0f, 0.5f, 0f);
            Transform stunPoint = CreateChild(mobRoot.transform, "StunPoint");
            stunPoint.localPosition = new Vector3(0f, 1.175f, 0f);

            SavePrefabDeterministic(
                mobRoot,
                $"{PREFAB_OUTPUT_ROOT}/Enemies/Mob0.prefab");
            Debug.Log("[LayaBattlePrefabImporter] 已生成 Mob0 敌人 Prefab。");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(mobRoot);
        }
    }

    /// <summary>
    /// 生成四个以脚底中心为原点的静态士兵 Prefab。
    /// </summary>
    [MenuItem("Tools/Battle/生成 Soldier Prefabs")]
    public static void GenerateSoldierPrefabs()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        LayaAtlasExtractor.ExtractAll();

        GenerateSoldierPrefab(
            "KnifeSoldier",
            "resources/img/gameObject/soldier/soldier_0.png",
            "knife");
        GenerateSoldierPrefab(
            "BowSoldier",
            "resources/img/gameObject/soldier/soldier_1.png",
            "bow");
        GenerateSoldierPrefab(
            "SpearSoldier",
            "resources/img/gameObject/soldier/soldier_2.png",
            "pike");
        GenerateSoldierPrefab(
            "CavalrySoldier",
            "resources/img/gameObject/soldier/soldier_3.png",
            "cavalry");

        Debug.Log("[LayaBattlePrefabImporter] 已生成四个 Soldier Prefab。");
    }

    /// <summary>
    /// 读取兵种表现 Profile 配置数据（Luban 导出，唯一真源）。
    /// </summary>
    /// <param name="prefabName">Prefab 名（用于错误提示）。</param>
    /// <param name="animationKey">动画键（对应 unit.xlsx 的 animationKey）。</param>
    /// <returns>匹配的兵种表现 Profile。</returns>
    /// <exception cref="InvalidOperationException">
    /// 配置数据缺失或 animationKey 无匹配记录。
    /// </exception>
    /// <remarks>
    /// <para>直接读取 <c>battle_tbsoldiervisual.bytes</c> 并用生成的
    /// <see cref="TbSoldierVisual"/> 反序列化，不依赖运行时 ConfigSystem/ModuleSystem
    /// （Editor 菜单环境下模块未初始化）。</para>
    /// </remarks>
    private static SoldierVisual LoadSoldierVisualProfile(
        string prefabName, string animationKey)
    {
        TextAsset bytesAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(
            SOLDIER_VISUAL_CONFIG_ASSET);
        if (bytesAsset == null)
        {
            throw new InvalidOperationException(
                $"生成 {prefabName} 失败：缺少兵种表现 Profile 配置数据 " +
                $"{SOLDIER_VISUAL_CONFIG_ASSET}，请先运行 Luban 导出");
        }

        var table = new TbSoldierVisual(new ByteBuf(bytesAsset.bytes));
        SoldierVisual profile = table.GetOrDefault(animationKey);
        if (profile == null)
        {
            throw new InvalidOperationException(
                $"生成 {prefabName} 失败：animationKey={animationKey} " +
                "无匹配的兵种表现 Profile 记录（soldier_visual.xlsx）");
        }

        return profile;
    }

    /// <summary>
    /// 生成以可见区域中心为朝向基点、默认朝上的 Arrow Prefab。
    /// </summary>
    [MenuItem("Tools/Battle/生成 Arrow")]
    public static void GenerateArrow()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        LayaAtlasExtractor.ExtractAll();

        const int sortingOrder = 40;
        Rect visiblePixelRect = new Rect(12f, 11f, 22f, 81f);
        GameObject arrowRoot = new GameObject("Arrow");
        try
        {
            Transform orientationRoot = CreateChild(arrowRoot.transform, "OrientationRoot");
            SpriteRenderer bodyRenderer = CreateGeneratedSpriteBody(
                orientationRoot,
                "resources/img/weapon/arrow_0.png",
                "<generated:Arrow>",
                "/Arrow/OrientationRoot/Body[0]",
                sortingOrder);

            float canvasWidth = bodyRenderer.sprite.rect.width / PIXELS_PER_UNIT;
            float canvasHeight = bodyRenderer.sprite.rect.height / PIXELS_PER_UNIT;
            bodyRenderer.transform.localPosition = new Vector3(
                canvasWidth * 0.5f -
                (visiblePixelRect.x + visiblePixelRect.width * 0.5f) / PIXELS_PER_UNIT,
                canvasHeight * 0.5f -
                (visiblePixelRect.y + visiblePixelRect.height * 0.5f) / PIXELS_PER_UNIT,
                sortingOrder * SORTING_DEPTH_STEP);

            SavePrefabDeterministic(
                arrowRoot,
                $"{PREFAB_OUTPUT_ROOT}/Projectiles/Arrow.prefab");
            Debug.Log("[LayaBattlePrefabImporter] 已生成默认朝上的 Arrow Prefab。");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(arrowRoot);
        }
    }

    private static void GenerateSoldierPrefab(
        string prefabName,
        string skin,
        string animationKey)
    {
        SoldierVisual profile = LoadSoldierVisualProfile(prefabName, animationKey);
        GameObject soldierRoot = new GameObject(prefabName);
        try
        {
            SpriteRenderer bodyRenderer = CreateGeneratedSpriteBody(
                soldierRoot.transform,
                skin,
                $"<generated:{prefabName}>",
                $"/{prefabName}/Body[0]",
                SOLDIER_BODY_SORTING_ORDER);

            // Body 位置来自兵种表现 Profile（世界单位，直接映射 localPosition）。
            // 视觉偏移只影响 Sprite 显示位置，不改变单位逻辑坐标。
            Transform body = bodyRenderer.transform;
            body.localPosition = new Vector3(
                profile.BodyLocalOffset.x,
                profile.BodyLocalOffset.y,
                SOLDIER_BODY_SORTING_ORDER * SORTING_DEPTH_STEP);

            // 默认朝向：defaultFacing<0 时 Body 初始朝左（Prefab 生成时生效）。
            // 当前四兵种 defaultFacing 均为 1，保持既有朝右行为不变。
            if (profile.DefaultFacing < 0)
            {
                Vector3 bodyScale = body.localScale;
                bodyScale.x = -Mathf.Abs(bodyScale.x);
                body.localScale = bodyScale;
            }

            Transform hitEffectPoint = CreateChild(soldierRoot.transform, "HitEffectPoint");
            hitEffectPoint.localPosition = new Vector3(
                profile.HitEffectPointOffset.x,
                profile.HitEffectPointOffset.y,
                SOLDIER_EFFECT_SORTING_ORDER * SORTING_DEPTH_STEP);

            if (profile.HasProjectileOrigin)
            {
                Transform projectileOrigin = CreateChild(soldierRoot.transform, "ProjectileOrigin");
                projectileOrigin.localPosition = new Vector3(
                    profile.ProjectileOriginOffset.x,
                    profile.ProjectileOriginOffset.y,
                    0f);
            }

            if (profile.HasWeaponPivot)
            {
                CreateSpearWeapon(soldierRoot.transform, profile.WeaponPivotOffset);
            }

            SavePrefabDeterministic(
                soldierRoot,
                $"{PREFAB_OUTPUT_ROOT}/Soldiers/{prefabName}.prefab");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(soldierRoot);
        }
    }

    /// <summary>
    /// 为枪兵生成独立的武器节点层级：WeaponPivot → PikeBody/PikeTip。
    /// </summary>
    /// <remarks>
    /// <para><b>层级：</b></para>
    /// <code>
    /// SpearSoldier
    /// ├─ Body
    /// ├─ WeaponPivot   ← 独立旋转挂点（武器旋转不带动角色）
    /// │  ├─ PikeBody   ← pike.png（枪身，"木"字旁）
    /// │  └─ PikeTip    ← pikeEff1.png（枪尖效果，默认隐藏）
    /// ├─ HitEffectPoint
    /// </code>
    /// <para>武器排序层高于 Body，武器挂点偏移来自兵种表现 Profile（WeaponPivotOffset）。</para>
    /// <para>生成后绑定 <see cref="SpearWeaponView"/> 组件引用，运行时不额外加载 Sprite。</para>
    /// </remarks>
    private static void CreateSpearWeapon(Transform soldierRoot, Vector2 weaponPivotOffset)
    {
        Transform weaponPivot = CreateChild(soldierRoot, "WeaponPivot");
        weaponPivot.localPosition = new Vector3(weaponPivotOffset.x, weaponPivotOffset.y, 0f);

        SpriteRenderer pikeBodyRenderer = CreateGeneratedSpriteBody(
            weaponPivot,
            PIKE_BODY_SKIN,
            "<generated:SpearSoldier>",
            "/SpearSoldier/WeaponPivot/PikeBody[0]",
            PIKE_SORTING_ORDER);
        pikeBodyRenderer.gameObject.name = "PikeBody";
        pikeBodyRenderer.transform.localPosition = new Vector3(0f, 0f, 0f);

        SpriteRenderer pikeTipRenderer = CreateGeneratedSpriteBody(
            weaponPivot,
            PIKE_TIP_SKIN,
            "<generated:SpearSoldier>",
            "/SpearSoldier/WeaponPivot/PikeTip[0]",
            PIKE_SORTING_ORDER + 1);
        pikeTipRenderer.gameObject.name = "PikeTip";
        pikeTipRenderer.transform.localPosition = new Vector3(0f, 0f, 0f);
        pikeTipRenderer.gameObject.SetActive(false);

        // 绑定 SpearWeaponView 组件，运行时不额外加载 Sprite（Prefab 静态绑定）。
        SpearWeaponView weaponView = soldierRoot.gameObject.GetComponent<SpearWeaponView>();
        if (weaponView == null)
        {
            weaponView = soldierRoot.gameObject.AddComponent<SpearWeaponView>();
        }

        weaponView.Bind(
            weaponPivot,
            pikeBodyRenderer.transform,
            pikeTipRenderer.transform);
    }

    private static SpriteRenderer CreateGeneratedSpriteBody(
        Transform parent,
        string skin,
        string sourceFile,
        string nodePath,
        int sortingOrder)
    {
        JObject bodyData = new JObject
        {
            ["_$type"] = "Image",
            ["name"] = "Body",
            ["skin"] = skin,
        };
        LayaImportNode bodyNode = ParseNode(bodyData, sourceFile, nodePath);
        GameObject body = CreateUnityNode(bodyNode, parent, new LayaImportContext());
        SpriteRenderer renderer = body.GetComponent<SpriteRenderer>();
        renderer.drawMode = SpriteDrawMode.Simple;
        renderer.sortingOrder = sortingOrder;

        float bodyScale = renderer.sprite.pixelsPerUnit / PIXELS_PER_UNIT;
        body.transform.localScale = new Vector3(bodyScale, bodyScale, 1f);
        body.transform.localPosition = new Vector3(
            0f,
            renderer.sprite.rect.height / (2f * PIXELS_PER_UNIT),
            sortingOrder * SORTING_DEPTH_STEP);
        return renderer;
    }

    /// <summary>
    /// 读取一个 Laya 布局文件并生成带来源路径的节点树。
    /// </summary>
    internal static LayaImportNode ParseLayout(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            throw new ArgumentException("布局资源路径不能为空。", nameof(assetPath));
        }

        string normalizedAssetPath = NormalizeAssetPath(assetPath);
        string fullPath = ToFullPath(normalizedAssetPath);
        if (!File.Exists(fullPath))
        {
            throw new LayaBattleImportException(normalizedAssetPath, "/", "源文件不存在");
        }

        JObject root;
        try
        {
            root = JObject.Parse(File.ReadAllText(fullPath));
        }
        catch (Exception exception) when (exception is IOException || exception is Newtonsoft.Json.JsonException)
        {
            throw new LayaBattleImportException(normalizedAssetPath, "/", $"JSON 读取失败：{exception.Message}");
        }

        string rootName = ReadNodeName(root);
        return ParseNode(root, normalizedAssetPath, $"/{rootName}[0]");
    }

    /// <summary>
    /// 将已解析的节点树转换为仅包含 Unity 原生组件的对象层级。
    /// </summary>
    internal static GameObject BuildUnityTree(LayaImportNode root, LayaImportContext context)
    {
        if (root == null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        return CreateUnityNode(root, null, context);
    }

    /// <summary>
    /// 使用固定资源路径覆盖保存导入器拥有的 Prefab。
    /// </summary>
    internal static GameObject SavePrefabDeterministic(GameObject temporaryRoot, string outputAssetPath)
    {
        if (temporaryRoot == null)
        {
            throw new ArgumentNullException(nameof(temporaryRoot));
        }

        string normalizedOutputPath = NormalizeAssetPath(outputAssetPath);
        if (!normalizedOutputPath.StartsWith(PREFAB_OUTPUT_ROOT + "/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Prefab 输出越出 Battle 输出根目录：{normalizedOutputPath}");
        }

        if (!string.Equals(Path.GetExtension(normalizedOutputPath), ".prefab", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Prefab 输出扩展名无效：{normalizedOutputPath}");
        }

        UnityEngine.Object existingAsset = AssetDatabase.LoadMainAssetAtPath(normalizedOutputPath);
        if (existingAsset != null && !(existingAsset is GameObject))
        {
            throw new InvalidOperationException($"输出路径已被非 Prefab 资源占用：{normalizedOutputPath}");
        }

        ValidatePrefabAddressUnique(normalizedOutputPath);

        string outputDirectory = Path.GetDirectoryName(normalizedOutputPath)?.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new InvalidOperationException($"无法解析 Prefab 输出目录：{normalizedOutputPath}");
        }

        Directory.CreateDirectory(ToFullPath(outputDirectory));
        GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(
            temporaryRoot,
            normalizedOutputPath,
            out bool success);
        if (!success || savedPrefab == null)
        {
            throw new InvalidOperationException($"Prefab 覆盖保存失败：{normalizedOutputPath}");
        }

        AssetDatabase.ImportAsset(normalizedOutputPath, ImportAssetOptions.ForceSynchronousImport);
        return savedPrefab;
    }

    private static void ValidatePrefabAddressUnique(string outputAssetPath)
    {
        string address = Path.GetFileNameWithoutExtension(outputAssetPath);
        string[] conflicts = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/AssetRaw/Battle" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(NormalizeAssetPath)
            .Where(path => !string.Equals(path, outputAssetPath, StringComparison.OrdinalIgnoreCase))
            .Where(path => !string.Equals(path, LAYA_SOURCE_ROOT, StringComparison.OrdinalIgnoreCase)
                && !path.StartsWith(LAYA_SOURCE_ROOT + "/", StringComparison.OrdinalIgnoreCase))
            .Where(path => string.Equals(
                Path.GetFileNameWithoutExtension(path),
                address,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        if (conflicts.Length > 0)
        {
            throw new InvalidOperationException(
                $"Battle Prefab 文件名地址冲突：address={address}, conflicts={string.Join(", ", conflicts)}");
        }
    }

    /// <summary>
    /// 从新临时根构建并覆盖保存，确保场景中不残留导入对象。
    /// </summary>
    internal static GameObject BuildAndSavePrefab(LayaImportNode root, string outputAssetPath)
    {
        GameObject temporaryRoot = BuildUnityTree(root, new LayaImportContext());
        temporaryRoot.name = Path.GetFileNameWithoutExtension(outputAssetPath);
        try
        {
            return SavePrefabDeterministic(temporaryRoot, outputAssetPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(temporaryRoot);
        }
    }

    private static Transform CreateChild(Transform parent, string name)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent, false);
        return child.transform;
    }

    private static void SetRequiredSpriteSorting(
        Transform root,
        string relativePath,
        int sortingOrder)
    {
        Transform target = root.Find(relativePath);
        SpriteRenderer renderer = target == null ? null : target.GetComponent<SpriteRenderer>();
        if (renderer == null)
        {
            throw new InvalidOperationException(
                $"导入布局缺少 SpriteRenderer：{root.name}/{relativePath}");
        }

        renderer.sortingOrder = sortingOrder;
        Vector3 position = target.localPosition;
        position.z = sortingOrder * SORTING_DEPTH_STEP;
        target.localPosition = position;
    }

    private static void OrganizeThemeRoot(Transform themeRoot)
    {
        Transform mountains = themeRoot.Find("mountains");
        if (mountains == null)
        {
            throw new InvalidOperationException($"导入布局缺少节点：{themeRoot.name}/mountains");
        }

        mountains.name = "Mountains";
        MoveChildrenWithPrefix(themeRoot, CreateChild(themeRoot, "Birds"), "bird");
        MoveChildrenWithPrefix(themeRoot, CreateChild(themeRoot, "Deer"), "deer");
    }

    private static void MoveChildrenWithPrefix(
        Transform sourceParent,
        Transform targetParent,
        string namePrefix)
    {
        List<Transform> matches = new List<Transform>();
        for (int index = 0; index < sourceParent.childCount; index++)
        {
            Transform child = sourceParent.GetChild(index);
            if (child != targetParent &&
                child.name.StartsWith(namePrefix, StringComparison.Ordinal))
            {
                matches.Add(child);
            }
        }

        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                $"导入布局缺少节点前缀：{sourceParent.name}/{namePrefix}*");
        }

        foreach (Transform child in matches)
        {
            child.SetParent(targetParent, false);
        }
    }

    private static void OrganizeBoardRoot(Transform boardRoot)
    {
        Transform importedBoard = FindRequiredChild(boardRoot, "ImportedBoard");
        Transform importedMap = FindRequiredChild(importedBoard, "map");
        Transform ground = CreateChild(boardRoot, "Ground");

        MoveRequiredChildPreserveWorld(importedMap, "road", boardRoot, "Road");
        MoveRequiredChildPreserveWorld(importedMap, "highGround", boardRoot, "HighGround");
        MoveRequiredChildPreserveWorld(importedMap, "divide", boardRoot, "Divide");
        MoveRequiredChildPreserveWorld(importedBoard, "gameObjectBox", boardRoot, "UnitSlotRoot");

        // 原始路径终点 end1/end2 是"80×80 终点格"的左上角，取其格中心作为路径终点锚点
        // （阿斗与终点格共用的视觉真源），而非使用节点左上角或硬编码格子坐标。
        // 不整体移动道路、棋盘或单位，只修正终点目标的视觉锚点。
        Vector3 playerEndAnchor = ExtractEndNodeCenter(importedMap, "end1");
        Vector3 opponentEndAnchor = ExtractEndNodeCenter(importedMap, "end2");
        RemoveStaticEndNode(importedMap, "end1");
        RemoveStaticEndNode(importedMap, "end2");

        List<Transform> groundVisuals = new List<Transform>();
        for (int index = 0; index < importedBoard.childCount; index++)
        {
            groundVisuals.Add(importedBoard.GetChild(index));
        }

        foreach (Transform child in groundVisuals)
        {
            child.SetParent(ground, true);
        }

        UnityEngine.Object.DestroyImmediate(importedBoard.gameObject);
        Transform spawnPointRoot = CreateChild(boardRoot, "SpawnPointRoot");
        CreateGridPoint(spawnPointRoot, "PlayerSpawn", 0, 8);
        CreateGridPoint(spawnPointRoot, "OpponentSpawn", 7, 1);

        Transform pathAnchorRoot = CreateChild(boardRoot, "PathAnchorRoot");
        CreateAnchorPoint(pathAnchorRoot, "PlayerEndAnchor", playerEndAnchor);
        CreateAnchorPoint(pathAnchorRoot, "OpponentEndAnchor", opponentEndAnchor);

        // 终点锚点与逻辑格原点重合时，终点节点即锚点位置；否则终点节点位于锚点之下、
        // 用于承载终点格逻辑（放置/寻路），由 BattleMapBindings 校验二者重合。
        Transform endPointRoot = CreateChild(boardRoot, "EndPointRoot");
        CreateAnchorPoint(endPointRoot, "PlayerEnd", playerEndAnchor);
        CreateAnchorPoint(endPointRoot, "OpponentEnd", opponentEndAnchor);

        EmbedAdou(endPointRoot.Find("PlayerEnd"));
        EmbedAdou(endPointRoot.Find("OpponentEnd"));
    }

    /// <summary>
    /// 提取原始路径终点节点（end1/end2）的"80×80 终点格中心"世界位置。
    /// z 归零：终点锚点与旧格子坐标保持一致 z=0，避免排序深度偏移。
    /// </summary>
    private static Vector3 ExtractEndNodeCenter(Transform importedMap, string endNodeName)
    {
        Transform endNode = FindRequiredChild(importedMap, endNodeName);
        // 终点格 80×80（80ppu=1 世界单位）：半格 = 40px = 0.5 世界单位，
        // 终点节点坐标是左上角（Laya Y 轴向下），中心 = 左上角 + (0.5, -0.5)。
        Vector3 localCenter = endNode.localPosition + new Vector3(0.5f, -0.5f, 0f);
        Vector3 worldCenter = importedMap.TransformPoint(localCenter);
        worldCenter.z = 0f;
        return worldCenter;
    }

    /// <summary>移除原始路径终点自带的静态心（heartBox），避免与运行时血量重复。</summary>
    private static void RemoveStaticEndNode(Transform importedMap, string endNodeName)
    {
        Transform endNode = importedMap.Find(endNodeName);
        if (endNode != null)
        {
            UnityEngine.Object.DestroyImmediate(endNode.gameObject);
        }
    }

    private static void CreateAnchorPoint(Transform parent, string name, Vector3 worldPosition)
    {
        Transform point = CreateChild(parent, name);
        point.position = worldPosition;
    }

    /// <summary>
    /// 在终点节点下嵌入阿斗 Prefab：脚底落在终点锚点（本地坐标 (0,0,0)），
    /// 血量节点（HealthRoot）作为阿斗子节点随地图固定，运行时不额外实例化。
    /// </summary>
    private static void EmbedAdou(Transform endPoint)
    {
        GameObject adouPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ADOU_PREFAB_ASSET);
        if (adouPrefab == null)
        {
            throw new InvalidOperationException(
                $"嵌入阿斗失败：缺少模板 Prefab {ADOU_PREFAB_ASSET}");
        }

        GameObject adou = (GameObject)PrefabUtility.InstantiatePrefab(adouPrefab);
        if (adou == null)
        {
            throw new InvalidOperationException(
                $"嵌入阿斗失败：无法实例化 {ADOU_PREFAB_ASSET}");
        }

        adou.transform.SetParent(endPoint, false);
        adou.transform.localPosition = Vector3.zero;
        adou.name = "ADou";
    }

    private static Transform CreateGridPoint(
        Transform parent,
        string name,
        int gridX,
        int gridY)
    {
        if (gridX < 0 || gridX >= 8 || gridY < 0 || gridY >= 10)
        {
            throw new ArgumentOutOfRangeException(
                nameof(gridX),
                $"战斗格坐标越界：({gridX}, {gridY})，有效范围为 8×10。");
        }

        Transform point = CreateChild(parent, name);
        point.localPosition = new Vector3(
            gridX + 0.5f - 4f,
            5f - (gridY + 0.5f),
            0f);
        return point;
    }

    private static Transform FindRequiredChild(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child == null)
        {
            throw new InvalidOperationException($"导入布局缺少节点：{parent.name}/{childName}");
        }

        return child;
    }

    /// <summary>
    /// 把血条填充子节点重置为相对背景居中（偏移 0），修正嵌套子节点双重中心化导致的偏斜。
    /// </summary>
    /// <param name="fill">血条填充子节点 Transform（hpImg1/hpImg2）。</param>
    /// <remarks>
    /// <para>源 mob.lh 中填充节点 x/y 是相对父（hpBgImg）左上角的居中偏移，但
    /// <see cref="ApplyTransform"/> 对嵌套 Image 子节点也按中心化公式换算，导致
    /// 填充中心被推到背景边缘。本方法把子节点相对父的中心偏移重置为 0，
    /// 使填充与背景中心完全重合。保留 z 排序深度与旋转/缩放。</para>
    /// </remarks>
    private static void CenterHealthBarFill(Transform fill)
    {
        Vector3 position = fill.localPosition;
        fill.localPosition = new Vector3(0f, 0f, position.z);
    }

    private static void MoveRequiredChildPreserveWorld(
        Transform sourceParent,
        string sourceName,
        Transform targetParent,
        string targetName)
    {
        Transform child = FindRequiredChild(sourceParent, sourceName);
        child.SetParent(targetParent, true);
        child.name = targetName;
    }

    private static void MoveRequiredChild(
        Transform sourceParent,
        string sourceName,
        Transform targetParent,
        string targetName,
        Vector3 positionOffset)
    {
        Transform child = sourceParent.Find(sourceName);
        if (child == null)
        {
            throw new InvalidOperationException($"导入布局缺少节点：{sourceParent.name}/{sourceName}");
        }

        child.SetParent(targetParent, false);
        child.name = targetName;
        child.localPosition += positionOffset;
    }

    private static GameObject CreateUnityNode(
        LayaImportNode node,
        Transform parent,
        LayaImportContext context)
    {
        if (!string.IsNullOrWhiteSpace(node.PrefabFile))
        {
            return CreatePrefabInstance(node, parent, context);
        }

        GameObject nodeObject = new GameObject(node.Name);
        Transform transform = nodeObject.transform;
        transform.SetParent(parent, false);
        bool isImage = string.Equals(node.NodeType, "Image", StringComparison.Ordinal);
        int sortingOrder = isImage ? context.NextSortingOrder++ : -1;
        ApplyTransform(transform, node, sortingOrder);

        if (isImage)
        {
            SpriteRenderer renderer = nodeObject.AddComponent<SpriteRenderer>();
            ApplyImageProperties(renderer, node, sortingOrder);
        }

        for (int index = 0; index < node.Children.Count; index++)
        {
            CreateUnityNode(node.Children[index], transform, context);
        }

        return nodeObject;
    }

    private static GameObject CreatePrefabInstance(
        LayaImportNode instanceNode,
        Transform parent,
        LayaImportContext context)
    {
        string prefabFile = NormalizeAssetPath(instanceNode.PrefabFile);
        if (!context.ActivePrefabFiles.Add(prefabFile))
        {
            throw new LayaBattleImportException(
                instanceNode.SourceFile,
                instanceNode.NodePath,
                $"检测到 Prefab 循环引用：{prefabFile}");
        }

        try
        {
            LayaImportNode prefabRoot = ParseLayout(prefabFile);
            JObject mergedData = MergePrefabData(prefabRoot.Data, instanceNode.Data);
            LayaImportNode mergedNode = ParseNode(mergedData, prefabFile, instanceNode.NodePath);
            return CreateUnityNode(mergedNode, parent, context);
        }
        finally
        {
            context.ActivePrefabFiles.Remove(prefabFile);
        }
    }

    private static JObject MergePrefabData(JObject prefabData, JObject instanceData)
    {
        JObject merged = (JObject)prefabData.DeepClone();
        foreach (JProperty property in instanceData.Properties())
        {
            if (string.Equals(property.Name, "_$prefab", StringComparison.Ordinal)
                || string.Equals(property.Name, "_$child", StringComparison.Ordinal))
            {
                continue;
            }

            merged[property.Name] = property.Value.DeepClone();
        }

        if (instanceData["_$child"] is JArray instanceChildren)
        {
            JArray mergedChildren = merged["_$child"] as JArray;
            if (mergedChildren == null)
            {
                mergedChildren = new JArray();
                merged["_$child"] = mergedChildren;
            }

            foreach (JToken child in instanceChildren)
            {
                mergedChildren.Add(child.DeepClone());
            }
        }

        merged.Remove("_$prefab");
        return merged;
    }

    private static void ApplyTransform(Transform transform, LayaImportNode node, int sortingOrder)
    {
        float x = ReadFloat(node, "x", 0f);
        float y = ReadFloat(node, "y", 0f);
        float width = ReadFloat(node, "width", 0f);
        float height = ReadFloat(node, "height", 0f);
        float anchorX = ReadFloat(node, "anchorX", 0f);
        float anchorY = ReadFloat(node, "anchorY", 0f);
        bool isImage = string.Equals(node.NodeType, "Image", StringComparison.Ordinal);
        float centerX = isImage ? x + (0.5f - anchorX) * width : x - anchorX * width;
        float centerY = isImage ? y + (0.5f - anchorY) * height : y - anchorY * height;

        transform.localPosition = LayaPixelsToUnityPosition(centerX, centerY, sortingOrder);
        transform.localRotation = Quaternion.Euler(0f, 0f, -ReadFloat(node, "rotation", 0f));
        transform.localScale = new Vector3(
            ReadFloat(node, "scaleX", 1f),
            ReadFloat(node, "scaleY", 1f),
            1f);

        bool active = ReadBool(node, "active", true);
        bool visible = ReadBool(node, "visible", true);
        transform.gameObject.SetActive(active && visible);
    }

    private static void ApplyImageProperties(SpriteRenderer renderer, LayaImportNode node, int sortingOrder)
    {
        if (!string.IsNullOrWhiteSpace(node.Skin))
        {
            renderer.sprite = ResolveSprite(node);
        }

        renderer.sortingOrder = sortingOrder;
        renderer.drawMode = SpriteDrawMode.Sliced;
        float defaultWidth = renderer.sprite == null ? 0f : renderer.sprite.rect.width;
        float defaultHeight = renderer.sprite == null ? 0f : renderer.sprite.rect.height;
        renderer.size = new Vector2(
            PixelsToWorld(ReadFloat(node, "width", defaultWidth)),
            PixelsToWorld(ReadFloat(node, "height", defaultHeight)));

        float alpha = Mathf.Clamp01(ReadFloat(node, "alpha", 1f));
        renderer.color = new Color(1f, 1f, 1f, alpha);
        renderer.enabled = ReadBool(node, "visible", true);
    }

    private static Sprite ResolveSprite(LayaImportNode node)
    {
        string expectedAssetPath = GetExpectedSpriteAssetPath(node);
        string spriteName = Path.GetFileNameWithoutExtension(expectedAssetPath);
        string[] matches = AssetDatabase.FindAssets($"{spriteName} t:Sprite", new[] { EXTRACTED_SPRITE_ROOT })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => string.Equals(
                NormalizeAssetPath(path),
                expectedAssetPath,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        if (matches.Length == 0)
        {
            throw new LayaBattleImportException(
                node.SourceFile,
                node.NodePath,
                $"skin 无匹配 Sprite：skin={node.Skin}, expected={expectedAssetPath}");
        }

        if (matches.Length > 1)
        {
            throw new LayaBattleImportException(
                node.SourceFile,
                node.NodePath,
                $"skin 匹配多个 Sprite：skin={node.Skin}, matches={string.Join(", ", matches)}");
        }

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(matches[0]);
        if (sprite == null)
        {
            throw new LayaBattleImportException(
                node.SourceFile,
                node.NodePath,
                $"skin 对应资源不是单 Sprite：skin={node.Skin}, asset={matches[0]}");
        }

        return sprite;
    }

    private static string GetExpectedSpriteAssetPath(LayaImportNode node)
    {
        string skin = NormalizeAssetPath(node.Skin);
        string relativePath;
        if (skin.StartsWith("resources/img/map/", StringComparison.Ordinal))
        {
            relativePath = "Map/" + skin.Substring("resources/img/map/".Length);
        }
        else if (skin.StartsWith("resources/img/battleUI/", StringComparison.Ordinal))
        {
            relativePath = "Map/BattleUI/" + skin.Substring("resources/img/battleUI/".Length);
        }
        else if (skin.StartsWith("resources/img/gameObject/", StringComparison.Ordinal))
        {
            relativePath = "GameObject/" + skin.Substring("resources/img/gameObject/".Length);
        }
        else if (skin.StartsWith("resources/img/weapon/", StringComparison.Ordinal))
        {
            relativePath = "Weapon/" + skin.Substring("resources/img/weapon/".Length);
        }
        else
        {
            throw new LayaBattleImportException(
                node.SourceFile,
                node.NodePath,
                $"skin 不属于允许的 Battle 源目录：{node.Skin}");
        }

        return NormalizeAssetPath($"{EXTRACTED_SPRITE_ROOT}/{relativePath}");
    }

    private static float ReadFloat(LayaImportNode node, string propertyName, float defaultValue)
    {
        JToken token = node.Data[propertyName];
        if (token == null)
        {
            return defaultValue;
        }

        if (float.TryParse(
            token.ToString(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out float value))
        {
            return value;
        }

        throw new LayaBattleImportException(
            node.SourceFile,
            node.NodePath,
            $"属性 {propertyName} 不是有效数字：{token}");
    }

    private static bool ReadBool(LayaImportNode node, string propertyName, bool defaultValue)
    {
        JToken token = node.Data[propertyName];
        if (token == null)
        {
            return defaultValue;
        }

        if (bool.TryParse(token.ToString(), out bool value))
        {
            return value;
        }

        throw new LayaBattleImportException(
            node.SourceFile,
            node.NodePath,
            $"属性 {propertyName} 不是有效布尔值：{token}");
    }

    private static float PixelsToWorld(float pixels)
    {
        return pixels / PIXELS_PER_UNIT;
    }

    /// <summary>
    /// 将 Laya 左上原点像素坐标转换为 Unity XY 世界坐标。
    /// </summary>
    internal static Vector3 LayaPixelsToUnityPosition(float x, float y, int sortingOrder = -1)
    {
        float sortingDepth = sortingOrder < 0 ? 0f : sortingOrder * SORTING_DEPTH_STEP;
        return new Vector3(PixelsToWorld(x), -PixelsToWorld(y), sortingDepth);
    }

    private static LayaImportNode ParseNode(JObject data, string sourceFile, string nodePath)
    {
        string prefabReference = data["_$prefab"]?.Value<string>();
        string nodeType = data["_$type"]?.Value<string>();
        bool isKnownIgnoredNode = string.Equals(nodeType, "Text", StringComparison.Ordinal)
            && string.Equals(ReadNodeName(data), "hpNum", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(prefabReference)
            && !isKnownIgnoredNode
            && (string.IsNullOrWhiteSpace(nodeType) || !SupportedNodeTypes.Contains(nodeType)))
        {
            throw new LayaBattleImportException(
                sourceFile,
                nodePath,
                $"不支持的节点类型：{nodeType ?? "<missing>"}");
        }

        LayaImportNode node = new LayaImportNode(data, sourceFile, nodePath)
        {
            NodeType = !string.IsNullOrWhiteSpace(prefabReference)
                ? "PrefabReference"
                : isKnownIgnoredNode ? "Ignored" : nodeType,
            Name = ReadNodeName(data),
            Skin = data["skin"]?.Value<string>(),
        };

        if (!string.IsNullOrWhiteSpace(prefabReference))
        {
            node.PrefabFile = ResolvePrefabAssetPath(sourceFile, prefabReference, nodePath);
        }

        JToken childToken = data["_$child"];
        if (childToken == null)
        {
            return node;
        }

        if (!(childToken is JArray children))
        {
            throw new LayaBattleImportException(sourceFile, nodePath, "_$child 必须是数组");
        }

        for (int index = 0; index < children.Count; index++)
        {
            if (!(children[index] is JObject childData))
            {
                throw new LayaBattleImportException(sourceFile, nodePath, $"_$child[{index}] 不是对象");
            }

            string childName = ReadNodeName(childData);
            string childPath = $"{nodePath}/{childName}[{index}]";
            if (ShouldSkipOldUi(sourceFile, childName))
            {
                continue;
            }

            ValidateWorldChild(sourceFile, node, childName, childPath);
            node.Children.Add(ParseNode(childData, sourceFile, childPath));
        }

        return node;
    }

    private static bool ShouldSkipOldUi(string sourceFile, string childName)
    {
        if (!IsBattleScene(sourceFile))
        {
            return false;
        }

        return OldUiNodes.Contains(childName)
            || childName.StartsWith("danger", StringComparison.Ordinal);
    }

    private static void ValidateWorldChild(
        string sourceFile,
        LayaImportNode parent,
        string childName,
        string childPath)
    {
        if (!IsBattleScene(sourceFile))
        {
            return;
        }

        HashSet<string> allowedNames = null;
        if (string.Equals(parent.NodeType, "Scene", StringComparison.Ordinal))
        {
            allowedNames = SceneWorldNodes;
        }
        else if (string.Equals(parent.Name, "box", StringComparison.Ordinal))
        {
            allowedNames = BoxWorldNodes;
        }
        else if (string.Equals(parent.Name, "map", StringComparison.Ordinal))
        {
            allowedNames = MapWorldNodes;
        }

        if (allowedNames != null && !allowedNames.Contains(childName))
        {
            throw new LayaBattleImportException(
                sourceFile,
                childPath,
                $"未知的战斗世界节点，必须更新白名单或黑名单：{childName}");
        }
    }

    private static bool IsBattleScene(string sourceFile)
    {
        return string.Equals(Path.GetFileName(sourceFile), "BattleScene.ls", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolvePrefabAssetPath(string sourceFile, string prefabReference, string nodePath)
    {
        string sourceDirectory = Path.GetDirectoryName(ToFullPath(sourceFile));
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            throw new LayaBattleImportException(sourceFile, nodePath, "无法解析源文件目录");
        }

        string referencePath = prefabReference.Replace('/', Path.DirectorySeparatorChar);
        string fullPrefabPath = Path.GetFullPath(Path.Combine(sourceDirectory, referencePath));
        string layoutRootPath = ToFullPath(LAYOUT_ROOT).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!fullPrefabPath.StartsWith(layoutRootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new LayaBattleImportException(
                sourceFile,
                nodePath,
                $"Prefab 引用越出布局根目录：{prefabReference}");
        }

        if (!File.Exists(fullPrefabPath))
        {
            throw new LayaBattleImportException(sourceFile, nodePath, $"Prefab 引用不存在：{prefabReference}");
        }

        return ToAssetPath(fullPrefabPath);
    }

    private static string ReadNodeName(JObject data)
    {
        string name = data["name"]?.Value<string>();
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        string nodeType = data["_$type"]?.Value<string>();
        return string.IsNullOrWhiteSpace(nodeType) ? "Node" : nodeType;
    }

    private static string NormalizeAssetPath(string assetPath)
    {
        return assetPath.Replace('\\', '/');
    }

    private static string ToFullPath(string assetPath)
    {
        DirectoryInfo projectRoot = Directory.GetParent(Application.dataPath);
        if (projectRoot == null)
        {
            throw new InvalidOperationException($"无法定位 Unity 工程根目录：{Application.dataPath}");
        }

        return Path.GetFullPath(Path.Combine(projectRoot.FullName, assetPath));
    }

    private static string ToAssetPath(string fullPath)
    {
        DirectoryInfo projectRoot = Directory.GetParent(Application.dataPath);
        if (projectRoot == null)
        {
            throw new InvalidOperationException($"无法定位 Unity 工程根目录：{Application.dataPath}");
        }

        string relativePath = Path.GetRelativePath(projectRoot.FullName, fullPath);
        return NormalizeAssetPath(relativePath);
    }
}

/// <summary>
/// 描述一个带来源信息的 Laya 节点。
/// </summary>
internal sealed class LayaImportNode
{
    internal LayaImportNode(JObject data, string sourceFile, string nodePath)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        SourceFile = sourceFile ?? throw new ArgumentNullException(nameof(sourceFile));
        NodePath = nodePath ?? throw new ArgumentNullException(nameof(nodePath));
    }

    internal JObject Data { get; }

    internal string SourceFile { get; }

    internal string NodePath { get; }

    internal string NodeType { get; set; }

    internal string Name { get; set; }

    internal string PrefabFile { get; set; }

    internal string Skin { get; set; }

    internal List<LayaImportNode> Children { get; } = new List<LayaImportNode>();
}

/// <summary>
/// 保存单次导入过程的递归与排序状态。
/// </summary>
internal sealed class LayaImportContext
{
    internal readonly HashSet<string> ActivePrefabFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    internal int NextSortingOrder;
}

/// <summary>
/// 表示包含 Laya 源文件和节点路径的导入错误。
/// </summary>
public sealed class LayaBattleImportException : Exception
{
    internal LayaBattleImportException(string sourceFile, string nodePath, string message)
        : base($"Laya 战斗布局导入失败：source={sourceFile}, node={nodePath}, reason={message}")
    {
        SourceFile = sourceFile;
        NodePath = nodePath;
    }

    public string SourceFile { get; }

    public string NodePath { get; }
}
