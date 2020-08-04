using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Jobs;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;

public class DynamicBoneManager : MonoBehaviour
{
    private static DynamicBoneManager instance;

    public static DynamicBoneManager Instance
    {
        get
        {
            if (!instance) instance = new GameObject("DynamicBoneManager").AddComponent<DynamicBoneManager>();
            return instance;
        }
    }

    [BurstCompile]
    private struct RootSetupJob : IJobParallelForTransform
    {
        public NativeArray<HeadInfo> HeadArray;
        public bool IsFirstUpdate;

        public void Execute(int index, TransformAccess transform)
        {
            HeadInfo curHeadInfo = HeadArray[index];
            curHeadInfo.RootParentBoneWorldPos = transform.position;
            curHeadInfo.RootParentBoneWorldRot = transform.rotation;

            if (!IsFirstUpdate)
            {
                curHeadInfo.ObjectMove = transform.position - (Vector3) curHeadInfo.ObjectPrevPosition;
            }

            curHeadInfo.ObjectPrevPosition = transform.position;
            float3 force = curHeadInfo.Gravity;
            float3 forceDir = math.normalizesafe(force);
            float3 pf = forceDir * math.max(math.dot(force, forceDir),
                            0); // project current gravity to rest gravity
            force -= pf; // remove projected gravity
            force = (force + curHeadInfo.Force) * curHeadInfo.ObjectScale;
            curHeadInfo.FinalForce = force;

            HeadArray[index] = curHeadInfo;
        }
    }

    [BurstCompile]
    private struct UpdateParticles1Job : IJobParallelFor
    {
        [ReadOnly] public NativeArray<HeadInfo> HeadArray;
        public NativeArray<ParticleInfo> ParticleArray;

        public void Execute(int index)
        {
            int headIndex = index / DynamicBone.MaxParticleLimit;
            HeadInfo curHeadInfo = HeadArray[headIndex];

            int offset = index % DynamicBone.MaxParticleLimit;
            //每个Head及其Particle只需要计算一次就够了！
            if (offset == 0)
            {
                float3 parentPosition = curHeadInfo.RootParentBoneWorldPos;
                quaternion parentRotation = curHeadInfo.RootParentBoneWorldRot;

                for (int j = 0; j < curHeadInfo.ParticleCount; j++)
                {
                    int pIdx = curHeadInfo.DataOffsetInGlobalArray + j;
                    ParticleInfo p = ParticleArray[pIdx];

                    var localPosition = p.LocalPosition * p.ParentScale;
                    var localRotation = p.LocalRotation;
                    var worldPosition = parentPosition + math.mul(parentRotation, localPosition);
                    var worldRotation = math.mul(parentRotation, localRotation);

                    parentPosition = p.WorldPosition = worldPosition;
                    parentRotation = p.WorldRotation = worldRotation;

                    ParticleArray[pIdx] = p;
                }
            }

            //如果遍历到空的部分就直接跳过就好了
            if (offset >= curHeadInfo.ParticleCount) return;

            int particleId = curHeadInfo.DataOffsetInGlobalArray + offset;
            ParticleInfo particle = ParticleArray[particleId];


            if (particle.ParentIndex >= 0)
            {
                float3 v = particle.TempWorldPosition - particle.TempPrevWorldPosition;
                float3 rMove = curHeadInfo.ObjectMove * particle.Inert;
                particle.TempPrevWorldPosition = particle.TempWorldPosition + rMove;

                float damping = particle.Damping;
                if (particle.IsCollide)
                {
                    damping += particle.Friction;
                    if (damping > 1)
                        damping = 1;
                    particle.IsCollide = false;
                }

                particle.TempWorldPosition += v * (1 - damping) + curHeadInfo.FinalForce + rMove;
            }
            else
            {
                particle.TempPrevWorldPosition = particle.TempWorldPosition;
                particle.TempWorldPosition = particle.WorldPosition;
            }

            ParticleArray[particleId] = particle;
        }
    }

    [BurstCompile]
    private struct UpdateParticle2Job : IJobParallelFor
    {
        [ReadOnly] public NativeArray<HeadInfo> HeadArray;
        public NativeArray<ParticleInfo> ParticleArray;
        [ReadOnly] public NativeArray<ColliderInfo> ColliderArray;
        [ReadOnly] public NativeMultiHashMap<int, int> BoneColliderMatchMap;

        public void Execute(int index)
        {
            //避免IndexOutOfRangeException:Index {0} is out of restricted IJobParallelFor range [{1}...{2] in ReadWriteBuffer.
            if (index % DynamicBone.MaxParticleLimit == 0) return;

            int headIndex = index / DynamicBone.MaxParticleLimit;
            HeadInfo curHeadInfo = HeadArray[headIndex];

            //遍历到那些空的Particle就不用再计算了
            int offset = index % DynamicBone.MaxParticleLimit;
            if (offset >= curHeadInfo.ParticleCount) return;

            int particleId = curHeadInfo.DataOffsetInGlobalArray + offset;
            ParticleInfo particleInfo = ParticleArray[particleId];

            int parentParticleIndex = curHeadInfo.DataOffsetInGlobalArray + particleInfo.ParentIndex;
            ParticleInfo parentParticleInfo = ParticleArray[parentParticleIndex];
            float3 pos = particleInfo.WorldPosition;
            float3 parentPos = parentParticleInfo.WorldPosition;
            float restLen = math.distance(parentPos, pos);
            float stiffness = math.lerp(1.0f, particleInfo.Stiffness, curHeadInfo.Weight);
            if (stiffness > 0 || particleInfo.Elasticity > 0)
            {
                float4x4 em0 = float4x4.TRS(parentParticleInfo.TempWorldPosition, parentParticleInfo.WorldRotation,
                    particleInfo.ParentScale);
                float3 restPos = math.mul(em0, new float4(particleInfo.LocalPosition.xyz, 1)).xyz;
                float3 d = restPos - particleInfo.TempWorldPosition;
                particleInfo.TempWorldPosition += d * particleInfo.Elasticity;
                if (stiffness > 0)
                {
                    d = restPos - particleInfo.TempWorldPosition;
                    float len = math.length(d);
                    float maxLen = restLen * (1 - stiffness) * 2;
                    if (len > maxLen)
                        particleInfo.TempWorldPosition += d * ((len - maxLen) / len);
                }
            }
            
            //碰撞检测, 获取根骨骼所对应的所有碰撞器并进行计算
            NativeMultiHashMap<int, int>.Enumerator enumerator = BoneColliderMatchMap.GetValuesForKey(headIndex);
            while (enumerator.MoveNext())
            {
                particleInfo.IsCollide =
                    DynamicBoneUtility.HandleCollision(ColliderArray[enumerator.Current],
                        ref particleInfo.TempWorldPosition,
                        in particleInfo.Radius);
            }
            
            for (int i = 0; i < ColliderArray.Length; i++)
            {
                particleInfo.IsCollide =
                    DynamicBoneUtility.HandleCollision(ColliderArray[i],
                        ref particleInfo.TempWorldPosition,
                        in particleInfo.Radius);
            }

            float3 dd = parentParticleInfo.TempWorldPosition - particleInfo.TempWorldPosition;
            float leng = math.length(dd);
            if (leng > 0)
            {
                particleInfo.TempWorldPosition += dd * ((leng - restLen) / leng);
            }



//            particleInfo.WorldPosition = particleInfo.TempWorldPosition;
            ParticleArray[particleId] = particleInfo;
        }
    }

    [BurstCompile]
    private struct ApplyToTransformJob : IJobParallelForTransform
    {
        public NativeArray<ParticleInfo> ParticleArray;

        public void Execute(int index, TransformAccess transform)
        {
            ParticleInfo particleInfo = ParticleArray[index];
            particleInfo.WorldPosition = particleInfo.TempWorldPosition;
            transform.position = particleInfo.WorldPosition;
            transform.rotation = particleInfo.WorldRotation;
            ParticleArray[index] = particleInfo;
        }
    }

    private List<DynamicBone> dynamicBoneList;
    private NativeList<HeadInfo> headInfoList;
    private NativeList<ParticleInfo> particleInfoList;
    private NativeList<ColliderInfo> colliderInfoList;
    private NativeMultiHashMap<int, int> boneColliderMatchMap;
    private TransformAccessArray headTransformAccessArray;
    private TransformAccessArray particleTransformAccessArray;
    private bool isFirstUpdate;

    private void Awake()
    {
        dynamicBoneList = new List<DynamicBone>();

        headInfoList = new NativeList<HeadInfo>(200, Allocator.Persistent);
        headTransformAccessArray = new TransformAccessArray(200, 64);

        particleInfoList = new NativeList<ParticleInfo>(Allocator.Persistent);
        particleTransformAccessArray = new TransformAccessArray(200 * DynamicBone.MaxParticleLimit, 64);

        colliderInfoList = new NativeList<ColliderInfo>(10, Allocator.Persistent);
        boneColliderMatchMap = new NativeMultiHashMap<int, int>(200, Allocator.Persistent);
        isFirstUpdate = true;
    }


    private void LateUpdate()
    {
        int runningDynamicBoneCount = headInfoList.Length;
        if (runningDynamicBoneCount == 0) return;

        int dataArrayLength = runningDynamicBoneCount * DynamicBone.MaxParticleLimit;

        JobHandle dependency;

        dependency = new RootSetupJob
        {
            HeadArray = headInfoList,
            IsFirstUpdate = isFirstUpdate
        }.Schedule(headTransformAccessArray);

        dependency = new UpdateParticles1Job
        {
            HeadArray = headInfoList,
            ParticleArray = particleInfoList,
        }.Schedule(dataArrayLength, DynamicBone.MaxParticleLimit, dependency);

        dependency = new UpdateParticle2Job
        {
            HeadArray = headInfoList,
            ParticleArray = particleInfoList,
            ColliderArray = colliderInfoList,
            BoneColliderMatchMap = boneColliderMatchMap
        }.Schedule(dataArrayLength, DynamicBone.MaxParticleLimit, dependency);

        dependency = new ApplyToTransformJob
        {
            ParticleArray = particleInfoList,
        }.Schedule(particleTransformAccessArray, dependency);

        dependency.Complete();
    }

    public void AddBone(DynamicBone target)
    {
        int index = dynamicBoneList.IndexOf(target);
        if (index != -1) return; //判断该bone是否已经被添加过了

        dynamicBoneList.Add(target);

        target.HeadInfo.DataOffsetInGlobalArray = particleInfoList.Length;

        int headIndex = headInfoList.Length;
        target.HeadInfo.Index = headIndex;

        //添加Bone和Collider的关系
        foreach (var c in target.Colliders)
        {
            boneColliderMatchMap.Add(headIndex, c.ColliderInfo.Index);
        }

        headInfoList.Add(target.HeadInfo);
        particleInfoList.AddRange(target.ParticleInfoArray);
        headTransformAccessArray.Add(target.RootParentTransform);
        for (int i = 0; i < DynamicBone.MaxParticleLimit; i++)
        {
            particleTransformAccessArray.Add(target.ParticleTransformArray[i]);
        }
    }

    public void RemoveBone(DynamicBone target)
    {
        int index = dynamicBoneList.IndexOf(target);
        if (index == -1) return; //判断该bone是否存在于当前集合中

        dynamicBoneList.RemoveAt(index);
        int curHeadIndex = target.HeadInfo.Index;

        //移除Bone的相关Collider的关系
        boneColliderMatchMap.Remove(curHeadIndex);

        //是否是队列中末尾对象
        bool isEndTarget = curHeadIndex == headInfoList.Length - 1;
        if (isEndTarget)
        {
            headInfoList.RemoveAtSwapBack(curHeadIndex);
            headTransformAccessArray.RemoveAtSwapBack(curHeadIndex);
            for (int i = DynamicBone.MaxParticleLimit - 1; i >= 0; i--)
            {
                int dataOffset = curHeadIndex * DynamicBone.MaxParticleLimit + i;
                particleInfoList.RemoveAtSwapBack(dataOffset);
                particleTransformAccessArray.RemoveAtSwapBack(dataOffset);
            }
        }
        else
        {
            //将最末列的HeadInfo 索引设置为当前将要移除的HeadInfo 索引
            DynamicBone lastTarget = dynamicBoneList[dynamicBoneList.Count - 1];
            HeadInfo lastHeadInfo = lastTarget.ResetHeadIndexAndDataOffset(curHeadIndex);
            headInfoList.RemoveAtSwapBack(curHeadIndex);
            headInfoList[curHeadIndex] = lastHeadInfo;
            headTransformAccessArray.RemoveAtSwapBack(curHeadIndex);
            for (int i = DynamicBone.MaxParticleLimit - 1; i >= 0; i--)
            {
                int dataOffset = curHeadIndex * DynamicBone.MaxParticleLimit + i;
                particleInfoList.RemoveAtSwapBack(dataOffset);
                particleTransformAccessArray.RemoveAtSwapBack(dataOffset);
            }
        }

        target.ClearJobData();

        if (isFirstUpdate) isFirstUpdate = false;
    }


    public void RefreshHeadInfo(in HeadInfo headInfo)
    {
        headInfoList[headInfo.Index] = headInfo;
    }

    public void RefreshParticleInfo(in NativeArray<ParticleInfo> particleInfoArray, in int headOffsetInGlobalArray)
    {
        for (int i = headOffsetInGlobalArray; i < particleInfoArray.Length + headOffsetInGlobalArray; i++)
        {
            particleInfoList[i] = particleInfoArray[i - headOffsetInGlobalArray];
        }
    }

    public void AddCollider(DynamicBoneCollider target)
    {
        colliderInfoList.Add(target.ColliderInfo);
    }

    public void RefreshColliderInfo(in ColliderInfo colliderInfo)
    {
        colliderInfoList[colliderInfo.Index] = colliderInfo;
    }


    private void OnDestroy()
    {
        if (particleTransformAccessArray.isCreated) particleTransformAccessArray.Dispose();
        if (particleInfoList.IsCreated) particleInfoList.Dispose();
        if (headInfoList.IsCreated) headInfoList.Dispose();
        if (colliderInfoList.IsCreated) colliderInfoList.Dispose();
        if (headTransformAccessArray.isCreated) headTransformAccessArray.Dispose();
        if (boneColliderMatchMap.IsCreated) boneColliderMatchMap.Dispose();
    }
}