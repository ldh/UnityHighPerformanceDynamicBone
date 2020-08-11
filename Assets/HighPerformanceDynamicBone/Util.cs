using UnityEngine;
using Unity.Mathematics;

namespace HighPerformanceDynamicBone
{
    public static class Util
    {
        public static float3 LocalToWorldPosition(float3 parentPosition,quaternion  parentRotation, float3 targetLocalPosition)
        {
            return parentPosition + math.mul(parentRotation, targetLocalPosition);
        }

        public static quaternion LocalToWorldRotation(quaternion  parentRotation, quaternion targetLocalRotation)
        {
            return math.mul(parentRotation, targetLocalRotation);
        }
        
        public static float3 WorldToLocalPosition(float3 parentPosition,quaternion  parentRotation, float3 targetWorldPosition)
        {
            return float3.zero;
        }

        public static quaternion WorldToLocalRotation(quaternion  parentRotation, quaternion targetWorldRotation)
        {
            return quaternion.identity;
        }
    }
}

