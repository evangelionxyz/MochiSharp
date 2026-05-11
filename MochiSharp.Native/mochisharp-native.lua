project "MochiSharp.Native"
    location "%{wks.location}/MochiSharp.Native"
    kind "StaticLib"
    language "C++"
    cppdialect "c++23"
    architecture "x64"

    targetdir (OUTPUT_DIR)
    objdir (INTOUTPUT_DIR)

    pchheader "PCH.hpp"
    pchsource "Source/PCH.cpp"
    forceincludes {
        "PCH.hpp"
    }

    files {
        "Source/**.cpp",
        "Source/**.hpp"
    }

    includedirs {
        "%{prj.location}/Source",
        "%{prj.location}/Source/MochiSharp",
        "%{IncludeDirs.Hostfxr}"
    }

    libdirs {
        "%{IncludeDirs.Hostfxr}"
    }

    links {
        "%{THIRDPARTY_DIR}/dotnet/host/fxr/9.0.11/x64/nethost.lib"
    }

    filter "system:windows"
        systemversion "latest"
        buildoptions { "/utf-8" }
        defines {
            "_WINDOWS",
            "_WIN32",
            "WIN32_LEAN_AND_MEAN",
            "_CRT_SECURE_NO_WARNINGS"
        }

    filter "configurations:Debug"
        runtime "Debug"
        optimize "off"
        symbols "on"
        defines {
            "_DEBUG",
            "MOCHI_DEBUG"
        }

    filter "configurations:Release"
        runtime "Release"
        optimize "on"
        symbols "off"
        defines {
            "_NDEBUG",
            "NDEBUG"
        }