using System;
using UnityEngine;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using Unity.Collections;

public struct HeadInfo
{
    public int Index;
    public float3 ObjectMove;
    public float3 ObjectPrevPosition;
    public float3 Gravity;
    public float ObjectScale;
    public float Weight;
    public float3 Force;
    public float3 FinalForce;
    public int ParticleCount;
    public int DataOffsetInGlobalArray;

    public float3 RootParentBoneWorldPos;
    public quaternion RootParentBoneWorldRot;
}

public struct ParticleInfo
{
    public int Index;
    public int ParentIndex;
    public float Damping;
    public float Elasticity;
    public float Stiffness;
    public float Inert;
    public float Friction;
    public float Radius;
    public float BoneLength;
    public bool IsCollide;

    public float3 EndOffset;
    public float3 InitLocalPosition;
    public quaternion InitLocalRotation;

    public float3 LocalPosition;
    public quaternion LocalRotation;

    public float3 TempWorldPosition;
    public float3 TempPrevWorldPosition;

    public float3 ParentScale;

    public float3 WorldPosition;
    public quaternion WorldRotation;
}

public class DynamicBone : MonoBehaviour
{
    /// <summary>
    /// 该值为所有动态骨骼链中最长的那一根所包含的节点数量
    /// </summary>
    public const int MaxParticleLimit = 22;

    /// <summary>
    /// 如果你对层级结构有需求则不要拆分，但是最终赋值Transform的时候会无法并行处理
    /// </summary>
    [Tooltip("是否将所有子物体拆分至最上层")]
    public bool Separate = false;
    
    [Tooltip("运动的根骨骼")]
    public Transform Root = null;

    [Tooltip("How much the bones slowed down.")] [Range(0, 1)]
    public float Damping = 0.1f;

    public AnimationCurve DampingDistrib = null;

    [Tooltip("How much the force applied to return each bone to original orientation.")] [Range(0, 1)]
    public float Elasticity = 0.1f;

    public AnimationCurve ElasticityDistrib = null;

    [Tooltip("How much bone's original orientation are preserved.")] [Range(0, 1)]
    public float Stiffness = 0.1f;

    public AnimationCurve StiffnessDistrib = null;

    [Tooltip("How much character's position change is ignored in physics simulation.")] [Range(0, 1)]
    public float Inert = 0;

    public AnimationCurve InertDistrib = null;

    [Tooltip("How much the bones slowed down when collide.")]
    public float Friction = 0;

    public AnimationCurve FrictionDistrib = null;

    [Tooltip("Each bone can be a sphere to collide with colliders. Radius describe sphere's size.")]
    public float Radius = 0;

    public AnimationCurve RadiusDistrib = null;

    [Tooltip("If End Length is not zero, an extra bone is generated at the end of transform hierarchy.")]
    public float EndLength = 0;

    [Tooltip("If End Offset is not zero, an extra bone is generated at the end of transform hierarchy.")]
    public Vector3 EndOffset = Vector3.zero;

    [Tooltip("The force apply to bones. Partial force apply to character's initial pose is cancelled out.")]
    public Vector3 Gravity = Vector3.zero;

    [Tooltip("The force apply to bones.")] public Vector3 Force = Vector3.zero;

    [Tooltip("Collider objects interact with the bones.")]
    public List<DynamicBoneCollider> Colliders = null;

    private float boneTotalLength;
    private float weight = 1.0f;

    [HideInInspector] public NativeArray<ParticleInfo> ParticleInfoArray;
    [HideInInspector] public Transform[] ParticleTransformArray;

    private int particleCount;
    private bool hasInitialized;
    

    /// <summary>
    /// 需要获取首个Particle的父物体，因为所有的Particle都会拆散至Hierarchy最上级，所以需要一个用以计算的基准Tranform
    /// </summary>
    public Transform RootParentTransform { get; private set; }

    [HideInInspector] public HeadInfo HeadInfo;

    private void OnValidate()
    {
        if (!hasInitialized) return;

        Damping = Mathf.Clamp01(Damping);
        Elasticity = Mathf.Clamp01(Elasticity);
        Stiffness = Mathf.Clamp01(Stiffness);
        Inert = Mathf.Clamp01(Inert);
        Friction = Mathf.Clamp01(Friction);
        Radius = Mathf.Max(Radius, 0);

        if (Application.isEditor && Application.isPlaying)
        {
            InitTransforms();
            UpdateParameters();
            DynamicBoneManager.Instance.RefreshHeadInfo(in HeadInfo);
            DynamicBoneManager.Instance.RefreshParticleInfo(in ParticleInfoArray, in HeadInfo.DataOffsetInGlobalArray);
        }
    }

    private void Start()
    {
        if (!Root)
        {
            Root = transform;
        }

        ParticleInfoArray = new NativeArray<ParticleInfo>(MaxParticleLimit, Allocator.Persistent);
        ParticleTransformArray = new Transform[MaxParticleLimit];
        particleCount = 0;
        SetupParticles();
        DynamicBoneManager.Instance.AddBone(this);
    }

    public HeadInfo ResetHeadIndexAndDataOffset(int headIndex)
    {
        HeadInfo.Index = headIndex;
        HeadInfo.DataOffsetInGlobalArray = headIndex * MaxParticleLimit;
        return HeadInfo;
    }

    public void ClearJobData()
    {
        if (ParticleInfoArray.IsCreated)
        {
            ParticleInfoArray.Dispose();
        }

        ParticleTransformArray = null;
    }

    private void SetupParticles()
    {
        if (Root == null)
            return;

        HeadInfo = new HeadInfo
        {
            ObjectPrevPosition = Root.position,
            ObjectScale = math.abs(Root.lossyScale.x),
            Gravity = Gravity,
            Weight = weight,
            Force = Force,
            ParticleCount = 0,
        };

        particleCount = 0;
        RootParentTransform = Root.parent;
        boneTotalLength = 0;
        AppendParticles(Root, -1, 0, ref HeadInfo);

        UpdateParameters();

        if (Separate)
        {
            for (int i = 0; i < particleCount; i++)
            {
                ParticleTransformArray[i].parent = null;
            }
        }

        

        HeadInfo.ParticleCount = particleCount;

        hasInitialized = true;
    }

    private void AppendParticles(Transform b, int parentIndex, float boneLength, ref HeadInfo head)
    {
        ParticleInfo particle = new ParticleInfo
        {
            Index = particleCount,
            ParentIndex = parentIndex
        };

        if (b != null)
        {
            particleCount++;

            particle.InitLocalPosition = b.localPosition;
            particle.InitLocalRotation = b.localRotation;
            particle.LocalPosition = particle.InitLocalPosition;
            particle.LocalRotation = particle.InitLocalRotation;
            particle.TempWorldPosition = particle.TempPrevWorldPosition = particle.WorldPosition = b.position;
            particle.WorldRotation = b.rotation;
            particle.ParentScale = b.parent.lossyScale;
        }
        else // end bone
        {
            Transform pb = ParticleTransformArray[parentIndex];
            if (EndLength > 0)
            {
                Transform ppb = pb.parent;
                if (ppb != null)
                    particle.EndOffset = pb.InverseTransformPoint((pb.position * 2 - ppb.position)) * EndLength;
                else
                    particle.EndOffset = new Vector3(EndLength, 0, 0);
            }
            else
            {
                particle.EndOffset = pb.InverseTransformPoint(Root.TransformDirection(EndOffset) + pb.position);
            }

            particle.TempWorldPosition = particle.TempPrevWorldPosition = pb.TransformPoint(particle.EndOffset);
        }

        if (parentIndex >= 0)
        {
            boneLength += math.distance(ParticleTransformArray[parentIndex].position, particle.TempWorldPosition);
            particle.BoneLength = boneLength;
            boneTotalLength = math.max(boneTotalLength, boneLength);
        }

        ParticleInfoArray[particle.Index] = particle;
        ParticleTransformArray[particle.Index] = b;

        int index = particle.Index;

        if (b != null)
        {
            for (int i = 0; i < b.childCount; ++i)
            {
                AppendParticles(b.GetChild(i), index, boneLength, ref head);
            }

            if (b.childCount == 0 && (EndLength > 0 || EndOffset != Vector3.zero))
                AppendParticles(null, index, boneLength, ref head);
        }
    }

    private void InitTransforms()
    {
        for (int i = 0; i < ParticleInfoArray.Length; ++i)
        {
            ParticleInfo particleInfo = ParticleInfoArray[i];
            particleInfo.LocalPosition = particleInfo.InitLocalPosition;
            particleInfo.LocalRotation = particleInfo.InitLocalRotation;
            ParticleInfoArray[i] = particleInfo;
        }
    }

    private void UpdateParameters()
    {
        if (Root == null)
            return;

        for (int i = 0; i < particleCount; ++i)
        {
            ParticleInfo particle = ParticleInfoArray[i];
            particle.Damping = Damping;
            particle.Elasticity = Elasticity;
            particle.Stiffness = Stiffness;
            particle.Inert = Inert;
            particle.Friction = Friction;
            particle.Radius = Radius;


            if (boneTotalLength > 0)
            {
                float a = particle.BoneLength / boneTotalLength;

                if (DampingDistrib != null && DampingDistrib.keys.Length > 0)
                {
                    particle.Damping *= DampingDistrib.Evaluate(a);
                }

                if (ElasticityDistrib != null && ElasticityDistrib.keys.Length > 0)
                    particle.Elasticity *= ElasticityDistrib.Evaluate(a);
                if (StiffnessDistrib != null && StiffnessDistrib.keys.Length > 0)
                    particle.Stiffness *= StiffnessDistrib.Evaluate(a);
                if (InertDistrib != null && InertDistrib.keys.Length > 0)
                    particle.Inert *= InertDistrib.Evaluate(a);
                if (FrictionDistrib != null && FrictionDistrib.keys.Length > 0)
                    particle.Friction *= FrictionDistrib.Evaluate(a);
                if (RadiusDistrib != null && RadiusDistrib.keys.Length > 0)
                    particle.Radius *= RadiusDistrib.Evaluate(a);
            }

            particle.Damping = Mathf.Clamp01(particle.Damping);
            particle.Elasticity = Mathf.Clamp01(particle.Elasticity);
            particle.Stiffness = Mathf.Clamp01(particle.Stiffness);
            particle.Inert = Mathf.Clamp01(particle.Inert);
            particle.Friction = Mathf.Clamp01(particle.Friction);
            particle.Radius = Mathf.Max(particle.Radius, 0);

            ParticleInfoArray[i] = particle;
        }
    }

    private void OnDestroy()
    {
        ParticleInfoArray.Dispose();
    }
}