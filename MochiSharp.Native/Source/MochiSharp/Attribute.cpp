#include "PCH.hpp"
#include "Attribute.hpp"
#include "Type.hpp"
#include "TypeCache.hpp"
#include "String.hpp"

#include "MochiManagedFunctions.hpp"

namespace mochi
{
    Type &Attribute::GetType()
    {
        if (!m_Type)
        {
            Type type;
            s_ManagedFunctions.GetAttributeTypeFptr(m_Handle, &type.m_Id);
            m_Type = TypeCache::Get().CacheType(std::move(type));
        }

        return *m_Type;
    }

    template<>
    std::string Attribute::GetFieldValue(std::string_view fieldName)
    {
        String result;
        GetFieldValueInternal(fieldName, &result);
        return std::string(result);
    }

    template<>
    bool Attribute::GetFieldValue(std::string_view fieldName)
    {
        Bool32 result;
        GetFieldValueInternal(fieldName, &result);
        return result;
    }

    void Attribute::GetFieldValueInternal(std::string_view fieldName, void *OutValue) const
    {
        auto filedNameStr = String::New(fieldName);
        s_ManagedFunctions.GetAttributeFieldValueFptr(m_Handle, filedNameStr, OutValue);
        String::Free(filedNameStr);
    }
}