// Copyright (c) 2025 Evangelion Manuhutu

#ifndef MATH_H
#define MATH_H

namespace criollo::math
{
    struct Vector3
    {
        float x, y, z;

        Vector3() : x(0.0f), y(0.0f), z(0.0f)
        {
        }

        Vector3(float x, float y, float z) : x(x), y(y), z(z)
        {   
        }

        Vector3 operator+(const Vector3 &other)
        {
            return Vector3{x + other.x, y + other.y, z + other.z };
        }
        
        Vector3 operator-(const Vector3 &other)
        {
            return Vector3{x - other.x, y - other.y, z - other.z };
        }

        Vector3 operator/(const Vector3 &other)
        {
            return Vector3{x / other.x, y / other.y, z / other.z };
        }

        Vector3 operator*(const Vector3 &other)
        {
            return Vector3{x * other.x, y * other.y, z * other.z };
        }

    };
}

#endif