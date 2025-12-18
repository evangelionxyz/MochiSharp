project "TestRuntime"
    location "%{wks.location}/TestRuntime"
    kind "ConsoleApp"
    language "C++"
    cppdialect "c++23"

    targetdir (OUTPUT_DIR)
    objdir (INTOOUTPUT_DIR)

    files {
        "Source/**.cpp",
        "Source/**.h"
    }

    links {
        "Criollo"
    }

    filter "system:windows"
        systemversion "latest"
        buildoptions { "/utf-8" }
        defines {
            "_WINDOWS",
            "WIN32",
            "WIN32_LEAN_AND_MEAN",
            "_CRT_SECURE_NO_WARNINGS",
            "_CONSOLE"
        }

    filter "configurations:Debug"
        runtime "Debug"
        optimize "off"
        symbols "on"
        defines { "_DEBUG" }

    filter "configurations:Release"
        runtime "Release"
        optimize "speed"
        symbols "on"
        defines { "NDEBUG" }

    filter "configurations:Shipping"
        runtime "Release"
        optimize "speed"
        symbols "off"
        defines { "NDEBUG" }
    
    filter "platforms:Any CPU"
        architecture "x64"
