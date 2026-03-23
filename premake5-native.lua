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
    include "MochiSharp.Native/mochisharp-native.lua"

    group "Example"
    include "Example/Native/example-native.lua"
    group ""