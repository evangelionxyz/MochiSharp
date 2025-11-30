// Copyright (c) 2025 Evangelion Manuhutu

#ifndef ENTITY_H
#define ENTITY_H

#include "Math.h"
#include <string>

// Test ECS Simple
namespace criollo
{
    struct TransformComponent
    {
		math::Vector3 position;
		math::Vector3 rotation;
		math::Vector3 scale;

        TransformComponent() = default;
    };
    
    struct Entity
    {
        std::string name;
        uint64_t id;
        TransformComponent transform;
        
        Entity() = default;
    };
}

#endif