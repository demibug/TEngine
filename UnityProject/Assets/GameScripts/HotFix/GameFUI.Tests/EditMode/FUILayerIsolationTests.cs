using FairyGUI;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameFUI.Tests.EditMode
{
    /// <summary>
    /// FairyGUI 与 UGUI 的 Unity Layer 及相机遮罩隔离验证。
    /// </summary>
    [TestFixture]
    public class FUILayerIsolationTests
    {
        private const string FuiLayerName = "FUI";
        private const string UiLayerName = "UI";
        private const string MainScenePath = "Assets/Scenes/main.unity";

        [Test]
        public void LayerConfiguration_ReservesDistinctUIAndFUILayers()
        {
            int uiLayer = LayerMask.NameToLayer(UiLayerName);
            int fuiLayer = LayerMask.NameToLayer(FuiLayerName);

            Assert.AreEqual(5, uiLayer, "UGUI 应继续使用既有 UI Layer 5。");
            Assert.AreEqual(8, fuiLayer, "FairyGUI 应使用预留的 FUI Layer 8。");
            Assert.AreNotEqual(uiLayer, fuiLayer, "UGUI 与 FairyGUI 不应共用 Unity Layer。");
            Assert.AreEqual(FuiLayerName, StageCamera.LayerName,
                "StageCamera 应以 FUI 作为唯一渲染 Layer。");
        }

        [Test]
        public void StageCamera_CheckMainCamera_CorrectsExistingCameraMask()
        {
            int uiLayer = LayerMask.NameToLayer(UiLayerName);
            int fuiLayer = LayerMask.NameToLayer(FuiLayerName);
            int expectedMask = 1 << fuiLayer;

            GameObject existingObject = GameObject.Find(StageCamera.Name);
            bool createdByTest = existingObject == null;
            Camera stageCamera = null;
            int originalMask = 0;

            try
            {
                StageCamera.CheckMainCamera();
                stageCamera = GameObject.Find(StageCamera.Name).GetComponent<Camera>();
                Assert.IsNotNull(stageCamera, "Stage Camera 应带有 Camera 组件。");

                if (!createdByTest)
                {
                    originalMask = stageCamera.cullingMask;
                }

                stageCamera.cullingMask = 1 << uiLayer;
                StageCamera.CheckMainCamera();

                Assert.AreEqual(expectedMask, stageCamera.cullingMask,
                    "已有 Stage Camera 必须被校正为只渲染 FUI Layer。");
            }
            finally
            {
                if (createdByTest && stageCamera != null)
                {
                    Object.DestroyImmediate(stageCamera.gameObject);
                }
                else if (stageCamera != null)
                {
                    stageCamera.cullingMask = originalMask;
                }
            }
        }

        [Test]
        public void ProductionMainCamera_ExcludesFUILayer()
        {
            int fuiLayer = LayerMask.NameToLayer(FuiLayerName);
            int uiLayer = LayerMask.NameToLayer(UiLayerName);
            int fuiMask = 1 << fuiLayer;
            int uiMask = 1 << uiLayer;

            Scene scene = SceneManager.GetSceneByPath(MainScenePath);
            bool closeAfterTest = !scene.IsValid() || !scene.isLoaded;
            if (closeAfterTest)
            {
                scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Additive);
            }

            try
            {
                Camera mainCamera = FindMainCamera(scene);
                Assert.IsNotNull(mainCamera, "生产 main 场景必须存在带 MainCamera Tag 的相机。");
                Assert.AreEqual(0, mainCamera.cullingMask & fuiMask,
                    "世界 Main Camera 不应渲染 FUI Layer，避免与 StageCamera 重复绘制。");
                Assert.AreNotEqual(0, mainCamera.cullingMask & uiMask,
                    "本次隔离不应改变 Main Camera 对既有 UI Layer 的可见性。");
            }
            finally
            {
                if (closeAfterTest && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static Camera FindMainCamera(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Camera[] cameras = root.GetComponentsInChildren<Camera>(true);
                foreach (Camera camera in cameras)
                {
                    if (camera.CompareTag("MainCamera"))
                    {
                        return camera;
                    }
                }
            }

            return null;
        }
    }
}
