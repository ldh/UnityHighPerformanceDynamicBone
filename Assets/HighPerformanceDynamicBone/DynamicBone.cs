using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;

namespace HighPerformanceDynamicBone
{
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
        public bool IsEndBone;

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
        [Tooltip("运动的根骨骼")] [SerializeField] private Transform rootBoneTransform = null;

        [Tooltip("阻尼：How much the bones slowed down.")] [SerializeField] [Range(0, 1)]
        private float damping = 0.1f;

        [SerializeField] private AnimationCurve dampingDistribution = null;

        [Tooltip("弹性：How much the force applied to return each bone to original orientation.")]
        [SerializeField]
        [Range(0, 1)]
        private float elasticity = 0.1f;

        [SerializeField] private AnimationCurve elasticityDistribution = null;

        [Tooltip("刚度：How much bone's original orientation are preserved.")] [SerializeField] [Range(0, 1)]
        private float stiffness = 0.1f;

        [SerializeField] private AnimationCurve stiffnessDistribution = null;

        [Tooltip("惰性：How much character's position change is ignored in physics simulation.")]
        [SerializeField]
        [Range(0, 1)]
        private float inert = 0;

        [SerializeField] private AnimationCurve inertDistribution = null;

        [Tooltip("摩擦力：How much the bones slowed down when collide.")] [SerializeField]
        private float friction = 0;

        [SerializeField] private AnimationCurve frictionDistribution = null;

        [Tooltip("半径：Each bone can be a sphere to collide with colliders. Radius describe sphere's size.")] [SerializeField]
        private float radius = 0;

        [SerializeField] private AnimationCurve radiusDistribution = null;

        [Tooltip("The force apply to bones. Partial force apply to character's initial pose is cancelled out.")] [SerializeField]
        private Vector3 gravity = Vector3.zero;

        [Tooltip("The force apply to bones.")] [SerializeField]
        private Vector3 force = Vector3.zero;

        [Tooltip("如果这个值不是0，最后会添加一个新的节点")]
        [SerializeField] private float endLength = 0;
        
        [Tooltip("如果这个值不是zero，最后会添加一个新的节点")]
        [SerializeField] private Vector3 endOffset = Vector3.zero;
        
        [Tooltip("需要和骨骼交互的碰撞器")] [SerializeField]
        private DynamicBoneCollider[] colliderArray = null;

        [Tooltip("从指定的节点开始后面的子节点都不会进行物理模拟")][SerializeField]
        private Transform[] exclusionTransformArray = null;
        
        private float boneTotalLength;
        
        private float weight = 1.0f;

        [NonSerialized] public NativeArray<ParticleInfo> ParticleInfoArray;
        [NonSerialized] public Transform[] ParticleTransformArray;

        private int particleCount;
        private bool hasInitialized;

        /// <summary>
        /// 获取挂载该脚本的骨骼的父物体用于物理模拟，如果父物体为空，则此骨骼无效
        /// </summary>
        public Transform RootBoneParentTransform { get; private set; }

        /// <summary>
        /// 此HeadInfo对应的是RootBoneParentTransform
        /// </summary>
        [HideInInspector] public HeadInfo HeadInfo;

        private void OnValidate()
        {
            if(!RootBoneParentTransform) return;
            if (!hasInitialized) return;

            damping = Mathf.Clamp01(damping);
            elasticity = Mathf.Clamp01(elasticity);
            stiffness = Mathf.Clamp01(stiffness);
            inert = Mathf.Clamp01(inert);
            friction = Mathf.Clamp01(friction);
            radius = Mathf.Max(radius, 0);

            if (Application.isEditor && Application.isPlaying)
            {
                InitTransforms();
                UpdateParameters();
                DynamicBoneManager.Instance.RefreshHeadInfo(in HeadInfo);
                DynamicBoneManager.Instance.RefreshParticleInfo(in ParticleInfoArray,
                    in HeadInfo.DataOffsetInGlobalArray);
            }
        }

        private void Awake()
        {
            if (!rootBoneTransform)
            {
                rootBoneTransform = transform;
            }
            RootBoneParentTransform = rootBoneTransform.parent;
            if(!RootBoneParentTransform) return;
            
            ParticleInfoArray = new NativeArray<ParticleInfo>(DynamicBoneManager.MaxParticleLimit, Allocator.Persistent);
            ParticleTransformArray = new Transform[DynamicBoneManager.MaxParticleLimit];

            SetupParticles();
            DynamicBoneManager.Instance.AddBone(this);
            hasInitialized = true;
        }

        public HeadInfo ResetHeadIndexAndDataOffset(int headIndex)
        {
            HeadInfo.Index = headIndex;
            HeadInfo.DataOffsetInGlobalArray = headIndex * DynamicBoneManager.MaxParticleLimit;
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
            if (rootBoneTransform == null)
                return;

            particleCount = 0;
            HeadInfo = new HeadInfo
            {
                ObjectPrevPosition = rootBoneTransform.position,
                ObjectScale = math.abs(rootBoneTransform.lossyScale.x),
                Gravity = gravity,
                Weight = weight,
                Force = force,
                ParticleCount = 0,
            };

            particleCount = 0;
            boneTotalLength = 0;
            AppendParticles(rootBoneTransform, -1, 0, ref HeadInfo);
            UpdateParameters();
            // TestAppendParticles();

            HeadInfo.ParticleCount = particleCount;
        }

        private void TestAppendParticles()
        {
            List<Transform> particleTransformList = new List<Transform>();

            Transform currentTransform = rootBoneTransform;

            do
            {
                for (int i = 0; i < currentTransform.childCount; i++)
                {
                    particleTransformList.Add(currentTransform.GetChild(i));
                }

                currentTransform = currentTransform.GetChild(0);
            } while (currentTransform.childCount > 0);

            for (int i = 0; i < particleTransformList.Count; i++)
            {
                Debug.Log(particleTransformList[i].name);
            }
        }

        //TODO:这个循环需要整理一下，目前过于混乱了
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
            else
            {
                Transform pb = ParticleTransformArray[parentIndex];
                if (endLength > 0)
                {
                    Transform ppb = pb.parent;
                    if (ppb != null)
                        particle.EndOffset = pb.InverseTransformPoint((pb.position * 2 - ppb.position)) * endLength;
                    else
                        particle.EndOffset = new Vector3(endLength, 0, 0);
                }
                else
                {
                    particle.EndOffset =
                        pb.InverseTransformPoint(rootBoneTransform.TransformDirection(endOffset) + pb.position);
                }

                particle.IsEndBone = true;
                
                particle.TempWorldPosition = particle.TempPrevWorldPosition = pb.TransformPoint(particle.EndOffset);
            }
                   
            //两Particle成一根骨，从第二个Particle开始计算骨骼长度
            if (parentIndex >= 0)
            {
                boneLength += math.distance(ParticleTransformArray[parentIndex].position, particle.TempWorldPosition);
                particle.BoneLength = boneLength;
                boneTotalLength = math.max(boneTotalLength, boneLength);
            }
            
            int index = particle.Index;
            Debug.Log(index);
            ParticleInfoArray[particle.Index] = particle;
            ParticleTransformArray[particle.Index] = b;
            
            //TODO:目前只支持单链骨骼，即每个骨骼下只有一个子物体，待修改支持树形骨骼
            if (b != null)
            {
                for (int i = 0; i < b.childCount; ++i)
                {
                    bool exclude = false;
                    if (exclusionTransformArray != null)
                    {
                        for (int j = 0; j < exclusionTransformArray.Length; j++)
                        {
                            Transform e = exclusionTransformArray[j];
                            if (e == b.GetChild(i))
                            {
                                exclude = true;
                                break;
                            }
                        }
                    }

                    if (!exclude)
                    {
                        AppendParticles(b.GetChild(i), index, boneLength, ref head);
                    }
                    //如果到该节点被剔除了，仍需要在最后添加一个空节点
                    else if ( endLength > 0 || endOffset != Vector3.zero)
                    {
                        AppendParticles(null, index, boneLength, ref head);
                    }
                }

                if (b.childCount == 0 && (endLength > 0 || endOffset != Vector3.zero))
                {
                    AppendParticles(null, index, boneLength, ref head);
                }
            }
        }

        /// <summary>
        /// 用于将所有Particle恢复至最初位置
        /// </summary>
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
            if (rootBoneTransform == null)
                return;

            for (int i = 0; i < particleCount; ++i)
            {
                ParticleInfo particle = ParticleInfoArray[i];
                particle.Damping = damping;
                particle.Elasticity = elasticity;
                particle.Stiffness = stiffness;
                particle.Inert = inert;
                particle.Friction = friction;
                particle.Radius = radius;


                if (boneTotalLength > 0)
                {
                    float a = particle.BoneLength / boneTotalLength;

                    if (dampingDistribution != null && dampingDistribution.keys.Length > 0)
                    {
                        particle.Damping *= dampingDistribution.Evaluate(a);
                    }

                    if (elasticityDistribution != null && elasticityDistribution.keys.Length > 0)
                        particle.Elasticity *= elasticityDistribution.Evaluate(a);
                    if (stiffnessDistribution != null && stiffnessDistribution.keys.Length > 0)
                        particle.Stiffness *= stiffnessDistribution.Evaluate(a);
                    if (inertDistribution != null && inertDistribution.keys.Length > 0)
                        particle.Inert *= inertDistribution.Evaluate(a);
                    if (frictionDistribution != null && frictionDistribution.keys.Length > 0)
                        particle.Friction *= frictionDistribution.Evaluate(a);
                    if (radiusDistribution != null && radiusDistribution.keys.Length > 0)
                        particle.Radius *= radiusDistribution.Evaluate(a);
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

        public DynamicBoneCollider[] GetColliderArray()
        {
            return colliderArray;
        }

        private void OnDestroy()
        {
            ParticleInfoArray.Dispose();
        }
    }
}

