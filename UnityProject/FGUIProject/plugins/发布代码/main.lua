local isFullPublish = false
local codeRoot = nil
local generatedFiles = {}
local packagePaths = {}
local publishHandlers = {}
-- 收集本次发布所有生成类的元数据，供发布后校验使用
local classRecords = {}
local codeFileMark = CS.FairyEditor.PublishHandler.CODE_FILE_MARK
local genCode = require(PluginPath .. '/GenCode_CSharp')

local function normalizePath(path)
    return string.lower(CS.System.IO.Path.GetFullPath(path))
end

local function getCodeRoot(handler)
    local codeRoot = CS.System.IO.Path.GetFullPath(handler.exportCodePath)
    local expectedRoot = CS.System.IO.Path.GetFullPath(
        CS.System.IO.Path.Combine(App.project.basePath, '../Assets/GameScripts/HotFix/GameFUI/UIBase'))

    if string.lower(codeRoot) ~= string.lower(expectedRoot) then
        error('发布代码目录与受保护目录不一致：' .. codeRoot)
    end

    return codeRoot
end

local function clearStaleGeneratedCode(path)
    if not CS.System.IO.Directory.Exists(path) then
        return 0
    end

    local files = CS.System.IO.Directory.GetFiles(
        path,
        '*.cs',
        CS.System.IO.SearchOption.AllDirectories)
    local deletedCount = 0

    for i = 0, files.Length - 1 do
        local codeFile = files:GetValue(i)
        local normalizedCodeFile = normalizePath(codeFile)

        if generatedFiles[normalizedCodeFile] ~= true then
            local content = CS.System.IO.File.ReadAllText(codeFile)

            if string.find(content, codeFileMark, 1, true) ~= nil then
                CS.System.IO.File.Delete(codeFile)

                local metaFile = codeFile .. '.meta'
                if CS.System.IO.File.Exists(metaFile) then
                    CS.System.IO.File.Delete(metaFile)
                end

                deletedCount = deletedCount + 1
            end
        end
    end

    return deletedCount
end

local function resetPublishState()
    isFullPublish = false
    codeRoot = nil
    generatedFiles = {}
    packagePaths = {}
    publishHandlers = {}
    classRecords = {}
end

-- 读取指定包内某个组件的源 XML 全文本，用于核对组件是否被标记为 Window/Widget。
-- 约定：包资源目录为 <basePath>/assets/<pkgName>，组件文件名以 <resName>.xml 形式记录在 package.xml。
local function readComponentXml(pkgName, resName)
    local assetsDir = CS.System.IO.Path.Combine(App.project.basePath, 'assets/' .. pkgName)
    local targetFile = resName .. '.xml'

    -- 优先尝试包根目录下的直接文件名
    local directPath = CS.System.IO.Path.Combine(assetsDir, targetFile)
    if CS.System.IO.File.Exists(directPath) then
        return CS.System.IO.File.ReadAllText(directPath)
    end

    -- 退化方案：解析 package.xml，依据 path 属性拼接真实路径
    local packageXmlPath = CS.System.IO.Path.Combine(assetsDir, 'package.xml')
    if CS.System.IO.File.Exists(packageXmlPath) then
        local packageContent = CS.System.IO.File.ReadAllText(packageXmlPath)
        local pattern = 'name="' .. targetFile .. '"[^>]*path="([^"]*)"'
        local match = string.match(packageContent, pattern)
        if match ~= nil then
            local resolvedPath = CS.System.IO.Path.Combine(assetsDir, match)
            resolvedPath = CS.System.IO.Path.Combine(resolvedPath, targetFile)
            if CS.System.IO.File.Exists(resolvedPath) then
                return CS.System.IO.File.ReadAllText(resolvedPath)
            end
        end
    end

    return nil
end

-- 判断类名对应的组件是否被标记为 Window（customExtention 含 FUIWindow）。
local function isMarkedAsWindow(record)
    local content = readComponentXml(record.pkgName, record.resName)
    if content == nil then
        return false
    end
    return string.find(content, 'FUIWindow', 1, true) ~= nil
        and string.find(content, 'customExtention', 1, true) ~= nil
end

-- 发布后校验：在所有包代码生成完成后执行，任一条件不满足立即抛出错误中断发布流程。
local function validatePublish()
    local seenUrls = {}

    for _, record in ipairs(classRecords) do
        local className = record.className
        local superClassName = record.superClassName or ''

        -- 条件1：拒绝顶层窗口（标记为 Window 的组件）退回继承 GComponent 而非 FUIWindow。
        if isMarkedAsWindow(record) then
            if string.find(superClassName, 'FUIWindow', 1, true) == nil then
                error(string.format(
                    '发布校验失败：%s 被标记为 Window，但生成类父类为 %s，未继承 FUIWindow。',
                    className, superClassName))
            end
        end

        -- 条件2：拒绝缺失 URL、PkgName、ResName 任一元数据常量。
        if record.url == nil or record.url == ''
            or record.pkgName == nil or record.pkgName == ''
            or record.resName == nil or record.resName == '' then
            error(string.format(
                '发布校验失败：%s 缺失元数据常量（URL/PkgName/ResName 必须齐全）：url=%s pkgName=%s resName=%s',
                className, tostring(record.url), tostring(record.pkgName), tostring(record.resName)))
        end

        -- 条件3：拒绝同一 URL 被多个生成类重复输出。
        if seenUrls[record.url] ~= nil then
            error(string.format(
                '发布校验失败：URL %s 被多个生成类重复输出：%s 与 %s。',
                record.url, seenUrls[record.url], className))
        end
        seenUrls[record.url] = className
    end

    -- 条件4：拒绝新旧命名冲突（同时存在 UI* 与历史 BattleUI*/Common* 命名的包或组件）。
    local uiNames = {}
    local legacyNames = {}
    for _, record in ipairs(classRecords) do
        local pkgName = record.pkgName or ''
        -- 包名层面：UIBattle/UICommon 为新命名，BattleUI/Common 为历史命名
        if string.sub(pkgName, 1, 2) == 'UI' then
            uiNames[pkgName] = true
        end
        if pkgName == 'BattleUI' or pkgName == 'Common' then
            legacyNames[pkgName] = true
        end
        -- 组件资源名层面：同步检测新旧命名共存
        local resName = record.resName or ''
        if string.sub(resName, 1, 2) == 'UI' then
            uiNames['res:' .. resName] = true
        end
        if string.sub(resName, 1, 8) == 'BattleUI' or string.sub(resName, 1, 6) == 'Common' then
            legacyNames['res:' .. resName] = true
        end
    end

    local uiCount = 0
    for _ in pairs(uiNames) do uiCount = uiCount + 1 end
    local legacyCount = 0
    for _ in pairs(legacyNames) do legacyCount = legacyCount + 1 end

    if uiCount > 0 and legacyCount > 0 then
        local legacyList = {}
        for name, _ in pairs(legacyNames) do legacyList[#legacyList + 1] = name end
        error(string.format(
            '发布校验失败：检测到新旧命名冲突，新命名 UI* 与历史命名 %s 同时存在，禁止以不确定资源集继续构建。',
            table.concat(legacyList, ', ')))
    end

    fprint('发布校验通过：父类契约、元数据完整性、URL 唯一性、新旧命名一致性均已确认。')
end

function onPublishStart(pkgs)
    resetPublishState()
    isFullPublish = pkgs.Length == App.project.allPackages.Count
end

function onPublish(handler)
    publishHandlers[#publishHandlers + 1] = handler

    if not handler.genCode then
        return
    end

    codeRoot = getCodeRoot(handler)
    local packagePath = CS.System.IO.Path.Combine(
        codeRoot,
        handler:ToFilename(handler.pkg.name))
    packagePaths[normalizePath(packagePath)] = packagePath

    handler.genCode = false
    local packageGeneratedFiles, packageClassRecords = genCode(handler)

    for filePath, _ in pairs(packageGeneratedFiles) do
        generatedFiles[filePath] = true
    end

    for _, record in ipairs(packageClassRecords) do
        classRecords[#classRecords + 1] = record
    end
end

function onPublishEnd(pkgs)
    local publishSucceeded = true

    for _, handler in ipairs(publishHandlers) do
        if not handler.isSuccess then
            publishSucceeded = false
            break
        end
    end

    if publishSucceeded and codeRoot ~= nil then
        -- 发布后校验：在清理过期代码前执行，校验失败立即抛出错误中断发布流程
        validatePublish()

        local deletedCount = 0

        if isFullPublish then
            deletedCount = clearStaleGeneratedCode(codeRoot)
        else
            for _, packagePath in pairs(packagePaths) do
                deletedCount = deletedCount + clearStaleGeneratedCode(packagePath)
            end
        end

        fprint('已清理过期 FairyGUI 自动生成代码：' .. deletedCount)
    elseif not publishSucceeded then
        fprint('FairyGUI 发布失败，已跳过过期代码和 .meta 清理')
    end

    resetPublishState()
end
