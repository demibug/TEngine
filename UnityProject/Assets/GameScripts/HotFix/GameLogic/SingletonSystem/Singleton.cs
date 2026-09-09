using System.Diagnostics;
using TEngine;

namespace GameLogic
{
    /// <summary>
    /// 全局对象必须继承于此。
    /// </summary>
    /// <typeparam name="T">子类类型。</typeparam>
    public abstract class Singleton<T> : ISingleton, ISingletonInstanceResetter where T : Singleton<T>, new()
    {
        protected static T _instance = default(T);

        public static T Instance
        {
            get
            {
                if (!ModuleSystem.IsRunning)
                {
                    throw new GameFrameworkException("Can not access a singleton while the module system is not running.");
                }

                SingletonSystem.RegisterSessionResetter(ResetForNewSession);
                if (null == _instance)
                {
                    T instance = new T();
                    _instance = instance;
                    try
                    {
                        instance.OnInit();
                        SingletonSystem.Retain(instance);
                    }
                    catch
                    {
                        _instance = null;
                        SingletonSystem.Release(instance);
                        throw;
                    }
                }

                return _instance;
            }
        }

        public static bool IsValid => _instance != null;

        protected Singleton()
        {
#if UNITY_EDITOR
            string st = new StackTrace().ToString();
            // using const string to compare simply
            if (!st.Contains("GameLogic.Singleton`1[T].get_Instance"))
            {
                UnityEngine.Debug.LogError($"请必须通过Instance方法来实例化{typeof(T).FullName}类");
            }
#endif
        }

        protected virtual void OnInit()
        {
        }

        public virtual void Active()
        {
        }

        public virtual void Release()
        {
            T instance = _instance;
            if (instance == null)
            {
                return;
            }

            try
            {
                OnRelease();
            }
            finally
            {
                SingletonSystem.Release(instance);
                _instance = null;
            }
        }

        protected virtual void OnRelease()
        {
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
