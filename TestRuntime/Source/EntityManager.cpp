// Copyright (c) 2025 Evangelion Manuhutu

#include "EntityManager.h"
#include <iostream>

namespace criollo
{
    EntityManager::EntityManager()
        : m_NextEntityID(1)
    {
    }

    EntityManager::~EntityManager()
    {
        Shutdown(false);
    }

    void EntityManager::Initialize()
    {
        ScriptBindings::Initialize();
        std::cout << "[EntityManager] Initialized" << std::endl;
    }

    void EntityManager::Shutdown(bool callManagedCallbacks)
    {
        if (callManagedCallbacks)
        {
            StopAll();
        }
        m_Scripts.clear();
        m_Entities.clear();
        ScriptBindings::Shutdown();
        std::cout << "[EntityManager] Shutdown" << std::endl;
    }

    Entity* EntityManager::CreateEntity(const std::string& name)
    {
        auto entity = std::make_unique<Entity>();
        entity->id = m_NextEntityID++;
        entity->name = name;
        entity->transform.position = { 0.0f, 0.0f, 0.0f };
        entity->transform.rotation = { 0.0f, 0.0f, 0.0f };
        entity->transform.scale = { 1.0f, 1.0f, 1.0f };

        Entity* entityPtr = entity.get();
        m_Entities.push_back(std::move(entity));

        ScriptBindings::RegisterEntity(entityPtr->id, entityPtr);

        std::cout << "[EntityManager] Created entity: " << entityPtr->name 
                  << " (ID: " << entityPtr->id << ")" << std::endl;

        return entityPtr;
    }

    void EntityManager::DestroyEntity(uint64_t id)
    {
        // Stop and detach script first
        StopEntity(id);
        DetachScript(id);

        // Remove from entities list
        auto it = std::remove_if(m_Entities.begin(), m_Entities.end(),
            [id](const std::unique_ptr<Entity>& entity) {
                return entity->id == id;
            });

        if (it != m_Entities.end())
        {
            m_Entities.erase(it, m_Entities.end());
            ScriptBindings::UnregisterEntity(id);
            std::cout << "[EntityManager] Destroyed entity: " << id << std::endl;
        }
    }

    Entity* EntityManager::GetEntity(uint64_t id)
    {
        return ScriptBindings::GetEntity(id);
    }

    void EntityManager::AttachScript(uint64_t entityID, const std::string& scriptTypeName, void* scriptInstance)
    {
        ScriptInstance instance;
        instance.managedInstance = scriptInstance;
        instance.isStarted = false;
        // Note: Delegates should be set externally before calling StartEntity
        m_Scripts[entityID] = instance;

        std::cout << "[EntityManager] Attached script '" << scriptTypeName 
                  << "' to entity " << entityID << std::endl;
    }

    void EntityManager::DetachScript(uint64_t entityID)
    {
        auto it = m_Scripts.find(entityID);
        if (it != m_Scripts.end())
        {
            m_Scripts.erase(it);
            std::cout << "[EntityManager] Detached script from entity " << entityID << std::endl;
        }
    }

    void EntityManager::SetScriptDelegates(uint64_t entityID, EntityStartDelegate start, EntityUpdateDelegate update, EntityStopDelegate stop)
    {
        auto it = m_Scripts.find(entityID);
        if (it != m_Scripts.end())
        {
            it->second.Start = start;
            it->second.Update = update;
            it->second.Stop = stop;
            std::cout << "[EntityManager] Set delegates for entity " << entityID << std::endl;
        }
        else
        {
            std::cout << "[EntityManager] Warning: Cannot set delegates, entity " << entityID << " has no script attached" << std::endl;
        }
    }

    void EntityManager::StartEntity(uint64_t entityID)
    {
        auto it = m_Scripts.find(entityID);
        if (it != m_Scripts.end() && !it->second.isStarted)
        {
            if (it->second.Start)
            {
                it->second.Start(entityID);
                it->second.isStarted = true;
                std::cout << "[EntityManager] Started entity " << entityID << std::endl;
            }
        }
    }

    void EntityManager::UpdateEntity(uint64_t entityID, float deltaTime)
    {
        auto it = m_Scripts.find(entityID);
        if (it != m_Scripts.end() && it->second.isStarted)
        {
            if (it->second.Update)
            {
                it->second.Update(entityID, deltaTime);
            }
        }
    }

    void EntityManager::StopEntity(uint64_t entityID)
    {
        auto it = m_Scripts.find(entityID);
        if (it != m_Scripts.end() && it->second.isStarted)
        {
            if (it->second.Stop)
            {
                it->second.Stop(entityID);
                it->second.isStarted = false;
                std::cout << "[EntityManager] Stopped entity " << entityID << std::endl;
            }
        }
    }

    void EntityManager::StartAll()
    {
        for (auto& [id, script] : m_Scripts)
        {
            StartEntity(id);
        }
    }

    void EntityManager::UpdateAll(float deltaTime)
    {
        for (auto& [id, script] : m_Scripts)
        {
            UpdateEntity(id, deltaTime);
        }
    }

    void EntityManager::StopAll()
    {
        for (auto& [id, script] : m_Scripts)
        {
            StopEntity(id);
        }
    }
}
