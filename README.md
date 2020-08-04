# UnityHighPerformanceDynamicBone
参照DynamicBone插件的思路，使用JobSystem和BurstCompiler对其进行了优化，在使用方法上与该插件非常相似，但是拥有超高的性能！

改善与新增：
有了高性能加持，部分引用较多的Collider可以直接设置成Global供所有碰撞器使用，免去反复拖拽的工作。

目前已知问题：
暂时只支持单链骨骼，有分叉的会出问题。
暂不能完美支持运行时修改参数，会出现抖动，不过用来调试确定参数是可以用的。
