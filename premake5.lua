workspace "MochiSharp"
    multiprocessorcompile("On")
    configurations {"Debug", "Release" }
    architecture "x64"

    startproject "Example.Native"

    BUILD_DIR = "%{wks.location}/bin"
    OUTPUT_DIR = "%{BUILD_DIR}/%{cfg.buildcfg}/%{cfg.platform}"
    INTOUTPUT_DIR = "%{wks.location}/bin/objs/%{cfg.buildcfg}/%{cfg.platform}/%{prj.name}"

    defines { "_CRT_SECURE_NO_WARNINGS" }

    -- Thirdparty
    THIRDPARTY_DIR = "%{wks.location}/ThirdParty"

    IncludeDirs = {}
    IncludeDirs["Hostfxr"] = "%{wks.location}/NetCore/include"

    -- Projects
    include "MochiSharp.Managed/mochisharp-managed.lua"
    include "MochiSharp.Native/mochisharp-native.lua"
    
    group "Example"
    include "Example/Managed/example-managed.lua"
    include "Example/Native/example-native.lua"
    group ""

-- Generate .vcxproj.user for C++ projects that host .NET via hostfxr.
-- Sets debugger type to "Mixed (.NET Core, .NET 5+)" and points the symbol
-- search path at the per-configuration output directory so VS finds both
-- native and managed .pdb files automatically on every F5 launch.
local function writeMixedDebuggerUserFile(prj, wksLocation)
    local configs = {
        { name = "Debug",    platform = "x64" },
        { name = "Release",  platform = "x64" },
    }

    -- Use the actual project name (e.g. "MochiSharp.Native") for the filename
    local userFile = path.join(prj.location, prj.name .. ".vcxproj.user")
    local binDir = path.join(wksLocation, "bin")

    local f = io.open(userFile, "w")
    if not f then
        print("[premake] WARNING: could not write " .. userFile)
        return
    end

    f:write("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n")
    f:write("<Project ToolsVersion=\"Current\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">\n")

    -- ShowAllFiles — editor-wide preference, harmless on Engine too
    f:write("  <PropertyGroup>\n")
    f:write("    <ShowAllFiles>true</ShowAllFiles>\n")
    f:write("  </PropertyGroup>\n")

    for _, cfg in ipairs(configs) do
        local condition = string.format("'$(Configuration)|$(Platform)'=='%s|%s'", cfg.name, cfg.platform)
        -- Absolute symbol search path for this configuration
        local symPath = path.join(binDir, cfg.name)

        f:write(string.format("  <PropertyGroup Condition=\"%s\">\n", condition))
        f:write("    <DebuggerFlavor>WindowsLocalDebugger</DebuggerFlavor>\n")
        f:write("    <LocalDebuggerDebuggerType>NativeWithManagedCore</LocalDebuggerDebuggerType>\n")
        f:write("    <LocalDebuggerWorkingDirectory>$(ProjectDir)</LocalDebuggerWorkingDirectory>\n")
        f:write(string.format("    <LocalDebuggerSymbolPath>%s</LocalDebuggerSymbolPath>\n", symPath))
        f:write("  </PropertyGroup>\n")
    end

    f:write("</Project>\n")
    f:close()
    print("[premake] Generated " .. userFile)
end

-- Automatically generate MSBuild properties to combat Any CPU mapping bugs for Slnx when forcing x64 workspace architecture
require "vstudio"
premake.override(premake.action, "call", function(base, name)
    base(name)
    for wks in premake.global.eachWorkspace() do
        for prj in premake.workspace.eachproject(wks) do
            if prj.language == "C#" then
                local wksPath = wks.location
                local binPath = path.join(wksPath, "bin")
                -- Calculate relative path to the workspace bin folder
                local relBin = path.getrelative(prj.location, binPath) .. "\\\\"
                local propsFile = path.join(prj.location, "Directory.Build.props")
                
                local f = io.open(propsFile, "w")
                if f then
                    f:write("<Project>\n")
                    f:write("  <PropertyGroup>\n")
                    f:write("    <BaseOutputPath>$(SolutionDir)Bin</BaseOutputPath>\n")
                    f:write("    <IntermediateOutputPath>$(SolutionDir)Bin/objs/$(MSBuildProjectName)/</IntermediateOutputPath>\n")
                    f:write("    <DebugType>pdbonly</DebugType>\n")
                    f:write("    <Nullable>enable</Nullable>\n")
                    f:write("    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>\n")
                    f:write("    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>\n")
                    f:write("    <GenerateRuntimeConfigurationFiles>true</GenerateRuntimeConfigurationFiles>\n")
                    f:write("  </PropertyGroup>\n")
                    f:write("</Project>\n")
                    f:close()
                end
            end
        end
    end
end)