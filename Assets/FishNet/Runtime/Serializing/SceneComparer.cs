using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace FishNet.Serializing.Helping
{
    internal sealed class SceneHandleEqualityComparer : EqualityComparer<Scene>
    {
        public override bool Equals(Scene a, Scene b)
        {
            return a.handle == b.handle;
        }

        public override int GetHashCode(Scene obj)
        {
            // Unity 6000.5+: Scene.handle is SceneHandle, no longer implicitly an int.
            return obj.handle.GetHashCode();
        }
    }
}