local function genCode(handler)
    local settings = handler.project:GetSettings("Publish").codeGeneration
    local codePkgName = handler:ToFilename(handler.pkg.name)
    local exportCodePath = handler.exportCodePath .. '/' .. codePkgName
    local namespaceName = codePkgName
    local generatedFiles = {}
    -- 收集每个生成类的元数据，供发布后校验使用
    local classRecords = {}

    if settings.packageName ~= nil and settings.packageName ~= '' then
        namespaceName = settings.packageName .. '.' .. namespaceName
    end

    local classes = handler:CollectClasses(settings.ignoreNoname, settings.ignoreNoname, nil)
    CS.System.IO.Directory.CreateDirectory(exportCodePath)

    local getMemberByName = settings.getMemberByName
    local classCnt = classes.Count
    local writer = CodeWriter.new()

    for i = 0, classCnt - 1 do
        local classInfo = classes[i]
        local members = classInfo.members
        writer:reset()

        writer:writeln('using FairyGUI;')
        writer:writeln('using FairyGUI.Utils;')
        writer:writeln()
        writer:writeln('namespace %s', namespaceName)
        writer:startBlock()
        writer:writeln('public partial class %s : %s', classInfo.className, classInfo.superClassName)
        writer:startBlock()

        local memberCnt = members.Count
        for j = 0, memberCnt - 1 do
            local memberInfo = members[j]
            writer:writeln('public %s %s;', memberInfo.type, memberInfo.varName)
        end

        writer:writeln('public const string URL = "ui://%s%s";', handler.pkg.id, classInfo.resId)
        writer:writeln('public const string PkgName = "%s";', handler.pkg.name)
        writer:writeln('public const string ResName = "%s";', classInfo.resName)
        writer:writeln()

        -- 记录本类的元数据，供发布后校验继承契约、元数据完整性和 URL 唯一性
        classRecords[#classRecords + 1] = {
            className = classInfo.className,
            superClassName = classInfo.superClassName,
            url = 'ui://' .. handler.pkg.id .. classInfo.resId,
            pkgName = handler.pkg.name,
            resName = classInfo.resName,
            pkgId = handler.pkg.id,
        }

        writer:writeln('public static %s CreateInstance()', classInfo.className)
        writer:startBlock()
        writer:writeln('return (%s)UIPackage.CreateObject("%s", "%s");', classInfo.className, handler.pkg.name, classInfo.resName)
        writer:endBlock()
        writer:writeln()

        if handler.project.type == ProjectType.MonoGame then
            writer:writeln('protected override void OnConstruct()')
            writer:startBlock()
        else
            writer:writeln('public override void ConstructFromXML(XML xml)')
            writer:startBlock()
            writer:writeln('base.ConstructFromXML(xml);')
            writer:writeln()
        end

        for j = 0, memberCnt - 1 do
            local memberInfo = members[j]
            if memberInfo.group == 0 then
                if getMemberByName then
                    writer:writeln('%s = (%s)GetChild("%s");', memberInfo.varName, memberInfo.type, memberInfo.name)
                else
                    writer:writeln('%s = (%s)GetChildAt(%s);', memberInfo.varName, memberInfo.type, memberInfo.index)
                end
            elseif memberInfo.group == 1 then
                if getMemberByName then
                    writer:writeln('%s = GetController("%s");', memberInfo.varName, memberInfo.name)
                else
                    writer:writeln('%s = GetControllerAt(%s);', memberInfo.varName, memberInfo.index)
                end
            else
                if getMemberByName then
                    writer:writeln('%s = GetTransition("%s");', memberInfo.varName, memberInfo.name)
                else
                    writer:writeln('%s = GetTransitionAt(%s);', memberInfo.varName, memberInfo.index)
                end
            end
        end

        writer:endBlock()
        writer:endBlock()
        writer:endBlock()
        local outputPath = exportCodePath .. '/' .. classInfo.className .. '.cs'
        writer:save(outputPath)
        generatedFiles[string.lower(CS.System.IO.Path.GetFullPath(outputPath))] = true
    end

    writer:reset()

    local binderName = codePkgName .. 'Binder'

    writer:writeln('using FairyGUI;')
    writer:writeln()
    writer:writeln('namespace %s', namespaceName)
    writer:startBlock()
    writer:writeln('public class %s', binderName)
    writer:startBlock()
    writer:writeln('public static void BindAll()')
    writer:startBlock()

    for i = 0, classCnt - 1 do
        local classInfo = classes[i]
        writer:writeln('UIObjectFactory.SetPackageItemExtension(%s.URL, typeof(%s));', classInfo.className, classInfo.className)
    end

    writer:endBlock()
    writer:endBlock()
    writer:endBlock()
    local binderPath = exportCodePath .. '/' .. binderName .. '.cs'
    writer:save(binderPath)
    generatedFiles[string.lower(CS.System.IO.Path.GetFullPath(binderPath))] = true

    return generatedFiles, classRecords
end

return genCode
