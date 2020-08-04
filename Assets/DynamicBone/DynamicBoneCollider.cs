using System;
using Unity.Burst;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Jobs;



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
    [Tooltip("Whether this collider is available for all bones or not")] [SerializeField]
    public bool IsGlobal = false;
    
    [Tooltip("The axis of the capsule's height.")] [SerializeField]
    public Direction Direction = Direction.Y;

    [Tooltip("The center of the sphere or capsule, in the object's local space.")] [SerializeField]
    public Vector3 Center = Vector3.zero;

    [Tooltip("Constrain bones to outside bound or inside bound.")] [SerializeField]
    public Bound Bound = Bound.Outside;

    [Tooltip("The radius of the sphere or capsule.")] [SerializeField]
    public float Radius = 0.5f;

    [Tooltip("The height of the capsule.")] [SerializeField]
    public float Height = 0;

    public ColliderInfo ColliderInfo { get; private set; }

    private static int index;

    private int curColliderIndex;

    private void OnValidate()
    {
        Radius = math.max(Radius, 0);
        Height = math.max(Height, 0);
    }
    
    private void Awake()
    {
        curColliderIndex = index++;
    }

    private void Start()
    {
        DynamicBoneManager.Instance.AddCollider(this);
    }

    private void Update()
    {
        ColliderInfo colliderInfo = new ColliderInfo
        {
            Index = curColliderIndex,
            IsGlobal = IsGlobal,
            Center = Center,
            Radius = Radius,
            Height = Height,
            Direction = Direction,
            Bound = Bound,
            Scale = transform.lossyScale.x, 
            Position = transform.position,
            Rotation = transform.rotation
        };
        DynamicBoneManager.Instance.RefreshColliderInfo(in colliderInfo);
    }

    private void OnDrawGizmosSelected()
    {
        if (!enabled)
            return;

        if (Bound == Bound.Outside)
            Gizmos.color = Color.yellow;
        else
            Gizmos.color = Color.magenta;
        float radius = Radius * math.abs(transform.lossyScale.x);
        float h = Height * 0.5f - Radius;
        if (h <= 0)
        {
            Gizmos.DrawWireSphere(transform.TransformPoint(Center), radius);
        }
        else
        {
            float3 c0 = Center;
            float3 c1 = Center;

            switch (Direction)
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