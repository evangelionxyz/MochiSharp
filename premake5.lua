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