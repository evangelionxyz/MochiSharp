#pragma once
#ifndef MOCHI_ATTRIBUTE_HPP
#define MOCHI_ATTRIBUTE_HPP

#include "Core.hpp"
#include "String.hpp"

namespace mochi
{
    class Type;

    class Attribute
    {
    public:
        Type &GetType();

        template<typename TReturn>
        TReturn GetFieldValue(std::string_view fieldName)
        {
            TReturn result;
            GetFieldValueInternal(fieldName, &result);
            return result;
        }

    private:
        void GetFieldValueInternal(std::string_view fieldName, void *outValue) const;

    private:
        ManagedHandle m_Handle = -1;
        Type *m_Type = nullptr;

        friend class Type;
        friend class MethodInfo;
        friend class FieldInfo;
        friend class PropertyInfo;
    };
}

#endif