using TEngine;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// 全局MonoBehavior必须继承于此
    /// </summary>
    /// <typeparam name="T">子类类型</typeparam>
    public class SingletonBehaviour<T> : MonoBehaviour, ISingletonInstanceResetter where T : SingletonBehaviour<T>
    {
        private static T _instance;

        private void Awake()
        {
            if (CheckInstance())
            {
                OnLoad();
            }
        }

        private bool CheckInstance()
        {
            if (!ModuleSystem.IsRunning)
            {
                Destroy(gameObject);
                return false;
            }

            if (this == Instance)
            {
                return true;
            }

            Object.Destroy(gameObject);
            return false;
        }

        protected virtual void OnLoad()
        {
        }

        protected virtual void OnDestroy()
        {
            if (this == _instance)
            {
                Release();
            }
        }

        /// <summary>
        /// 判断对象是否有效
        /// </summary>
        public static bool IsValid
        {
            get { return _instance != null; }
        }

        public static T Active()
        {
            return Instance;
        }

        public static void Release()
        {
            T instance = _instance;
            if (ReferenceEquals(instance, null))
            {
                return;
            }

            try
            {
                SingletonSystem.Release(instance.gameObject, instance);
            }
            finally
            {
                _instance = null;
            }
        }

        /// <summary>
        /// 实例
        /// </summary>
        public static T Instance
        {
            get
            {
                if (!ModuleSystem.IsRunning)
                {
                    throw new GameFrameworkException("Can not access a singleton behaviour while the module system is not running.");
                }

                SingletonSystem.RegisterSessionResetter(ResetForNewSession);
                if (_instance == null)
                {
                    System.Type thisType = typeof(T);
                    string instName = thisType.Name;
                    GameObject go = SingletonSystem.GetGameObject(instName);
                    if (go == null)
                    {
                        go = GameObject.Find($"/{instName}");
                        if (go == null)
                        {
                            go = new GameObject(instName);
                            go.transform.position = Vector3.zero;
                        }
                    }

                    if (go != null)
                    {
                        _instance = go.GetComponent<T>();
                        if (_instance == null)
                        {
                            _instance = go.AddComponent<T>();
                        }
                    }

                    if (_instance == null)
                    {
                        Log.Fatal($"Can't create SingletonBehaviour<{typeof(T)}>");
                    }

                    SingletonSystem.Retain(go, _instance);
                }

                return _instance;
            }
        }

        void ISingletonInstanceResetter.ClearInstanceForShutdown()
        {
            if (ReferenceEquals(_instance, this))
            {
                _instance = null;
            }
        }

        private static void ResetForNewSession()
        {
            _instance = null;
        }
    }
}
