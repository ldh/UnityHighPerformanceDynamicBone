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
    }
}

