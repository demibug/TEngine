using System.Collections;
using FairyGUI;
using NUnit.Framework;
using TEngine.FairyGUIIntegration;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace GameLogic.FairyGUI.PlayModeTests
{
    public sealed class FguiRuntimeHostTests
    {
        [UnityTest]
        public IEnumerator Host_UsesHigherCamera_BlocksOnlyFguiHits_AndSuspendsIdempotently()
        {
            FguiSettings settings = ScriptableObject.CreateInstance<FguiSettings>();
            FguiRuntimeHost host = null;
            try
            {
                host = FguiRuntimeHost.Create(settings, null);
                yield return null;

                Assert.That(StageCamera.main, Is.Not.Null);
                Assert.That(StageCamera.main.depth, Is.EqualTo(settings.StageCameraDepth));
                int renderLayer = LayerMask.NameToLayer(settings.RenderLayerName);
                Assert.That(renderLayer, Is.GreaterThanOrEqualTo(0));
                Assert.That(Stage.inst.layer, Is.EqualTo(renderLayer));
                Assert.That(StageCamera.main.cullingMask, Is.EqualTo(1 << renderLayer),
                    "FairyGUI must not share the UGUI camera's UI-layer culling mask.");

                GameObject shieldObject = GameObject.Find("TEngine.FairyGUI.InputShield");
                Assert.That(shieldObject, Is.Not.Null);
                Assert.That(shieldObject.GetComponent<Canvas>().sortingOrder, Is.GreaterThan(0));
                FguiInputBridge shield = shieldObject.GetComponent<FguiInputBridge>();

                GComponent blocker = new GComponent { opaque = true };
                GComponent layer = host.GetLayer(FguiLayer.UI);
                blocker.SetSize(layer.width, layer.height);
                layer.AddChild(blocker);

                Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                Assert.That(shield.IsRaycastLocationValid(center, null), Is.True,
                    "An opaque FairyGUI view must prevent the UGUI raycast from passing through.");

                blocker.RemoveFromParent();
                blocker.Dispose();
                Assert.That(shield.IsRaycastLocationValid(center, null), Is.False,
                    "Empty FairyGUI layers must not block UGUI.");

                var eventSystemObject = new GameObject("FGUI test EventSystem", typeof(EventSystem),
                    typeof(StandaloneInputModule));
                var selectedObject = new GameObject("FGUI test selection");
                yield return null;
                EventSystem.current.SetSelectedGameObject(selectedObject);
                host.SetModalActive(true);
                Assert.That(shield.IsRaycastLocationValid(center, null), Is.True);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.Null,
                    "Taking modal ownership must clear retained UGUI focus/interaction state.");
                host.SetModalActive(false);
                Object.Destroy(eventSystemObject);
                Object.Destroy(selectedObject);

                System.IDisposable first = host.SuspendPresentation();
                System.IDisposable second = host.SuspendPresentation();
                Assert.That(host.IsSuspended, Is.True);
                Assert.That(StageCamera.main.enabled, Is.False);
                Assert.That(shield.IsRaycastLocationValid(center, null), Is.False);
                first.Dispose();
                first.Dispose();
                Assert.That(host.IsSuspended, Is.True);
                second.Dispose();
                Assert.That(host.IsSuspended, Is.False);
                Assert.That(StageCamera.main.enabled, Is.True);

                Camera ownedCamera = StageCamera.main;
                StageEngine ownedEngine = Stage.inst.gameObject.GetComponent<StageEngine>();
                host.Shutdown();
                host = null;
                yield return null;
                Assert.That(ownedCamera.gameObject.activeSelf, Is.True,
                    "The owned camera object stays discoverable so FairyGUI cannot recreate a stray camera.");
                Assert.That(ownedCamera.enabled, Is.False);
                Assert.That(ownedEngine.enabled, Is.False);

                host = FguiRuntimeHost.Create(settings, null);
                yield return null;
                Assert.That(StageCamera.main, Is.SameAs(ownedCamera));
                Assert.That(ownedCamera.enabled, Is.True);
                Assert.That(ownedEngine.enabled, Is.True);
            }
            finally
            {
                if (host != null)
                    host.Shutdown();

                Object.Destroy(settings);
            }

            yield return null;
            Assert.That(GameObject.Find("TEngine.FairyGUI.InputShield"), Is.Null);
        }
    }
}
