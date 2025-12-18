project "Criollo"
    location "%{wks.location}/Criollo"
    kind "SharedLib"
    language "C++"
    cppdialect "c++23"
    
    filter "platforms:Any CPU"
        architecture "x64"

    targetdir (OUTPUT_DIR)
    objdir (INTOUTPUT_DIR)

    files {
        "Source/**.cpp",
        "Source/**.h"
    }

    filter "system:windows"
        systemversion "latest"
        buildoptions { "/utf-8" }
        defines {
            "_WINDOWS",
            "WIN32",
            "WIN32_LEAN_AND_MEAN",
            "CRIOLLOCORE_EXPORTS",
            "_CRT_SECURE_NO_WARNINGS"
        }

    filter "configurations:Debug"
        runtime "Debug"
        optimize "off"
        symbols "on"
        defines {
            "_DEBUG",
            "CRIOLLO_DEBUG"
        }

    filter "configurations:Release"
        runtime "Release"
        optimize "on"
        symbols "on"
        defines {
            "_NDEBUG",
            "NDEBUG"
        }

    filter "configurations:Shipping"
        runtime "Release"
        optimize "on"
        symbols "off"
        defines {
            "_NDEBUG",
            "NDEBUG"
        }