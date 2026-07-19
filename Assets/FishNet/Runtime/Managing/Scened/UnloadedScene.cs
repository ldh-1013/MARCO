using UnityEngine.SceneManagement;

namespace FishNet.Managing.Scened
{
    public struct UnloadedScene
    {
        public readonly string Name;

        /// <summary>
        /// Raw data of the scene handle (SceneHandle.GetRawData()).
        /// Unity 6000.5+ removed the implicit SceneHandle/int conversions, so the handle is
        /// stored as the full 64-bit raw value rather than a truncated int.
        /// </summary>
        public readonly ulong Handle;

        public UnloadedScene(Scene s)
        {
            Name = s.name;
            Handle = s.handle.GetRawData();
        }

        public UnloadedScene(string name, ulong handle)
        {
            Name = name;
            Handle = handle;
        }

        /// <summary>
        /// Returns a scene based on handle.
        /// Result may not be valid as some Unity versions discard of the scene information after unloading.
        /// </summary>
        /// <returns></returns>
        public Scene GetScene()
        {
            int loadedScenes = UnityEngine.SceneManagement.SceneManager.sceneCount;
            for (int i = 0; i < loadedScenes; i++)
            {
                Scene s = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (s.IsValid() && s.handle.GetRawData() == Handle)
                    return s;
            }

            return default;
        }
    }
}