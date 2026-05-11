#pragma once
#ifndef MOCHI_TYPE_CACHE_HPP
#define MOCHI_TYPE_CACHE_HPP

#include "Core.hpp"
#include "StableVector.hpp"

namespace mochi
{
    class Type;

    class [[deprecated(MOCHI_GLOBAL_ALC_MSG)]] TypeCache
    {
    public:
        [[deprecated(MOCHI_GLOBAL_ALC_MSG)]]
        static TypeCache &Get();

        [[deprecated(MOCHI_GLOBAL_ALC_MSG)]]
        Type *CacheType(Type &&InType);

        [[deprecated(MOCHI_GLOBAL_ALC_MSG_P(ManagedAssembly::GetLocalType))]]
        Type *GetTypeByName(std::string_view InName) const;

        [[deprecated(MOCHI_GLOBAL_ALC_MSG)]]
        Type *GetTypeByID(TypeId InTypeID) const;

        [[deprecated(MOCHI_GLOBAL_ALC_MSG)]]
        void Clear();

    private:
        StableVector<Type> m_Types;
        std::unordered_map<std::string, Type *> m_NameCache;
        std::unordered_map<TypeId, Type *> m_IDCache;
    };
}

#endif