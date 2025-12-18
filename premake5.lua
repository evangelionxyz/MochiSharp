workspace "Criollo"
    flags { "MultiProcessorCompile" }
    configurations {
        "Debug",
        "Release",
        "Shipping"
    }

    platforms { "Any CPU" }


    BUILD_DIR = "%{wks.location}/bin"
    OUTPUT_DIR = "%{BUILD_DIR}/%{cfg.buildcfg}/%{cfg.platform}"
    INTOUTPUT_DIR = "%{wks.location}/bin/objs/%{cfg.buildcfg}/%{cfg.platform}/%{prj.name}"

    -- Projects
    include "Criollo/criollo.lua"
    include "TestRuntime/test-runtime.lua"
    include "TestScript/test-script.lua"