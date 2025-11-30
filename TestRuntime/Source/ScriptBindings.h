// Copyright (c) 2025 Evangelion Manuhutu

#ifndef SCRIPT_BINDINGS_H
#define SCRIPT_BINDINGS_H

#include "Entity.h"
#include <unordered_map>
#include <memory>

namespace criollo
{
    // Script bindings for Entity system
    class ScriptBindings
    {
    public:
        static void Initialize();
        static void Shutdown();

        // Entity management
        static void RegisterEntity(uint64_t id, Entity* entity);
        static void UnregisterEntity(uint64_t id);
        static Entity* GetEntity(uint64_t id);

        // Internal call implementations
        static void Entity_GetTransform(uint64_t entityID, TransformComponent* outTransform);
        static void Entity_SetTransform(uint64_t entityID, TransformComponent* transform);
        static bool Entity_HasComponent(uint64_t entityID, const char* componentType);
        static void Log(const char* message);

    private:
        static std::unordered_map<uint64_t, Entity*> s_Entities;
    };

    // C-style functions for managed delegates
    extern "C"
    {
        __declspec(dllexport) void Entity_GetTransform(uint64_t entityID, TransformComponent* outTransform);
        __declspec(dllexport) void Entity_SetTransform(uint64_t entityID, TransformComponent* transform);
        __declspec(dllexport) bool Entity_HasComponent(uint64_t entityID, const char* componentType);
        __declspec(dllexport) void NativeLog(const char* message);
    }
}

#endif // SCRIPT_BINDINGS_H
