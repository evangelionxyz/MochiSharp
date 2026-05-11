#pragma once
#ifndef MOCHI_PROPERTY_INFO_HPP
#define MOCHI_PROPERTY_INFO_HPP

#include "Core.hpp"
#include "String.hpp"

namespace mochi
{
    class Type;
    class Attribute;

    class PropertyInfo
    {
    public:
        String GetName() const;
        Type &GetType();

        std::vector<Attribute> GetAttributes() const;

    private:
        ManagedHandle m_Handle = -1;
        Type *m_Type = nullptr;

        friend class Type;
    };
}

#endif