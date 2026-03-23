workspace "MochiSharp-Managed"
    multiprocessorcompile("On")
    configurations { "Debug", "Release" }

    startproject "Example.Managed"

    BUILD_DIR = "%{wks.location}/bin"
    OUTPUT_DIR = "%{BUILD_DIR}/%{cfg.buildcfg}/%{cfg.platform}"
    INTOUTPUT_DIR = "%{wks.location}/bin/objs/%{cfg.buildcfg}/%{cfg.platform}/%{prj.name}"

    -- Thirdparty
    THIRDPARTY_DIR = "%{wks.location}/ThirdParty"

    -- Projects
    include "MochiSharp.Managed/mochisharp-managed.lua"
    
    group "Example"
    include "Example/Managed/example-managed.lua"
    group ""