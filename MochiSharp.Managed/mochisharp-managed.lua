project "MochiSharp.Managed"
    location "%{wks.location}/MochiSharp.Managed"
    kind "SharedLib"
    language "C#"
    dotnetframework "net10.0"
    vsprops {
        AppendTargetFrameworkToOutputPath = "false",
        Nullable = "enable",
        AllowUnsafeBlocks = "true",
        CopyLocalLockFileAssemblies = "true",
        EnableDynamicLoading = "true",
        ImplicitUsing = "enable"
    }

    -- Don't specify architecture here. (see https://github.com/premake/premake-core/issues/1758)

    targetdir (OUTPUT_DIR)
    objdir (INTOUTPUT_DIR)

    files {
        "Managed/**.cs",
        "Interop/**.cs",
    }
