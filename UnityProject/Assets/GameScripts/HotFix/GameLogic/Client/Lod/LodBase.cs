using UnityEngine;

namespace GameLogic
{
    public class LodBase : MonoBehaviour
    {
        public float[] m_lodArray;

        private bool m_hasRegistered;

        protected LodBase()
        {
        }
        
        public virtual void UpdateLod()
        {
        }
        
        private void OnEnable()
        {
            UpdateLod();
        }
        
        protected void Start()
        {
            if (!m_hasRegistered)
            {
                LodMgr.Instance.AddHandler(UpdateLod);
                m_hasRegistered = true;
            }
        }

        protected void OnDestroy()
        {
            LodMgr.Instance.RemoveHandler(UpdateLod);
            m_hasRegistered = false;
        }

        protected void OnSpawn()
        {
            if (!m_hasRegistered)
            {
                LodMgr.Instance.AddHandler(UpdateLod);
                m_hasRegistered = true;
            }
        }

        protected void OnDespawn()
        {
            LodMgr.Instance.RemoveHandler(UpdateLod);
            m_hasRegistered = false;
        }

        protected bool IsLodChanged()
        {
            float previousLodDistance = LodMgr.Instance.GetPreviousLodDistance();
            float lodDistance = LodMgr.Instance.GetLodDistance();
            for (int i = 0; i < m_lodArray.Length; i++)
            {
                if ((lodDistance > m_lodArray[i] && previousLodDistance <= m_lodArray[i]) || (lodDistance <= m_lodArray[i] && previousLodDistance > m_lodArray[i]))
                {
                    return true;
                }
            }

            return false;
        }

        protected int GetCurrentLodLevel()
        {
            if (m_lodArray == null)
            {
                return 0;
            }
            
            float lodDistance = LodMgr.Instance.GetLodDistance();
            for (int i = 0; i < m_lodArray.Length; i++)
            {
                if (lodDistance <= m_lodArray[i])
                {
                    return i;
                }
            }
            
            return m_lodArray.Length - 1;
        }

        protected int GetPreviousLodLevel()
        {
            float previousLodDistance = LodMgr.Instance.GetPreviousLodDistance();
            for (int i = 0; i < m_lodArray.Length; i++)
            {
                if (previousLodDistance <= m_lodArray[i])
                {
                    return i;
                }
            }
            
            return m_lodArray.Length - 1;
        }
    }
}