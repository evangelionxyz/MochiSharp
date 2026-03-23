// Copyright (c) 2025 Evangelion Manuhutu

#include "Host.h"

#include <thread>
#include <chrono>
#include <print>
#include <string>

namespace ExampleInterop
{
    struct Vector3
    {
        float X;
        float Y;
        float Z;
    };

    struct Transform
    {
        Vector3 Position;
        Vector3 Rotation;
        Vector3 Scale;
    };
}

enum ScriptMethodSig : int
{
    Void = 0,
    Void_Float = 1,
    Void_Int = 2,
    Void_Bool = 3,

    Int_IntInt = 10,
    Vector3_Vector3Vector3 = 11,
    Void_Transform = 12,
    Transform = 13,
};

struct ScriptInstance
{
    MochiSharp::DotNetHost* Host;
    uint64_t InstanceId = 0;
    int OnAwake = 0;
    int OnStart = 0;
    int OnUpdate = 0;
    int SetTransform = 0;
    int GetTransform = 0;

    void Init(MochiSharp::DotNetHost* host, uint64_t instanceId, const char* typeName)
    {
        Host = host;
        InstanceId = instanceId;
        if (Host->CreateInstance(typeName, instanceId))
        {
            std::println("[C++] Created instance {} of type {}", instanceId, typeName);
            OnAwake = Host->BindInstanceMethod(instanceId, "OnAwake", ScriptMethodSig::Void);
            OnStart = Host->BindInstanceMethod(instanceId, "OnStart", ScriptMethodSig::Void);
            OnUpdate = Host->BindInstanceMethod(instanceId, "OnUpdate", ScriptMethodSig::Void_Float);
            SetTransform = Host->BindInstanceMethod(instanceId, "SetTransform", ScriptMethodSig::Void_Transform);
            GetTransform = Host->BindInstanceMethod(instanceId, "GetTransform", ScriptMethodSig::Transform);
        }
        else
        {
            std::println("[C++] Failed to create instance {}", InstanceId);
        }
    }

    void Awake() { if (OnAwake) Host->Invoke(OnAwake, nullptr, 0, nullptr); }
    void Start() { if (OnStart) Host->Invoke(OnStart, nullptr, 0, nullptr); }
    void Update(float dt) { if (OnUpdate) { void* args[] = { &dt }; Host->Invoke(OnUpdate, args, 1, nullptr); } }
    
    void SetTx(const ExampleInterop::Transform& t) { 
        if (SetTransform) { 
            void* args[] = { const_cast<ExampleInterop::Transform*>(&t) }; 
            Host->Invoke(SetTransform, args, 1, nullptr); 
        } 
    }
    
    ExampleInterop::Transform GetTx() {
        ExampleInterop::Transform t{};
        if (GetTransform) Host->Invoke(GetTransform, nullptr, 0, &t);
        return t;
    }
};

#ifdef _WIN32
int __cdecl wmain(int argc, wchar_t *argv[])
#else
int main(int argc, char *argv[])
#endif
{
    // MochiSharp::HostSettings settings;

    MochiSharp::DotNetHost host;
    if (!host.Init(L"MochiSharp.Managed.runtimeconfig.json"))
    {
        return 1;
    }

    // Load the script assembly
    if (!host.LoadAssembly("Example.Managed.dll"))
    {
        return 1;
    }

    // Register signatures (the core stays generic; the app defines what these IDs mean).
    // Note: use assembly-qualified names for app-defined structs.
    const char *vector3Type = "Example.Managed.Interop.Vector3, Example.Managed";
    const char *transformType = "Example.Managed.Interop.Transform, Example.Managed";

    {
        host.RegisterSignature(ScriptMethodSig::Void, "System.Void", nullptr, 0);
    }

    {
        const char *p1[] = { "System.Single" };
        host.RegisterSignature(ScriptMethodSig::Void_Float, "System.Void", p1, 1);
    }

    {
        const char *p2[] = { "System.Int32", "System.Int32" };
        host.RegisterSignature(ScriptMethodSig::Int_IntInt, "System.Int32", p2, 2);
    }

    {
        const char *p2[] = { vector3Type, vector3Type };
        host.RegisterSignature(ScriptMethodSig::Vector3_Vector3Vector3, vector3Type, p2, 2);
    }

    {
        const char *p1[] = { transformType };
        host.RegisterSignature(ScriptMethodSig::Void_Transform, "System.Void", p1, 1);
    }

    {
        host.RegisterSignature(ScriptMethodSig::Transform, transformType, nullptr, 0);
    }

    // Create multiple script instances
    ScriptInstance player1;
    player1.Init(&host, 1, "Example.Managed.Scripts.Player");

    ScriptInstance player2;
    player2.Init(&host, 2, "Example.Managed.Scripts.Player");

    player1.Awake();
    player2.Awake();

    player1.Start();
    player2.Start();

    // Set different transforms to prove independence
    ExampleInterop::Transform t1 = { {1,1,1}, {0,0,0}, {1,1,1} };
    player1.SetTx(t1);

    ExampleInterop::Transform t2 = { {2,2,2}, {0,45,0}, {2,2,2} };
    player2.SetTx(t2);

    // Verify state
    auto t1_out = player1.GetTx();
    auto t2_out = player2.GetTx();
    
    std::println("[C++] Player 1 Pos: {},{},{}", t1_out.Position.X, t1_out.Position.Y, t1_out.Position.Z);
    std::println("[C++] Player 2 Pos: {},{},{}", t2_out.Position.X, t2_out.Position.Y, t2_out.Position.Z);

    bool running = true;
    auto start = std::chrono::steady_clock::now();

    int runningCount = 0;
    while (running && runningCount <= 10)
    {
        auto end = std::chrono::steady_clock::now();
        float deltaTime = std::chrono::duration_cast<std::chrono::milliseconds>(end - start).count() / 1000.0f;
        start = end;

        player1.Update(deltaTime);
        player2.Update(deltaTime);
        
        std::this_thread::sleep_for(std::chrono::milliseconds(16));
        runningCount++;
    }

    return 0;
}
