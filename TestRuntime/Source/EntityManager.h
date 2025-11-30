// Copyright (c) 2025 Evangelion Manuhutu

#ifndef ENTITY_MANAGER_H
#define ENTITY_MANAGER_H

#include "Entity.h"
#include "ScriptBindings.h"
#include <vector>
#include <memory>
#include <windows.h>

#if defined(_WIN32)
    #ifndef CORECLR_DELEGATE_CALLTYPE
        #define CORECLR_DELEGATE_CALLTYPE __stdcall
    #endif
#else
    #ifndef CORECLR_DELEGATE_CALLTYPE
        #define CORECLR_DELEGATE_CALLTYPE
    #endif
#endif

namespace criollo
{
    // Delegates for C# entity lifecycle methods
    typedef void (CORECLR_DELEGATE_CALLTYPE *EntityStartDelegate)(uint64_t entityID);
    typedef void (CORECLR_DELEGATE_CALLTYPE *EntityUpdateDelegate)(uint64_t entityID, float deltaTime);
    typedef void (CORECLR_DELEGATE_CALLTYPE *EntityStopDelegate)(uint64_t entityID);
    typedef void* (CORECLR_DELEGATE_CALLTYPE *CreateEntityInstanceDelegate)(const char* typeName, uint64_t entityID);

    struct ScriptInstance
    {
        void* managedInstance;
        EntityStartDelegate Start;
        EntityUpdateDelegate Update;
        EntityStopDelegate Stop;
        bool isStarted = false;
    };

    class EntityManager
    {
    public:
        EntityManager();
        ~EntityManager();

        void Initialize();
        void Shutdown();

        // Entity management
        Entity* CreateEntity(const std::string& name);
        void DestroyEntity(uint64_t id);
        Entity* GetEntity(uint64_t id);

        // Script management
        void AttachScript(uint64_t entityID, const std::string& scriptTypeName, void* scriptInstance);
        void DetachScript(uint64_t entityID);
        void SetScriptDelegates(uint64_t entityID, EntityStartDelegate start, EntityUpdateDelegate update, EntityStopDelegate stop);

        // Lifecycle
        void StartEntity(uint64_t entityID);
        void UpdateEntity(uint64_t entityID, float deltaTime);
        void StopEntity(uint64_t entityID);

        // Batch operations
        void StartAll();
        void UpdateAll(float deltaTime);
        void StopAll();

    private:
        uint64_t m_NextEntityID;
        std::vector<std::unique_ptr<Entity>> m_Entities;
        std::unordered_map<uint64_t, ScriptInstance> m_Scripts;
    };
}

#endif // ENTITY_MANAGER_H
