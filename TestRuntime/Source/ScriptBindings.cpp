// Copyright (c) 2025 Evangelion Manuhutu

#include "ScriptBindings.h"
#include <iostream>

namespace criollo
{
    std::unordered_map<uint64_t, Entity*> ScriptBindings::s_Entities;

    void ScriptBindings::Initialize()
    {
        std::cout << "[ScriptBindings] Initialized" << std::endl;
    }

    void ScriptBindings::Shutdown()
    {
        s_Entities.clear();
        std::cout << "[ScriptBindings] Shutdown" << std::endl;
    }

    void ScriptBindings::RegisterEntity(uint64_t id, Entity* entity)
    {
        if (entity)
        {
            s_Entities[id] = entity;
            std::cout << "[ScriptBindings] Registered entity: " << id << std::endl;
        }
    }

    void ScriptBindings::UnregisterEntity(uint64_t id)
    {
        s_Entities.erase(id);
        std::cout << "[ScriptBindings] Unregistered entity: " << id << std::endl;
    }

    Entity* ScriptBindings::GetEntity(uint64_t id)
    {
        auto it = s_Entities.find(id);
        if (it != s_Entities.end())
            return it->second;
        return nullptr;
    }

    void ScriptBindings::Entity_GetTransform(uint64_t entityID, TransformComponent* outTransform)
    {
        Entity* entity = GetEntity(entityID);
        if (entity && outTransform)
        {
            *outTransform = entity->transform;
        }
    }

    void ScriptBindings::Entity_SetTransform(uint64_t entityID, TransformComponent* transform)
    {
        Entity* entity = GetEntity(entityID);
        if (entity && transform)
        {
            entity->transform = *transform;
        }
    }

    bool ScriptBindings::Entity_HasComponent(uint64_t entityID, const char* componentType)
    {
        Entity* entity = GetEntity(entityID);
        if (!entity)
            return false;

        // For now, we only have Transform component
        // In a real ECS, you'd check the component type
        return true;
    }

    void ScriptBindings::Log(const char* message)
    {
        std::cout << "[C++ Native Log] " << message << std::endl;
    }

    // C-style exports for managed code
    extern "C"
    {
        __declspec(dllexport) void Entity_GetTransform(uint64_t entityID, TransformComponent* outTransform)
        {
            ScriptBindings::Entity_GetTransform(entityID, outTransform);
        }

        __declspec(dllexport) void Entity_SetTransform(uint64_t entityID, TransformComponent* transform)
        {
            ScriptBindings::Entity_SetTransform(entityID, transform);
        }

        __declspec(dllexport) bool Entity_HasComponent(uint64_t entityID, const char* componentType)
        {
            return ScriptBindings::Entity_HasComponent(entityID, componentType);
        }

        __declspec(dllexport) void NativeLog(const char* message)
        {
            ScriptBindings::Log(message);
        }
    }
}
