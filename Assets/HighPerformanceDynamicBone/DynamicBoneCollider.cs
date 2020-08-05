using System;
using Unity.Burst;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Jobs;
using UnityEngine.Serialization;

namespace HighPerformanceDynamicBone
{
    public enum Direction
    {
        X,
        Y,
        Z
    }

    public enum Bound
    {
        Outside,
        Inside
    }

    public struct ColliderInfo
    {
        public int Index;
        public bool IsGlobal;
        public Bound Bound;
        public float Height;
        public float Radius;
        public float3 Center;
        public Direction Direction;
        public float Scale;
        public float3 Position;
        public quaternion Rotation;

    }

    public class DynamicBoneCollider : MonoBehaviour
    {
        [Tooltip("是否为全局碰撞器")] [SerializeField] private bool isGlobal = false;

        [Tooltip("碰撞器半径")] [SerializeField] private float radius = 0.5f;

        [Tooltip("高度，大于0即为胶囊体")] [SerializeField]
        private float height = 0;

        [Tooltip("高度的轴向")] [SerializeField] private Direction direction = Direction.Y;

        [Tooltip("碰撞器中心位置， 相对于挂载物体的局部空间")] [SerializeField]
        private Vector3 center = Vector3.zero;

        [Tooltip("把骨骼束缚在外面或里面")] [SerializeField]
        private Bound bound = Bound.Outside;


        [HideInInspector] public ColliderInfo ColliderInfo;

        private bool hasInitialized;

        private void OnValidate()
        {
            if (!hasInitialized) return;
            if (Application.isEditor && Application.isPlaying)
            {
                ColliderInfo.Radius = math.max(radius, 0);
                ColliderInfo.Height = math.max(height, 0);
                ColliderInfo.Bound = bound;
                ColliderInfo.Center = center;
                ColliderInfo.Direction = direction;
                DynamicBoneManager.Instance.RefreshColliderInfo(ColliderInfo);
            }
        }

        private void Awake()
        {
            ColliderInfo = new ColliderInfo
            {
                IsGlobal = isGlobal,
                Center = center,
                Radius = radius,
                Height = height,
                Direction = direction,
                Bound = bound,
                Scale = transform.lossyScale.x,
            };
            DynamicBoneManager.Instance.AddCollider(this);
            hasInitialized = true;
        }

        private void OnDrawGizmosSelected()
        {
            if (!enabled)
                return;

            if (bound == Bound.Outside)
                Gizmos.color = Color.yellow;
            else
                Gizmos.color = Color.magenta;
            float radius = this.radius * math.abs(transform.lossyScale.x);
            float h = height * 0.5f - this.radius;
            if (h <= 0)
            {
                Gizmos.DrawWireSphere(transform.TransformPoint(center), radius);
            }
            else
            {
                float3 c0 = center;
                float3 c1 = center;

                switch (direction)
                {
                    case Direction.X:
                        c0.x -= h;
                        c1.x += h;
                        break;
                    case Direction.Y:
                        c0.y -= h;
                        c1.y += h;
                        break;
                    case Direction.Z:
                        c0.z -= h;
                        c1.z += h;
                        break;
                }

                Gizmos.DrawWireSphere(transform.TransformPoint(c0), radius);
                Gizmos.DrawWireSphere(transform.TransformPoint(c1), radius);
            }
        }
    }
}
