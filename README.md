# UnityHighPerformanceDynamicBone
参照DynamicBone插件的思路，使用JobSystem和BurstCompiler对其进行了优化，在使用方法上与该插件非常相似，但是拥有超高的性能！

Unity为我们提供的JobSystem和BurstCompiler已经很成熟了，完全可以用在项目之中。同时这个项目也是一个用来学习DOTS其中两员大将的非常不错的选择。（什么？都2020年了你居然还不会在Unity使用多线程？）

同时本项目也使用新的数学库Unity.Mathmatics对以前的Vector和transform下的方法进行了修改。



改善与新增：
* 有了高性能加持，部分引用较多的Collider可以直接设置成Global供所有碰撞器使用，免去反复拖拽的工作。

目前已知问题：
* 暂时只支持单链骨骼，有分叉的会出问题。
* 暂不能完美支持运行时修改参数，会出现抖动，不过用来调试确定参数是可以用的。


更新：

* 2020.8.4：将Collider也使用TransformAccess在Job中进行优化